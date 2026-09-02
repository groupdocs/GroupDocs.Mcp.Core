namespace GroupDocs.Mcp.Core.Licensing;

/// <summary>
/// A reading of metered usage from the product engine.
/// </summary>
/// <remarks>
/// Only meaningful in <see cref="LicenseMode.Metered"/>. Outside metered mode
/// <see cref="ILicenseManager.GetConsumption"/> returns <c>null</c> — deliberately not a
/// zero, because "0 credits consumed" is a plausible-looking lie when the concept does not
/// apply, and that class of silent-wrong-value is exactly what this release is fixing.
///
/// If the engine call itself fails (it may contact GroupDocs servers), <see cref="Error"/>
/// is set and the numeric values stay <c>null</c> — again, never a fabricated zero.
/// </remarks>
public sealed record MeteredConsumption
{
    /// <summary>Consumed quantity, or <c>null</c> if the reading failed.</summary>
    public decimal? Quantity { get; init; }

    /// <summary>Remaining credit, or <c>null</c> if the reading failed.</summary>
    public decimal? Credit { get; init; }

    /// <summary>Set when the reading failed; <c>null</c> on success.</summary>
    public string? Error { get; init; }

    /// <summary>A failed reading, carrying the reason instead of invented numbers.</summary>
    public static MeteredConsumption Failed(string reason) => new() { Error = reason };
}
