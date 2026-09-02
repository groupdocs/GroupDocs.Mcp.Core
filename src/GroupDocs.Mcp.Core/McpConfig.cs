namespace GroupDocs.Mcp.Core;

public class McpConfig
{
    /// <summary>
    /// Path to the GroupDocs license file.
    /// Also checked via GROUPDOCS_LICENSE_PATH environment variable.
    /// </summary>
    public string? LicensePath { get; set; }

    /// <summary>
    /// Metered public key. Also checked via GROUPDOCS_METERED_PUBLIC_KEY.
    /// Both keys must be set for metered licensing to activate.
    /// </summary>
    public string? MeteredPublicKey { get; set; }

    /// <summary>
    /// Metered private key. Also checked via GROUPDOCS_METERED_PRIVATE_KEY.
    /// Treat as a secret: never logged, never echoed in tool output.
    /// </summary>
    public string? MeteredPrivateKey { get; set; }

    /// <summary>
    /// Maximum characters to return in text output before truncation.
    /// Default: 5000.
    /// </summary>
    public int MaxOutputCharacters { get; set; } = 5000;

    /// <summary>
    /// Maximum number of files listed in a "file not found" recovery message.
    /// Anything beyond this is summarised as "…and N more" rather than silently dropped.
    /// Default: 50.
    /// </summary>
    public int MaxListedFiles { get; set; } = 50;

    /// <summary>
    /// Maximum size in bytes for base64-encoded file content.
    /// Default: 10 MB.
    /// </summary>
    public long MaxBase64SizeBytes { get; set; } = 10 * 1024 * 1024;

    /// <summary>
    /// Default expiry for download URLs.
    /// Default: 1 hour.
    /// </summary>
    public TimeSpan DownloadUrlExpiry { get; set; } = TimeSpan.FromHours(1);

    public void SetLicensePath(string path) => LicensePath = path;

    /// <summary>Sets both metered keys. Both are required for metered mode.</summary>
    public void SetMeteredKey(string publicKey, string privateKey)
    {
        MeteredPublicKey = publicKey;
        MeteredPrivateKey = privateKey;
    }
}
