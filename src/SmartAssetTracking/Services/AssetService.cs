using Microsoft.Extensions.Logging;
using SmartAssetTracking.Models;
using SmartAssetTracking.Repositories;

namespace SmartAssetTracking.Services;

/// <summary>Values entered by the user when creating or updating an asset.</summary>
public record AssetInput(
    AssetKind Kind,
    string Brand,
    string ModelName,
    string SerialNumber,
    DateTime PurchaseDate,
    decimal PurchasePriceUsd,
    int OfficeId,
    DateTime? WarrantyExpirationDate = null,
    int? EmployeeId = null);

public interface IAssetService
{
    Task<IReadOnlyList<Asset>> GetAllSortedAsync(CancellationToken ct = default);
    Task<Asset> GetByIdAsync(int id, CancellationToken ct = default);
    Task<IReadOnlyList<Asset>> SearchAsync(AssetQuery query, CancellationToken ct = default);
    Task<IReadOnlyList<Asset>> GetExpiredAsync(CancellationToken ct = default);
    Task<IReadOnlyList<Asset>> GetExpiringSoonAsync(CancellationToken ct = default);
    Task<IReadOnlyList<Asset>> GetMaintenanceScheduleAsync(CancellationToken ct = default);
    Task<Asset> CreateAsync(AssetInput input, CancellationToken ct = default);
    Task<Asset> UpdateAsync(int id, AssetInput input, CancellationToken ct = default);
    Task DeleteAsync(int id, CancellationToken ct = default);
    Task AssignAsync(int assetId, int? employeeId, CancellationToken ct = default);
    Task RecordMaintenanceAsync(int assetId, DateTime performedOn, DateTime? nextMaintenance, string? notes, CancellationToken ct = default);
}

public class AssetService(
    IAssetRepository assets,
    IOfficeRepository offices,
    IEmployeeRepository employees,
    ILifecycleService lifecycle,
    UserSession session,
    ILogger<AssetService> logger) : IAssetService
{
    private static readonly DateTime EarliestPurchaseDate = new(1990, 1, 1);
    private const decimal MaxPriceUsd = 1_000_000m;

    /// <summary>Sorts by category first (computers, mobiles, equipment), then by purchase date.</summary>
    public static IReadOnlyList<Asset> SortByCategoryThenPurchaseDate(IEnumerable<Asset> source) =>
        source.OrderBy(a => a.Category)
              .ThenBy(a => a.PurchaseDate)
              .ThenBy(a => a.Id)
              .ToList();

    public async Task<IReadOnlyList<Asset>> GetAllSortedAsync(CancellationToken ct = default) =>
        SortByCategoryThenPurchaseDate(await assets.GetAllAsync(ct));

    public async Task<Asset> GetByIdAsync(int id, CancellationToken ct = default) =>
        await assets.GetByIdAsync(id, ct) ?? throw new NotFoundException($"No asset with ID {id} exists.");

    public async Task<IReadOnlyList<Asset>> SearchAsync(AssetQuery query, CancellationToken ct = default) =>
        SortByCategoryThenPurchaseDate(await assets.FindAsync(query, ct));

    public Task<IReadOnlyList<Asset>> GetExpiredAsync(CancellationToken ct = default) =>
        SearchAsync(new AssetQuery { PurchasedOnOrBefore = lifecycle.ExpiredPurchaseCutoff }, ct);

    public async Task<IReadOnlyList<Asset>> GetExpiringSoonAsync(CancellationToken ct = default)
    {
        var all = await assets.FindAsync(new AssetQuery { PurchasedAfter = lifecycle.ExpiredPurchaseCutoff }, ct);
        return all.Where(lifecycle.IsExpiringSoon)
                  .OrderBy(lifecycle.GetRemainingDays)
                  .ToList();
    }

    public async Task<IReadOnlyList<Asset>> GetMaintenanceScheduleAsync(CancellationToken ct = default)
    {
        var all = await assets.GetAllAsync(ct);
        return all.Where(a => a.NextMaintenanceDate is not null)
                  .OrderBy(a => a.NextMaintenanceDate)
                  .ToList();
    }

    public async Task<Asset> CreateAsync(AssetInput input, CancellationToken ct = default)
    {
        session.Require(UserRole.Manager);
        await ValidateAsync(input, existingAssetId: null, ct);

        var asset = AssetFactory.Create(input.Kind, input.Brand, input.ModelName, input.SerialNumber,
            input.PurchaseDate, input.PurchasePriceUsd, input.OfficeId, input.WarrantyExpirationDate, input.EmployeeId);

        await assets.AddAsync(asset, ct);
        logger.LogInformation("User {User} created asset {Asset}", session.CurrentUser?.Username, asset);
        return await GetByIdAsync(asset.Id, ct);
    }

    public async Task<Asset> UpdateAsync(int id, AssetInput input, CancellationToken ct = default)
    {
        session.Require(UserRole.Manager);
        var asset = await GetByIdAsync(id, ct);

        if (asset.Kind != input.Kind)
            throw new ValidationException("The asset type cannot be changed. Delete the asset and create a new one instead.");

        await ValidateAsync(input, id, ct);

        asset.Brand = input.Brand.Trim();
        asset.ModelName = input.ModelName.Trim();
        asset.SerialNumber = input.SerialNumber.Trim();
        asset.PurchaseDate = input.PurchaseDate.Date;
        asset.PurchasePriceUsd = input.PurchasePriceUsd;
        asset.OfficeId = input.OfficeId;
        asset.WarrantyExpirationDate = (input.WarrantyExpirationDate ?? input.PurchaseDate.AddYears(Asset.DefaultWarrantyYears)).Date;
        asset.EmployeeId = input.EmployeeId;

        await assets.UpdateAsync(asset, ct);
        logger.LogInformation("User {User} updated asset {Asset}", session.CurrentUser?.Username, asset);
        return await GetByIdAsync(id, ct);
    }

    public async Task DeleteAsync(int id, CancellationToken ct = default)
    {
        session.Require(UserRole.Manager);
        if (!await assets.DeleteAsync(id, ct))
            throw new NotFoundException($"No asset with ID {id} exists.");
        logger.LogInformation("User {User} deleted asset {AssetId}", session.CurrentUser?.Username, id);
    }

    public async Task AssignAsync(int assetId, int? employeeId, CancellationToken ct = default)
    {
        session.Require(UserRole.Manager);
        var asset = await GetByIdAsync(assetId, ct);

        if (employeeId is not null && await employees.GetByIdAsync(employeeId.Value, ct) is null)
            throw new NotFoundException($"No employee with ID {employeeId} exists.");

        asset.EmployeeId = employeeId;
        await assets.UpdateAsync(asset, ct);
        logger.LogInformation("User {User} assigned asset {AssetId} to employee {EmployeeId}",
            session.CurrentUser?.Username, assetId, employeeId?.ToString() ?? "(none)");
    }

    public async Task RecordMaintenanceAsync(int assetId, DateTime performedOn, DateTime? nextMaintenance, string? notes,
        CancellationToken ct = default)
    {
        session.Require(UserRole.Manager);
        var asset = await GetByIdAsync(assetId, ct);

        var errors = new ValidationErrors()
            .Require(performedOn.Date <= lifecycle.Today, "Maintenance date cannot be in the future.")
            .Require(performedOn.Date >= asset.PurchaseDate, "Maintenance date cannot be before the purchase date.")
            .Require(nextMaintenance is null || nextMaintenance.Value.Date > performedOn.Date,
                "Next maintenance date must be after the maintenance date.")
            .Require(notes is null || notes.Length <= 500, "Maintenance notes cannot be longer than 500 characters.");
        errors.ThrowIfAny();

        asset.RecordMaintenance(performedOn, nextMaintenance, notes);
        await assets.UpdateAsync(asset, ct);
        logger.LogInformation("User {User} recorded maintenance on asset {AssetId}", session.CurrentUser?.Username, assetId);
    }

    private async Task ValidateAsync(AssetInput input, int? existingAssetId, CancellationToken ct)
    {
        var errors = new ValidationErrors()
            .RequireText(input.Brand, "Brand", 50)
            .RequireText(input.ModelName, "Model name", 100)
            .RequireText(input.SerialNumber, "Serial number", 50)
            .Require(Enum.IsDefined(input.Kind), "Unknown asset type.")
            .Require(input.PurchasePriceUsd > 0, "Purchase price must be greater than zero.")
            .Require(input.PurchasePriceUsd <= MaxPriceUsd, $"Purchase price cannot exceed {MaxPriceUsd:N0} USD.")
            .Require(input.PurchaseDate.Date <= lifecycle.Today, "Purchase date cannot be in the future.")
            .Require(input.PurchaseDate.Date >= EarliestPurchaseDate, $"Purchase date cannot be before {EarliestPurchaseDate:yyyy-MM-dd}.")
            .Require(input.WarrantyExpirationDate is null || input.WarrantyExpirationDate.Value.Date >= input.PurchaseDate.Date,
                "Warranty expiration cannot be before the purchase date.");

        if (await offices.GetByIdAsync(input.OfficeId, ct) is null)
            errors.Add($"Office {input.OfficeId} does not exist.");

        if (input.EmployeeId is not null && await employees.GetByIdAsync(input.EmployeeId.Value, ct) is null)
            errors.Add($"Employee {input.EmployeeId} does not exist.");

        if (!string.IsNullOrWhiteSpace(input.SerialNumber) &&
            await assets.SerialNumberExistsAsync(input.SerialNumber, existingAssetId, ct))
            errors.Add($"Serial number '{input.SerialNumber.Trim()}' is already registered.");

        errors.ThrowIfAny();
    }
}
