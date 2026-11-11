// ============================================================================
// ReportService.cs
// ----------------------------------------------------------------------------
// Calculates statistics for the reports (Level 3) and the dashboard (Level 5):
//   * asset count and total value per office
//   * the most expensive assets
//   * assets per employee
//   * dashboard key figures: totals, expiring assets, top asset type...
//
// It loads the data through the repositories and uses LINQ (GroupBy, Sum,
// Count, OrderBy, ...) to summarise it. The results are returned as small
// "record" types that only hold data, which makes them easy to display or
// export to a file.
// ============================================================================

using SmartAssetTracking.Models;
using SmartAssetTracking.Repositories;

namespace SmartAssetTracking.Services;

// One row of the office report. A positional record is a compact way to declare
// a data-only type with these properties.
public record OfficeSummary(
    int OfficeId,
    string Office,
    string Country,
    string Currency,
    int AssetCount,
    int ExpiringSoon,
    int Expired,
    decimal TotalValueUsd,
    decimal TotalValueLocal);

public record EmployeeAssetCount(int EmployeeId, string Employee, string Department, int AssetCount, decimal TotalValueUsd);

// Everything the dashboard screen shows, bundled in one object.
// "(string Type, int Count)" is a "named tuple": a pair of values with names.
public record DashboardStats(
    int TotalAssets,
    int TotalEmployees,
    int ExpiringSoon,
    int Expired,
    int UnassignedAssets,
    int MaintenanceDue,
    decimal TotalCompanyValue,
    string ReportingCurrency,
    string? TopAssetType,
    int TopAssetTypeCount,
    string? MostValuableOffice,
    IReadOnlyList<(string Type, int Count)> AssetsPerType,
    IReadOnlyList<EmployeeAssetCount> AssetsPerEmployee,
    IReadOnlyList<OfficeSummary> Offices);

public interface IReportService
{
    Task<IReadOnlyList<OfficeSummary>> GetOfficeSummariesAsync(CancellationToken ct = default);
    Task<IReadOnlyList<Asset>> GetMostExpensiveAsync(int top = 10, CancellationToken ct = default);
    Task<IReadOnlyList<EmployeeAssetCount>> GetAssetsPerEmployeeAsync(CancellationToken ct = default);
    Task<DashboardStats> GetDashboardAsync(CancellationToken ct = default);
}

/// <summary>Aggregated statistics built with LINQ on top of the repositories.</summary>
public class ReportService(
    IAssetRepository assets,
    IOfficeRepository offices,
    IEmployeeRepository employees,
    ILifecycleService lifecycle,
    ICurrencyService currency,
    Helpers.LifecycleOptions lifecycleOptions) : IReportService
{
    public async Task<IReadOnlyList<OfficeSummary>> GetOfficeSummariesAsync(CancellationToken ct = default)
    {
        var allOffices = await offices.GetAllAsync(ct);
        var allAssets = await assets.GetAllAsync(ct);
        return BuildOfficeSummaries(allOffices, allAssets);
    }

    // The N most expensive assets. Prices are compared in USD so different currencies don't matter.
    public async Task<IReadOnlyList<Asset>> GetMostExpensiveAsync(int top = 10, CancellationToken ct = default)
    {
        var allAssets = await assets.GetAllAsync(ct);
        return allAssets.OrderByDescending(a => a.PurchasePriceUsd) // highest price first
                        .ThenBy(a => a.Id)
                        .Take(Math.Max(1, top))                    // keep only the first N
                        .ToList();
    }

    public async Task<IReadOnlyList<EmployeeAssetCount>> GetAssetsPerEmployeeAsync(CancellationToken ct = default)
    {
        var allEmployees = await employees.GetAllAsync(ct);
        return BuildEmployeeCounts(allEmployees);
    }

    public async Task<DashboardStats> GetDashboardAsync(CancellationToken ct = default)
    {
        // Load everything once, then calculate all figures in memory.
        var allAssets = await assets.GetAllAsync(ct);
        var allOffices = await offices.GetAllAsync(ct);
        var allEmployees = await employees.GetAllAsync(ct);
        var officeSummaries = BuildOfficeSummaries(allOffices, allAssets);

        // GroupBy puts assets with the same TypeName into groups, e.g. all "Laptop"s together.
        // Select turns each group into a (Type, Count) pair. Then we sort with the biggest group first.
        var perType = allAssets
            .GroupBy(a => a.TypeName)
            .Select(g => (Type: g.Key, Count: g.Count()))
            .OrderByDescending(t => t.Count)
            .ThenBy(t => t.Type)
            .ToList();

        var maintenanceDueBy = lifecycle.Today.AddDays(lifecycleOptions.MaintenanceDueSoonDays);
        var reporting = currency.ReportingCurrency;

        // "TotalAssets: allAssets.Count" uses named arguments, which make a long
        // constructor call much easier to read.
        return new DashboardStats(
            TotalAssets: allAssets.Count,
            TotalEmployees: allEmployees.Count,
            ExpiringSoon: allAssets.Count(lifecycle.IsExpiringSoon),   // Count(condition) counts the matches
            Expired: allAssets.Count(lifecycle.IsExpired),
            UnassignedAssets: allAssets.Count(a => a.EmployeeId is null),
            MaintenanceDue: allAssets.Count(a => a.NextMaintenanceDate is not null && a.NextMaintenanceDate <= maintenanceDueBy),
            TotalCompanyValue: currency.ConvertFromUsd(allAssets.Sum(a => a.PurchasePriceUsd), reporting),
            ReportingCurrency: reporting,
            TopAssetType: perType.FirstOrDefault().Type,        // the biggest group (or null if there are no assets)
            TopAssetTypeCount: perType.FirstOrDefault().Count,
            // MaxBy returns the item with the largest value. "?." handles the case of no offices.
            MostValuableOffice: officeSummaries.Where(o => o.AssetCount > 0).MaxBy(o => o.TotalValueUsd)?.Office,
            AssetsPerType: perType,
            AssetsPerEmployee: BuildEmployeeCounts(allEmployees),
            Offices: officeSummaries);
    }

    // Builds one OfficeSummary per office.
    private List<OfficeSummary> BuildOfficeSummaries(IEnumerable<Office> allOffices, IReadOnlyCollection<Asset> allAssets)
    {
        // ToLookup groups assets by OfficeId, so byOffice[3] quickly gives all assets in office 3.
        var byOffice = allAssets.ToLookup(a => a.OfficeId);

        return allOffices
            .Select(o =>
            {
                // A lambda with { } can have several statements, and it must "return" a value.
                var officeAssets = byOffice[o.Id].ToList();
                var totalUsd = officeAssets.Sum(a => a.PurchasePriceUsd);
                return new OfficeSummary(
                    o.Id, o.Name, o.Country, o.CurrencyCode,
                    AssetCount: officeAssets.Count,
                    ExpiringSoon: officeAssets.Count(lifecycle.IsExpiringSoon),
                    Expired: officeAssets.Count(lifecycle.IsExpired),
                    TotalValueUsd: totalUsd,
                    TotalValueLocal: currency.ConvertFromUsd(totalUsd, o.CurrencyCode));
            })
            .OrderBy(s => s.OfficeId)
            .ToList();
    }

    // Employee name, department and how many assets they have (most assets first).
    private static List<EmployeeAssetCount> BuildEmployeeCounts(IEnumerable<Employee> allEmployees) =>
        allEmployees
            .Select(e => new EmployeeAssetCount(e.Id, e.FullName, e.Department, e.AssignedAssets.Count,
                e.AssignedAssets.Sum(a => a.PurchasePriceUsd)))
            .OrderByDescending(e => e.AssetCount)
            .ThenBy(e => e.Employee)
            .ToList();
}
