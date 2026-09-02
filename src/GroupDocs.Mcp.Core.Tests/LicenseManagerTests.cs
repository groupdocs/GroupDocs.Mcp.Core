using GroupDocs.Mcp.Core.Licensing;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xunit;

namespace GroupDocs.Mcp.Core.Tests;

/// <summary>
/// Licensing resolution, precedence and — importantly — key redaction.
/// </summary>
/// <remarks>
/// Environment variables are process-global, so the env-var cases share a collection to keep
/// them off the parallel path and always restore what they changed.
/// </remarks>
public class LicenseManagerTests
{
    // ---- test double -------------------------------------------------------

    private sealed class TestLicenseManager : LicenseManager
    {
        private readonly Func<MeteredConsumption>? _consumption;

        public TestLicenseManager(
            McpConfig config, ILogger<LicenseManager> logger, Func<MeteredConsumption>? consumption = null)
            : base(Options.Create(config), logger)
            => _consumption = consumption;

        public string? LicensePathApplied { get; private set; }
        public string? PublicKeyApplied { get; private set; }
        public string? PrivateKeyApplied { get; private set; }

        protected override void SetLicenseFromPath(string licensePath) => LicensePathApplied = licensePath;

        protected override void SetMeteredKeyCore(string publicKey, string privateKey)
        {
            PublicKeyApplied = publicKey;
            PrivateKeyApplied = privateKey;
        }

        protected override MeteredConsumption ReadConsumptionCore()
            => _consumption?.Invoke() ?? new MeteredConsumption { Quantity = 1m, Credit = 2m };
    }

    private sealed class CapturingLogger : ILogger<LicenseManager>
    {
        public List<string> Messages { get; } = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(
            LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
            => Messages.Add(formatter(state, exception));
    }

    private const string PublicKey = "PUB-abcdef123456";
    private const string PrivateKey = "PRIV-super-secret-value-9876";

    private static (TestLicenseManager Manager, CapturingLogger Logger) Create(
        McpConfig config, Func<MeteredConsumption>? consumption = null)
    {
        var logger = new CapturingLogger();
        return (new TestLicenseManager(config, logger, consumption), logger);
    }

    // ---- resolution order --------------------------------------------------

    [Fact]
    public void SetLicense_WithNothingConfigured_IsEvaluation()
    {
        var (manager, _) = Create(new McpConfig());

        manager.SetLicense();

        Assert.Equal(LicenseMode.Evaluation, manager.Mode);
        Assert.False(manager.IsLicensed);
    }

    [Fact]
    public void SetLicense_WithBothMeteredKeys_IsMetered()
    {
        var (manager, _) = Create(new McpConfig
        {
            MeteredPublicKey = PublicKey,
            MeteredPrivateKey = PrivateKey
        });

        manager.SetLicense();

        Assert.Equal(LicenseMode.Metered, manager.Mode);
        Assert.True(manager.IsLicensed);
        Assert.Equal(PublicKey, manager.PublicKeyApplied);
        Assert.Equal(PrivateKey, manager.PrivateKeyApplied);
    }

    [Theory]
    [InlineData(PublicKey, null)]
    [InlineData(null, PrivateKey)]
    public void SetLicense_WithOnlyOneMeteredKey_FallsBackAndWarns(string? publicKey, string? privateKey)
    {
        var (manager, logger) = Create(new McpConfig
        {
            MeteredPublicKey = publicKey,
            MeteredPrivateKey = privateKey
        });

        manager.SetLicense();

        // Half-configured must never look like success.
        Assert.Equal(LicenseMode.Evaluation, manager.Mode);
        Assert.False(manager.IsLicensed);
        Assert.Null(manager.PublicKeyApplied);
        Assert.Contains(logger.Messages, m => m.Contains("needs both"));
    }

    [Fact]
    public void SetLicense_WithLicenseFileOnly_IsLicensed()
    {
        var path = Path.GetTempFileName();
        try
        {
            var (manager, _) = Create(new McpConfig { LicensePath = path });

            manager.SetLicense();

            Assert.Equal(LicenseMode.Licensed, manager.Mode);
            Assert.True(manager.IsLicensed);
            Assert.Equal(path, manager.LicensePathApplied);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void SetLicense_WithMissingLicenseFile_IsEvaluation()
    {
        var (manager, _) = Create(new McpConfig
        {
            LicensePath = Path.Combine(Path.GetTempPath(), $"absent-{Guid.NewGuid():N}.lic")
        });

        manager.SetLicense();

        Assert.Equal(LicenseMode.Evaluation, manager.Mode);
        Assert.False(manager.IsLicensed);
    }

    [Fact]
    public void SetLicense_WithBothMeteredAndLicenseFile_PrefersMeteredAndWarns()
    {
        var path = Path.GetTempFileName();
        try
        {
            var (manager, logger) = Create(new McpConfig
            {
                LicensePath = path,
                MeteredPublicKey = PublicKey,
                MeteredPrivateKey = PrivateKey
            });

            manager.SetLicense();

            Assert.Equal(LicenseMode.Metered, manager.Mode);
            Assert.Null(manager.LicensePathApplied);
            Assert.Contains(logger.Messages, m => m.Contains("license file is ignored"));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void SetLicense_IsAppliedOnlyOnce()
    {
        var (manager, logger) = Create(new McpConfig());

        manager.SetLicense();
        manager.SetLicense();
        manager.SetLicense();

        Assert.Single(logger.Messages);
    }

    // ---- key redaction -----------------------------------------------------

    [Fact]
    public void SetLicense_NeverLogsThePrivateKey()
    {
        var (manager, logger) = Create(new McpConfig
        {
            MeteredPublicKey = PublicKey,
            MeteredPrivateKey = PrivateKey
        });

        manager.SetLicense();

        var everything = string.Join("\n", logger.Messages);
        Assert.DoesNotContain(PrivateKey, everything);
        // Not even a fragment of it.
        Assert.DoesNotContain("super-secret", everything);
        // The public key is masked to a prefix, not printed whole.
        Assert.DoesNotContain(PublicKey, everything);
        Assert.Contains("PUB-", everything);
    }

    // ---- consumption -------------------------------------------------------

    [Fact]
    public void GetConsumption_OutsideMeteredMode_IsNull()
    {
        var (manager, _) = Create(new McpConfig());
        manager.SetLicense();

        // Deliberately null rather than zero: "0 consumed" is a plausible-looking lie
        // when the concept does not apply.
        Assert.Null(manager.GetConsumption());
    }

    [Fact]
    public void GetConsumption_InMeteredMode_ReturnsReading()
    {
        var (manager, _) = Create(
            new McpConfig { MeteredPublicKey = PublicKey, MeteredPrivateKey = PrivateKey },
            () => new MeteredConsumption { Quantity = 142.5m, Credit = 857.5m });
        manager.SetLicense();

        var consumption = manager.GetConsumption();

        Assert.NotNull(consumption);
        Assert.Equal(142.5m, consumption!.Quantity);
        Assert.Equal(857.5m, consumption.Credit);
        Assert.Null(consumption.Error);
    }

    [Fact]
    public void GetConsumption_WhenEngineThrows_ReportsErrorNotZero()
    {
        var (manager, _) = Create(
            new McpConfig { MeteredPublicKey = PublicKey, MeteredPrivateKey = PrivateKey },
            () => throw new HttpRequestException("no route to host"));
        manager.SetLicense();

        var consumption = manager.GetConsumption();

        Assert.NotNull(consumption);
        Assert.Null(consumption!.Quantity);
        Assert.Null(consumption.Credit);
        Assert.Contains("no route to host", consumption.Error);
    }

    // ---- environment variables --------------------------------------------

    [Fact]
    public void SetLicense_ReadsMeteredKeysFromEnvironment()
    {
        var savedPublic = Environment.GetEnvironmentVariable(LicenseManager.PublicKeyVariable);
        var savedPrivate = Environment.GetEnvironmentVariable(LicenseManager.PrivateKeyVariable);
        try
        {
            Environment.SetEnvironmentVariable(LicenseManager.PublicKeyVariable, PublicKey);
            Environment.SetEnvironmentVariable(LicenseManager.PrivateKeyVariable, PrivateKey);

            var (manager, _) = Create(new McpConfig());
            manager.SetLicense();

            Assert.Equal(LicenseMode.Metered, manager.Mode);
            Assert.Equal(PublicKey, manager.PublicKeyApplied);
        }
        finally
        {
            Environment.SetEnvironmentVariable(LicenseManager.PublicKeyVariable, savedPublic);
            Environment.SetEnvironmentVariable(LicenseManager.PrivateKeyVariable, savedPrivate);
        }
    }

    [Fact]
    public void SetLicense_ConfigTakesPrecedenceOverEnvironment()
    {
        var savedPublic = Environment.GetEnvironmentVariable(LicenseManager.PublicKeyVariable);
        var savedPrivate = Environment.GetEnvironmentVariable(LicenseManager.PrivateKeyVariable);
        try
        {
            Environment.SetEnvironmentVariable(LicenseManager.PublicKeyVariable, "ENV-PUBLIC");
            Environment.SetEnvironmentVariable(LicenseManager.PrivateKeyVariable, "ENV-PRIVATE");

            var (manager, _) = Create(new McpConfig
            {
                MeteredPublicKey = PublicKey,
                MeteredPrivateKey = PrivateKey
            });
            manager.SetLicense();

            Assert.Equal(PublicKey, manager.PublicKeyApplied);
            Assert.Equal(PrivateKey, manager.PrivateKeyApplied);
        }
        finally
        {
            Environment.SetEnvironmentVariable(LicenseManager.PublicKeyVariable, savedPublic);
            Environment.SetEnvironmentVariable(LicenseManager.PrivateKeyVariable, savedPrivate);
        }
    }

    // ---- honest diagnostics ------------------------------------------------

    [Fact]
    public void SetLicense_WhenEngineRejectsMeteredKeys_SaysSoInsteadOfClaimingNothingConfigured()
    {
        var logger = new CapturingLogger();
        var manager = new RejectingLicenseManager(
            new McpConfig { MeteredPublicKey = PublicKey, MeteredPrivateKey = PrivateKey }, logger);

        manager.SetLicense();

        Assert.Equal(LicenseMode.Evaluation, manager.Mode);
        // "No license configured" would be false here and would send a customer looking in
        // entirely the wrong place - the keys WERE supplied, the engine refused them.
        Assert.NotNull(manager.Diagnostic);
        Assert.Contains("rejected them", manager.Diagnostic);
        // The rejection reason must not leak the private key.
        Assert.DoesNotContain(PrivateKey, manager.Diagnostic);
    }

    [Fact]
    public void SetLicense_WithOnlyOneKey_RecordsDiagnostic()
    {
        var (manager, _) = Create(new McpConfig { MeteredPublicKey = PublicKey });

        manager.SetLicense();

        Assert.NotNull(manager.Diagnostic);
        Assert.Contains("needs both", manager.Diagnostic);
    }

    [Fact]
    public void SetLicense_WithMissingLicenseFile_RecordsDiagnostic()
    {
        var (manager, _) = Create(new McpConfig
        {
            LicensePath = Path.Combine(Path.GetTempPath(), $"absent-{Guid.NewGuid():N}.lic")
        });

        manager.SetLicense();

        Assert.NotNull(manager.Diagnostic);
        Assert.Contains("not found", manager.Diagnostic);
    }

    [Fact]
    public void SetLicense_WhenAllIsWell_HasNoDiagnostic()
    {
        var (manager, _) = Create(new McpConfig
        {
            MeteredPublicKey = PublicKey,
            MeteredPrivateKey = PrivateKey
        });

        manager.SetLicense();

        Assert.Equal(LicenseMode.Metered, manager.Mode);
        Assert.Null(manager.Diagnostic);
    }

    private sealed class RejectingLicenseManager : LicenseManager
    {
        public RejectingLicenseManager(McpConfig config, ILogger<LicenseManager> logger)
            : base(Options.Create(config), logger) { }

        protected override void SetLicenseFromPath(string licensePath) { }

        // Mirrors the real engines, which validate the key pair inside SetMeteredKey.
        protected override void SetMeteredKeyCore(string publicKey, string privateKey)
            => throw new InvalidOperationException("Invalid metered key.");

        protected override MeteredConsumption ReadConsumptionCore() => new();
    }
}
