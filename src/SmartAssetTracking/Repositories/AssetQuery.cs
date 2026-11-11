using SmartAssetTracking.Models;

namespace SmartAssetTracking.Repositories;

/// <summary>
/// Search / filter criteria. Every property is optional; only the ones set are applied.
/// Text criteria are case-insensitive "contains" matches.
/// </summary>
public record AssetQuery
{
    public string? Brand { get; init; }
    public string? Model { get; init; }
    public int? OfficeId { get; init; }
    public string? OfficeName { get; init; }
    public int? PurchaseYear { get; init; }
    public AssetCategory? Category { get; init; }
    public int? EmployeeId { get; init; }
    public bool? Assigned { get; init; }

    /// <summary>Only assets purchased on or before this date (used for "expired" filtering).</summary>
    public DateTime? PurchasedOnOrBefore { get; init; }

    /// <summary>Only assets purchased after this date.</summary>
    public DateTime? PurchasedAfter { get; init; }

    /// <summary>Human-readable description of the active criteria, e.g. "Brand = Lenovo".</summary>
    public string Describe()
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(Brand)) parts.Add($"Brand = {Brand}");
        if (!string.IsNullOrWhiteSpace(Model)) parts.Add($"Model = {Model}");
        if (!string.IsNullOrWhiteSpace(OfficeName)) parts.Add($"Office = {OfficeName}");
        else if (OfficeId is not null) parts.Add($"Office Id = {OfficeId}");
        if (PurchaseYear is not null) parts.Add($"Purchase Year = {PurchaseYear}");
        if (Category is not null) parts.Add($"Category = {Category.Value.ToDisplayName()}");
        if (EmployeeId is not null) parts.Add($"Employee Id = {EmployeeId}");
        if (Assigned is not null) parts.Add(Assigned.Value ? "Assigned" : "Unassigned");
        if (PurchasedOnOrBefore is not null) parts.Add($"Purchased <= {PurchasedOnOrBefore:yyyy-MM-dd}");
        if (PurchasedAfter is not null) parts.Add($"Purchased > {PurchasedAfter:yyyy-MM-dd}");
        return parts.Count == 0 ? "All assets" : string.Join(", ", parts);
    }
}
