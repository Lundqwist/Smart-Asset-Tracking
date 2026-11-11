using SmartAssetTracking.Models;
using SmartAssetTracking.Repositories;

namespace SmartAssetTracking.Services;

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

    public async Task<IReadOnlyList<Asset>> GetMostExpensiveAsync(int top = 10, CancellationToken ct = default)
    {
        var allAssets = await assets.GetAllAsync(ct);
        return allAssets.OrderByDescending(a => a.PurchasePriceUsd)
                        .ThenBy(a => a.Id)
                        .Take(Math.Max(1, top))
                        .ToList();
    }

    public async Task<IReadOnlyList<EmployeeAssetCount>> GetAssetsPerEmployeeAsync(CancellationToken ct = default)
    {
        var allEmployees = await employees.GetAllAsync(ct);
        return BuildEmployeeCounts(allEmployees);
    }

    public async Task<DashboardStats> GetDashboardAsync(CancellationToken ct = default)
    {
        var allAssets = await assets.GetAllAsync(ct);
        var allOffices = await offices.GetAllAsync(ct);
        var allEmployees = await employees.GetAllAsync(ct);
        var officeSummaries = BuildOfficeSummaries(allOffices, allAssets);

        var perType = allAssets
            .GroupBy(a => a.TypeName)
            .Select(g => (Type: g.Key, Count: g.Count()))
            .OrderByDescending(t => t.Count)
            .ThenBy(t => t.Type)
            .ToList();

        var maintenanceDueBy = lifecycle.Today.AddDays(lifecycleOptions.MaintenanceDueSoonDays);
        var reporting = currency.ReportingCurrency;

        return new DashboardStats(
            TotalAssets: allAssets.Count,
            TotalEmployees: allEmployees.Count,
            ExpiringSoon: allAssets.Count(lifecycle.IsExpiringSoon),
            Expired: allAssets.Count(lifecycle.IsExpired),
            UnassignedAssets: allAssets.Count(a => a.EmployeeId is null),
            MaintenanceDue: allAssets.Count(a => a.NextMaintenanceDate is not null && a.NextMaintenanceDate <= maintenanceDueBy),
            TotalCompanyValue: currency.ConvertFromUsd(allAssets.Sum(a => a.PurchasePriceUsd), reporting),
            ReportingCurrency: reporting,
            TopAssetType: perType.FirstOrDefault().Type,
            TopAssetTypeCount: perType.FirstOrDefault().Count,
            MostValuableOffice: officeSummaries.Where(o => o.AssetCount > 0).MaxBy(o => o.TotalValueUsd)?.Office,
            AssetsPerType: perType,
            AssetsPerEmployee: BuildEmployeeCounts(allEmployees),
            Offices: officeSummaries);
    }

    private List<OfficeSummary> BuildOfficeSummaries(IEnumerable<Office> allOffices, IReadOnlyCollection<Asset> allAssets)
    {
        var byOffice = allAssets.ToLookup(a => a.OfficeId);
        return allOffices
            .Select(o =>
            {
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

    private static List<EmployeeAssetCount> BuildEmployeeCounts(IEnumerable<Employee> allEmployees) =>
        allEmployees
            .Select(e => new EmployeeAssetCount(e.Id, e.FullName, e.Department, e.AssignedAssets.Count,
                e.AssignedAssets.Sum(a => a.PurchasePriceUsd)))
            .OrderByDescending(e => e.AssetCount)
            .ThenBy(e => e.Employee)
            .ToList();
}
