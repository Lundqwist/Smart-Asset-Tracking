// ============================================================================
// AssetQuery.cs
// ----------------------------------------------------------------------------
// A small object that describes WHAT to search for (Level 4: search and
// filtering). Instead of writing a separate method for every possible search
// ("by brand", "by brand and office", ...), we fill in only the fields we
// care about, and AssetRepository builds the matching database query.
// ============================================================================

using SmartAssetTracking.Models;

namespace SmartAssetTracking.Repositories;

/// <summary>
/// Search / filter criteria. Every property is optional; only the ones set are applied.
/// Text criteria are case-insensitive "contains" matches.
/// </summary>
// A "record" is a special kind of class made for holding data. Two records with
// the same values count as equal, and records support the "with" keyword to
// copy one and change a few values.
// "{ get; init; }" means the value can only be set when the object is created:
//     new AssetQuery { Brand = "Lenovo" }
// After that it can't change. This makes the object safe to pass around.
public record AssetQuery
{
    public string? Brand { get; init; }
    public string? Model { get; init; }
    public int? OfficeId { get; init; }
    public string? OfficeName { get; init; }
    public int? PurchaseYear { get; init; }
    public AssetCategory? Category { get; init; }
    public int? EmployeeId { get; init; }

    // true = only assigned assets, false = only unassigned, null = don't care
    public bool? Assigned { get; init; }

    /// <summary>Only assets purchased on or before this date (used for "expired" filtering).</summary>
    public DateTime? PurchasedOnOrBefore { get; init; }

    /// <summary>Only assets purchased after this date.</summary>
    public DateTime? PurchasedAfter { get; init; }

    /// <summary>Human-readable description of the active criteria, e.g. "Brand = Lenovo".</summary>
    // Used for the "Search: Brand = Lenovo" line above the search results.
    public string Describe()
    {
        var parts = new List<string>();

        // "is not null" checks that a value was actually given.
        if (!string.IsNullOrWhiteSpace(Brand)) parts.Add($"Brand = {Brand}");
        if (!string.IsNullOrWhiteSpace(Model)) parts.Add($"Model = {Model}");
        if (!string.IsNullOrWhiteSpace(OfficeName)) parts.Add($"Office = {OfficeName}");
        else if (OfficeId is not null) parts.Add($"Office Id = {OfficeId}");
        if (PurchaseYear is not null) parts.Add($"Purchase Year = {PurchaseYear}");

        // ".Value" reads the real value out of a nullable type (AssetCategory? -> AssetCategory).
        if (Category is not null) parts.Add($"Category = {Category.Value.ToDisplayName()}");
        if (EmployeeId is not null) parts.Add($"Employee Id = {EmployeeId}");
        if (Assigned is not null) parts.Add(Assigned.Value ? "Assigned" : "Unassigned");

        // ":yyyy-MM-dd" inside {} formats the date, e.g. 2024-02-11.
        if (PurchasedOnOrBefore is not null) parts.Add($"Purchased <= {PurchasedOnOrBefore:yyyy-MM-dd}");
        if (PurchasedAfter is not null) parts.Add($"Purchased > {PurchasedAfter:yyyy-MM-dd}");

        // string.Join glues the parts together with ", " between them.
        return parts.Count == 0 ? "All assets" : string.Join(", ", parts);
    }
}
