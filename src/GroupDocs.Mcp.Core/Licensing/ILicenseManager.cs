namespace GroupDocs.Mcp.Core.Licensing;

public interface ILicenseManager
{
    /// <summary>
    /// True in both <see cref="LicenseMode.Licensed"/> and <see cref="LicenseMode.Metered"/> —
    /// metered is a licensed state. Kept separate from <see cref="Mode"/> on purpose: a caller
    /// that only needs "will output carry evaluation limitations?" checks this one flag and does
    /// not have to know which mode strings count as licensed, so adding a future mode cannot
    /// silently break it.
    /// </summary>
    bool IsLicensed { get; }

    /// <summary>How this process is licensed. Valid only after <see cref="SetLicense"/>.</summary>
    LicenseMode Mode { get; }

    /// <summary>
    /// Applies licensing once per process. Safe to call repeatedly — subsequent calls are no-ops.
    /// </summary>
    void SetLicense();

    /// <summary>
    /// Current metered usage, or <c>null</c> when <see cref="Mode"/> is not
    /// <see cref="LicenseMode.Metered"/>. A failed reading returns a
    /// <see cref="MeteredConsumption"/> carrying <see cref="MeteredConsumption.Error"/>,
    /// never fabricated zeros.
    /// </summary>
    MeteredConsumption? GetConsumption();

    /// <summary>
    /// Name and version of the product engine, or <c>null</c> if the server does not declare one.
    /// Reported by <c>get_license_status</c>; the audit found engine versions were otherwise
    /// invisible to callers.
    /// </summary>
    (string Name, string Version)? DescribeEngine();

    /// <summary>
    /// Why licensing did not end up where the configuration asked, or <c>null</c> if nothing went
    /// wrong. Set when keys or a license file were supplied but could not be applied — reporting
    /// a bare "no license configured" in that case would be actively misleading.
    /// </summary>
    string? Diagnostic { get; }
}
