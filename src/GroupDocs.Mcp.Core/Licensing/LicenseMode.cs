namespace GroupDocs.Mcp.Core.Licensing;

/// <summary>
/// How the server is licensed for this process.
/// </summary>
/// <remarks>
/// The wire values emitted by <c>get_license_status</c> are the lower-cased names:
/// <c>"evaluation"</c>, <c>"licensed"</c>, <c>"metered"</c>. "Evaluation" is used
/// rather than "trial" to match GroupDocs' public documentation and the engine's own
/// watermark text — a user searching for what the tool told them should find the docs.
/// </remarks>
public enum LicenseMode
{
    /// <summary>No license configured. Output may carry evaluation limitations.</summary>
    Evaluation = 0,

    /// <summary>A classic license file was applied.</summary>
    Licensed = 1,

    /// <summary>A metered key pair was applied — usage is billed per operation.</summary>
    Metered = 2
}
