namespace SmartAssetTracking.Models;

/// <summary>A company office. Each office uses one local currency for price display.</summary>
public class Office
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Country { get; set; } = string.Empty;

    /// <summary>ISO 4217 currency code, e.g. SEK, USD, EUR, TRY.</summary>
    public string CurrencyCode { get; set; } = "USD";

    public List<Asset> Assets { get; set; } = [];

    public override string ToString() => $"{Name} ({Country}, {CurrencyCode})";
}
