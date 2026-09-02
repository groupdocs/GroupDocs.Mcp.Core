using System.Reflection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace GroupDocs.Mcp.Core.Licensing;

/// <summary>
/// Resolves licensing from config or environment variables and tracks the active mode.
/// </summary>
/// <remarks>
/// Core owns the plumbing — reading the environment, precedence, the once-only guard, the
/// half-configured and both-configured warnings, and key redaction. The product supplies only
/// the two calls Core cannot make itself, because Core deliberately references no GroupDocs
/// product package: <see cref="SetLicenseFromPath"/> and <see cref="SetMeteredKeyCore"/>.
///
/// Resolution order: metered key pair, then license file, then evaluation.
/// </remarks>
public abstract class LicenseManager : ILicenseManager
{
    /// <summary>Environment variable holding the metered public key.</summary>
    public const string PublicKeyVariable = "GROUPDOCS_METERED_PUBLIC_KEY";

    /// <summary>Environment variable holding the metered private key. Treat as a secret.</summary>
    public const string PrivateKeyVariable = "GROUPDOCS_METERED_PRIVATE_KEY";

    /// <summary>Environment variable holding the path to a classic license file.</summary>
    public const string LicensePathVariable = "GROUPDOCS_LICENSE_PATH";

    private readonly McpConfig _config;
    private readonly ILogger<LicenseManager> _logger;
    private bool _initialized;

    public bool IsLicensed { get; private set; }

    public LicenseMode Mode { get; private set; } = LicenseMode.Evaluation;

    public string? Diagnostic { get; private set; }

    protected LicenseManager(IOptions<McpConfig> config, ILogger<LicenseManager> logger)
    {
        _config = config.Value;
        _logger = logger;
    }

    public void SetLicense()
    {
        if (_initialized) return;
        _initialized = true;

        if (TryApplyMetered()) return;
        if (TryApplyLicenseFile()) return;

        _logger.LogWarning(
            "No license configured. Running in evaluation mode - output may carry evaluation " +
            "limitations. Set {PublicKey} and {PrivateKey} for metered licensing, or {LicensePath} " +
            "for a license file.",
            PublicKeyVariable, PrivateKeyVariable, LicensePathVariable);
    }

    public MeteredConsumption? GetConsumption()
    {
        if (Mode != LicenseMode.Metered) return null;

        try
        {
            return ReadConsumptionCore();
        }
        catch (Exception ex)
        {
            // Never fabricate a zero — a plausible-looking number is worse than an honest failure.
            // The engine call may reach GroupDocs servers, so this can fail for network reasons.
            _logger.LogWarning(ex, "Failed to read metered consumption.");
            return MeteredConsumption.Failed($"{ex.GetType().Name}: {ex.Message}");
        }
    }

    private bool TryApplyMetered()
    {
        var publicKey = Coalesce(_config.MeteredPublicKey, PublicKeyVariable);
        var privateKey = Coalesce(_config.MeteredPrivateKey, PrivateKeyVariable);

        var hasPublic = !string.IsNullOrWhiteSpace(publicKey);
        var hasPrivate = !string.IsNullOrWhiteSpace(privateKey);

        if (!hasPublic || !hasPrivate)
        {
            // Half-configured is worth naming. Silently falling through to the license file or
            // to evaluation is how a customer ends up believing they are billed per use when
            // they are not.
            if (hasPublic || hasPrivate)
            {
                var configured = hasPublic ? PublicKeyVariable : PrivateKeyVariable;
                Diagnostic =
                    $"Only {configured} is set. Metered licensing needs both {PublicKeyVariable} " +
                    $"and {PrivateKeyVariable}; the metered configuration was ignored.";
                _logger.LogWarning(
                    "Only {Configured} is set. Metered licensing needs both {PublicKey} and " +
                    "{PrivateKey}; ignoring the metered configuration.",
                    configured, PublicKeyVariable, PrivateKeyVariable);
            }
            return false;
        }

        if (ResolveLicensePath() is not null)
        {
            _logger.LogWarning(
                "Both metered keys and a license file are configured. Using metered licensing; " +
                "the license file is ignored.");
        }

        try
        {
            SetMeteredKeyCore(publicKey!.Trim(), privateKey!.Trim());
            IsLicensed = true;
            Mode = LicenseMode.Metered;
            // The public key is masked to a short prefix; the private key is never logged.
            _logger.LogInformation(
                "Metered licensing active (public key {PublicKeyPrefix}).", Mask(publicKey!));
            return true;
        }
        catch (Exception ex)
        {
            // The engine validates the key pair here, so an invalid or expired key lands in this
            // branch. Record why: without it the status would read "No license configured", which
            // is false and would send a customer looking in the wrong place.
            Diagnostic =
                $"Metered keys were supplied but the engine rejected them ({ex.GetType().Name}: " +
                $"{ex.Message}). Falling back.";
            _logger.LogWarning(ex, "Failed to apply the metered key. Falling back.");
            return false;
        }
    }

    private bool TryApplyLicenseFile()
    {
        var licensePath = ResolveLicensePath();
        if (licensePath is null) return false;

        if (!File.Exists(licensePath))
        {
            Diagnostic = $"License file not found at '{licensePath}'.";
            _logger.LogWarning(
                "License file not found at {LicensePath}. Running in evaluation mode.", licensePath);
            return false;
        }

        try
        {
            SetLicenseFromPath(licensePath);
            IsLicensed = true;
            Mode = LicenseMode.Licensed;
            _logger.LogInformation("License applied from {LicensePath}.", licensePath);
            return true;
        }
        catch (Exception ex)
        {
            Diagnostic =
                $"License file '{licensePath}' could not be applied ({ex.GetType().Name}: {ex.Message}).";
            _logger.LogWarning(
                ex, "Failed to apply license from {LicensePath}. Running in evaluation mode.", licensePath);
            return false;
        }
    }

    private string? ResolveLicensePath()
    {
        var path = Coalesce(_config.LicensePath, LicensePathVariable);
        return string.IsNullOrWhiteSpace(path) ? null : path;
    }

    public (string Name, string Version)? DescribeEngine()
    {
        var assembly = EngineMarkerType?.Assembly;
        if (assembly is null) return null;

        var name = assembly.GetName();
        var version = assembly
                          .GetCustomAttribute<System.Reflection.AssemblyInformationalVersionAttribute>()
                          ?.InformationalVersion?.Split('+')[0]
                      ?? name.Version?.ToString()
                      ?? "unknown";

        return (name.Name ?? "unknown", version);
    }

    private static string? Coalesce(string? configured, string variableName)
        => string.IsNullOrWhiteSpace(configured)
            ? Environment.GetEnvironmentVariable(variableName)
            : configured;

    /// <summary>
    /// Renders a key as a short prefix for logs. Never used on the private key.
    /// </summary>
    private static string Mask(string key)
    {
        var trimmed = key.Trim();
        return trimmed.Length <= 4 ? "****" : string.Concat(trimmed.AsSpan(0, 4), "...");
    }

    /// <summary>
    /// A type from the product engine assembly, used to report the engine name and version in
    /// <c>get_license_status</c>. Returns <c>null</c> by default, in which case the engine is
    /// omitted from the payload rather than guessed — the family runs engine versions from 26.3
    /// to 26.8, so a wrong number is worse than no number.
    /// </summary>
    /// <example><c>protected override Type? EngineMarkerType => typeof(GroupDocs.Comparison.Comparer);</c></example>
    protected virtual Type? EngineMarkerType => null;

    /// <summary>
    /// Applies a classic license file. Each server calls its own product's
    /// <c>License().SetLicense(path)</c>.
    /// </summary>
    protected abstract void SetLicenseFromPath(string licensePath);

    /// <summary>
    /// Applies a metered key pair. Each server calls its own product's
    /// <c>new Metered().SetMeteredKey(publicKey, privateKey)</c>.
    /// </summary>
    /// <remarks>
    /// Abstract rather than virtual on purpose: a no-op default would let a server accept metered
    /// keys, log nothing useful, and silently run in evaluation mode while the customer believes
    /// they are being billed per use.
    /// </remarks>
    protected abstract void SetMeteredKeyCore(string publicKey, string privateKey);

    /// <summary>
    /// Reads metered usage from the product engine — typically
    /// <c>Metered.GetConsumptionQuantity()</c> and <c>Metered.GetConsumptionCredit()</c>.
    /// Called only in <see cref="LicenseMode.Metered"/>; exceptions are caught by the caller.
    /// </summary>
    protected abstract MeteredConsumption ReadConsumptionCore();
}
