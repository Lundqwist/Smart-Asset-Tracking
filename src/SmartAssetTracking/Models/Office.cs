// ============================================================================
// Office.cs
// ----------------------------------------------------------------------------
// A company office (Level 3). EF Core turns this class into the "Offices"
// table. Each asset belongs to exactly one office, and one office can have
// many assets. This is called a "one-to-many" relationship.
// ============================================================================

namespace SmartAssetTracking.Models;

/// <summary>A company office. Each office uses one local currency for price display.</summary>
public class Office
{
    // Primary key: the unique number of the office.
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Country { get; set; } = string.Empty;

    /// <summary>ISO 4217 currency code, e.g. SEK, USD, EUR, TRY.</summary>
    // The currency decides how prices are shown for assets in this office.
    // The exchange rates are in appsettings.json (see CurrencyService).
    public string CurrencyCode { get; set; } = "USD";

    // The "many" side of the relationship: all assets in this office.
    // "List<Asset>" is a resizable list that can only hold Asset objects.
    // "= []" starts it as an empty list so it is never null.
    public List<Asset> Assets { get; set; } = [];

    // Used when we print an office, e.g. "Sweden Office (Sweden, SEK)".
    public override string ToString() => $"{Name} ({Country}, {CurrencyCode})";
}
