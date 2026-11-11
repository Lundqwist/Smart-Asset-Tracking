// ============================================================================
// AssetService.cs
// ----------------------------------------------------------------------------
// The "business logic" for assets: the rules that must hold no matter where
// a request comes from. The service sits between the menus (UI) and the
// database (repositories):
//
//     Menu (UI)  ->  AssetService (rules)  ->  AssetRepository (database)
//
// The service checks permissions (only Managers and Admins may change
// assets), validates the input (no empty brand, no future dates, no
// duplicate serial numbers...), sorts results and writes to the log.
// Keeping the rules here, not in the menus, means they can't be skipped and
// can be unit tested.
// ============================================================================

using Microsoft.Extensions.Logging;
using SmartAssetTracking.Models;
using SmartAssetTracking.Repositories;

namespace SmartAssetTracking.Services;

/// <summary>Values entered by the user when creating or updating an asset.</summary>
// A "positional record": one line declares a type with these properties and a
// constructor that takes them in this order. It's a neat way to bundle the form
// data the user typed in, before it becomes a real Asset.
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

// IReadOnlyList<Asset> = a list the caller may read but not change.
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

// All the parameters below are "injected": the dependency injection container
// creates them and hands them to this class (see ServiceRegistration.cs).
public class AssetService(
    IAssetRepository assets,
    IOfficeRepository offices,
    IEmployeeRepository employees,
    ILifecycleService lifecycle,
    UserSession session,
    ILogger<AssetService> logger) : IAssetService
{
    // "static readonly" works like a constant that is calculated when the program starts.
    private static readonly DateTime EarliestPurchaseDate = new(1990, 1, 1);
    private const decimal MaxPriceUsd = 1_000_000m; // "_" is only a digit separator for readability

    /// <summary>Sorts by category first (computers, mobiles, equipment), then by purchase date.</summary>
    // Level 2: "Sort assets by category first, then sort by purchase date".
    // LINQ: OrderBy sorts by the first key, ThenBy breaks ties with the next key.
    // Category is an enum (Computer = 1, Mobile = 2, OfficeEquipment = 3), so computers come first.
    // We sort in C# (not SQL) because Category is calculated in C# and isn't a database column.
    public static IReadOnlyList<Asset> SortByCategoryThenPurchaseDate(IEnumerable<Asset> source) =>
        source.OrderBy(a => a.Category)
              .ThenBy(a => a.PurchaseDate)
              .ThenBy(a => a.Id)
              .ToList();

    public async Task<IReadOnlyList<Asset>> GetAllSortedAsync(CancellationToken ct = default) =>
        SortByCategoryThenPurchaseDate(await assets.GetAllAsync(ct));

    // Unlike the repository (which returns null), the service throws a clear error if the Id doesn't exist.
    public async Task<Asset> GetByIdAsync(int id, CancellationToken ct = default) =>
        await assets.GetByIdAsync(id, ct) ?? throw new NotFoundException($"No asset with ID {id} exists.");

    public async Task<IReadOnlyList<Asset>> SearchAsync(AssetQuery query, CancellationToken ct = default) =>
        SortByCategoryThenPurchaseDate(await assets.FindAsync(query, ct));

    // Expired = bought 3 or more years ago. This filter runs in the database (fast).
    public Task<IReadOnlyList<Asset>> GetExpiredAsync(CancellationToken ct = default) =>
        SearchAsync(new AssetQuery { PurchasedOnOrBefore = lifecycle.ExpiredPurchaseCutoff }, ct);

    // Assets that are YELLOW or RED, with the most urgent first.
    public async Task<IReadOnlyList<Asset>> GetExpiringSoonAsync(CancellationToken ct = default)
    {
        // First let the database skip the already-expired ones...
        var all = await assets.FindAsync(new AssetQuery { PurchasedAfter = lifecycle.ExpiredPurchaseCutoff }, ct);

        // ...then check the status in C#. "lifecycle.IsExpiringSoon" is a "method group":
        // passing the method itself, which is the same as writing a => lifecycle.IsExpiringSoon(a).
        return all.Where(lifecycle.IsExpiringSoon)
                  .OrderBy(lifecycle.GetRemainingDays)
                  .ToList();
    }

    // Assets that have a next maintenance date, ordered by that date (soonest first).
    public async Task<IReadOnlyList<Asset>> GetMaintenanceScheduleAsync(CancellationToken ct = default)
    {
        var all = await assets.GetAllAsync(ct);
        return all.Where(a => a.NextMaintenanceDate is not null)
                  .OrderBy(a => a.NextMaintenanceDate)
                  .ToList();
    }

    // ---- CREATE ----
    public async Task<Asset> CreateAsync(AssetInput input, CancellationToken ct = default)
    {
        session.Require(UserRole.Manager);                     // 1. permission check
        await ValidateAsync(input, existingAssetId: null, ct); // 2. input check (throws if invalid)

        // 3. Create the right class (Laptop, Tablet, ...) with the factory.
        var asset = AssetFactory.Create(input.Kind, input.Brand, input.ModelName, input.SerialNumber,
            input.PurchaseDate, input.PurchasePriceUsd, input.OfficeId, input.WarrantyExpirationDate, input.EmployeeId);

        await assets.AddAsync(asset, ct);                      // 4. save to the database

        // 5. Write to the log file. {User} and {Asset} are placeholders filled by the values after the text.
        logger.LogInformation("User {User} created asset {Asset}", session.CurrentUser?.Username, asset);

        // 6. Read it back, so Office and Employee are filled in for display.
        return await GetByIdAsync(asset.Id, ct);
    }

    // ---- UPDATE ----
    public async Task<Asset> UpdateAsync(int id, AssetInput input, CancellationToken ct = default)
    {
        session.Require(UserRole.Manager);
        var asset = await GetByIdAsync(id, ct);

        // A Laptop object can't turn into a Tablet object, because they are different classes.
        if (asset.Kind != input.Kind)
            throw new ValidationException("The asset type cannot be changed. Delete the asset and create a new one instead.");

        await ValidateAsync(input, id, ct);

        // Copy the new values onto the asset.
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

    // ---- DELETE ----
    public async Task DeleteAsync(int id, CancellationToken ct = default)
    {
        session.Require(UserRole.Manager);
        if (!await assets.DeleteAsync(id, ct))
            throw new NotFoundException($"No asset with ID {id} exists.");
        logger.LogInformation("User {User} deleted asset {AssetId}", session.CurrentUser?.Username, id);
    }

    // Level 5: assign an asset to an employee. Pass null as employeeId to un-assign.
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

    // Level 5: maintenance tracking.
    public async Task RecordMaintenanceAsync(int assetId, DateTime performedOn, DateTime? nextMaintenance, string? notes,
        CancellationToken ct = default)
    {
        session.Require(UserRole.Manager);
        var asset = await GetByIdAsync(assetId, ct);

        // Check every rule, then throw all the problems together.
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

    // All the input rules for an asset, in one place, used by both Create and Update.
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

        // These checks need the database, so they are done with "await".
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
