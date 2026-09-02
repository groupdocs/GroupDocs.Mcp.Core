using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace GroupDocs.Mcp.Core.Licensing;

/// <summary>
/// Builds the payload for the Core-shipped <c>get_license_status</c> tool.
/// </summary>
/// <remarks>
/// Ships in Core rather than in each product so it is written once, not twelve times, and so the
/// wire shape cannot drift between servers. It also closes a gap the external audit found: before
/// this, a client had no programmatic way to discover that a server was running unlicensed.
/// </remarks>
public static class LicenseStatusTool
{
    public const string ToolName = "get_license_status";

    public const string ToolDescription =
        "Returns how this MCP server is licensed, and — under metered licensing — how much has " +
        "been consumed. Call this when the user asks about licensing, evaluation limitations, " +
        "metered usage, remaining credit, or which product engine version is running. " +
        "Returns a JSON object with `mode` (\"evaluation\", \"licensed\" or \"metered\"), " +
        "`licensed` (true for both licensed and metered), `consumption` (null unless metered; " +
        "otherwise `quantity` and `credit`, or `error` if the reading failed), and `server` / " +
        "`engine` name and version. Takes no arguments and never modifies anything.";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public static string Build(ILicenseManager licenseManager)
    {
        // Licensing is applied lazily on first use; make sure the reported mode is the real one
        // rather than the pre-initialisation default.
        licenseManager.SetLicense();

        var consumption = licenseManager.GetConsumption();

        var payload = new
        {
            mode = licenseManager.Mode switch
            {
                LicenseMode.Metered => "metered",
                LicenseMode.Licensed => "licensed",
                _ => "evaluation"
            },
            licensed = licenseManager.IsLicensed,
            source = licenseManager.Mode switch
            {
                LicenseMode.Metered => "metered-keys",
                LicenseMode.Licensed => "license-file",
                _ => (string?)null
            },
            // null outside metered mode — a zero would read as "nothing consumed", which is a
            // plausible-looking lie when the concept does not apply.
            consumption = consumption is null
                ? null
                : new
                {
                    quantity = consumption.Quantity,
                    credit = consumption.Credit,
                    error = consumption.Error
                },
            server = DescribeServer(),
            engine = DescribeEngine(licenseManager),
            // A configuration that was supplied and rejected must not be reported as "nothing
            // configured" - that is the misleading-status class this release exists to remove.
            note = licenseManager.IsLicensed
                ? licenseManager.Diagnostic
                : licenseManager.Diagnostic is { } diagnostic
                    ? diagnostic + " Output may carry evaluation limitations."
                    : "No license configured — output may carry evaluation limitations. Set " +
                      "GROUPDOCS_METERED_PUBLIC_KEY and GROUPDOCS_METERED_PRIVATE_KEY for metered " +
                      "licensing, or GROUPDOCS_LICENSE_PATH for a license file."
        };

        return JsonSerializer.Serialize(payload, JsonOptions);
    }

    private static object DescribeServer()
    {
        var assembly = Assembly.GetEntryAssembly();
        return new
        {
            name = assembly?.GetName().Name ?? "unknown",
            version = InformationalVersion(assembly)
        };
    }

    /// <summary>
    /// Reports which product engine answered. The family runs a wide spread of engine versions
    /// (26.3 to 26.8) and that was previously invisible to callers. Returns null when the server
    /// does not declare an engine — omitting it beats reporting the server's own version, which
    /// is what a naive lookup returns since the license manager lives in the server assembly.
    /// </summary>
    private static object? DescribeEngine(ILicenseManager licenseManager)
    {
        if (licenseManager.DescribeEngine() is not { } engine) return null;
        return new { name = engine.Name, version = engine.Version };
    }

    private static string InformationalVersion(Assembly? assembly)
        => assembly?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()
               ?.InformationalVersion?.Split('+')[0]
           ?? assembly?.GetName().Version?.ToString()
           ?? "unknown";
}
