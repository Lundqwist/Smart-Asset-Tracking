using Microsoft.EntityFrameworkCore;
using SmartAssetTracking.Data;
using SmartAssetTracking.Models;

namespace SmartAssetTracking.Repositories;

public interface IAssetRepository
{
    Task<List<Asset>> GetAllAsync(CancellationToken ct = default);
    Task<Asset?> GetByIdAsync(int id, CancellationToken ct = default);
    Task<List<Asset>> FindAsync(AssetQuery query, CancellationToken ct = default);
    Task<bool> SerialNumberExistsAsync(string serialNumber, int? excludeAssetId = null, CancellationToken ct = default);
    Task<Asset> AddAsync(Asset asset, CancellationToken ct = default);
    Task UpdateAsync(Asset asset, CancellationToken ct = default);
    Task<bool> DeleteAsync(int id, CancellationToken ct = default);
}

public class AssetRepository(IDbContextFactory<AppDbContext> contextFactory) : IAssetRepository
{
    public Task<List<Asset>> GetAllAsync(CancellationToken ct = default) => FindAsync(new AssetQuery(), ct);

    public async Task<Asset?> GetByIdAsync(int id, CancellationToken ct = default)
    {
        await using var db = await contextFactory.CreateDbContextAsync(ct);
        return await WithIncludes(db.Assets).FirstOrDefaultAsync(a => a.Id == id, ct);
    }

    public async Task<List<Asset>> FindAsync(AssetQuery query, CancellationToken ct = default)
    {
        await using var db = await contextFactory.CreateDbContextAsync(ct);
        return await ApplyQuery(WithIncludes(db.Assets), query).ToListAsync(ct);
    }

    public async Task<bool> SerialNumberExistsAsync(string serialNumber, int? excludeAssetId = null, CancellationToken ct = default)
    {
        await using var db = await contextFactory.CreateDbContextAsync(ct);
        var normalized = serialNumber.Trim().ToLower();
        return await db.Assets.AnyAsync(
            a => a.SerialNumber.ToLower() == normalized && (excludeAssetId == null || a.Id != excludeAssetId), ct);
    }

    public async Task<Asset> AddAsync(Asset asset, CancellationToken ct = default)
    {
        await using var db = await contextFactory.CreateDbContextAsync(ct);
        db.Assets.Add(asset);
        await db.SaveChangesAsync(ct);
        return asset;
    }

    public async Task UpdateAsync(Asset asset, CancellationToken ct = default)
    {
        await using var db = await contextFactory.CreateDbContextAsync(ct);
        var existing = await db.Assets.FirstOrDefaultAsync(a => a.Id == asset.Id, ct)
                       ?? throw new KeyNotFoundException($"Asset {asset.Id} was not found.");

        // Copies scalar values and foreign keys only - navigation properties are left untouched.
        db.Entry(existing).CurrentValues.SetValues(asset);
        await db.SaveChangesAsync(ct);
    }

    public async Task<bool> DeleteAsync(int id, CancellationToken ct = default)
    {
        await using var db = await contextFactory.CreateDbContextAsync(ct);
        var existing = await db.Assets.FirstOrDefaultAsync(a => a.Id == id, ct);
        if (existing is null)
            return false;

        db.Assets.Remove(existing);
        await db.SaveChangesAsync(ct);
        return true;
    }

    /// <summary>Discriminator values belonging to a category.</summary>
    public static string[] KindsInCategory(AssetCategory category) => category switch
    {
        AssetCategory.Computer => [nameof(AssetKind.Laptop), nameof(AssetKind.Desktop)],
        AssetCategory.Mobile => [nameof(AssetKind.MobilePhone), nameof(AssetKind.Tablet)],
        AssetCategory.OfficeEquipment => [nameof(AssetKind.OfficeEquipment)],
        _ => []
    };

    private static IQueryable<Asset> WithIncludes(IQueryable<Asset> source) =>
        source.AsNoTracking().Include(a => a.Office).Include(a => a.Employee);

    /// <summary>Builds the LINQ query from the criteria. Everything here is translated to SQL.</summary>
    private static IQueryable<Asset> ApplyQuery(IQueryable<Asset> q, AssetQuery query)
    {
        if (!string.IsNullOrWhiteSpace(query.Brand))
        {
            var brand = query.Brand.Trim().ToLower();
            q = q.Where(a => a.Brand.ToLower().Contains(brand));
        }

        if (!string.IsNullOrWhiteSpace(query.Model))
        {
            var model = query.Model.Trim().ToLower();
            q = q.Where(a => a.ModelName.ToLower().Contains(model));
        }

        if (query.OfficeId is not null)
            q = q.Where(a => a.OfficeId == query.OfficeId);
        else if (!string.IsNullOrWhiteSpace(query.OfficeName))
        {
            var office = query.OfficeName.Trim().ToLower();
            q = q.Where(a => a.Office.Name.ToLower().Contains(office) || a.Office.Country.ToLower().Contains(office));
        }

        if (query.PurchaseYear is not null)
            q = q.Where(a => a.PurchaseDate.Year == query.PurchaseYear);

        if (query.Category is not null)
        {
            // Filter on the TPH discriminator column, e.g. AssetType IN ('Laptop', 'Desktop').
            var kinds = KindsInCategory(query.Category.Value);
            q = q.Where(a => kinds.Contains(EF.Property<string>(a, "AssetType")));
        }

        if (query.EmployeeId is not null)
            q = q.Where(a => a.EmployeeId == query.EmployeeId);

        if (query.Assigned is not null)
            q = query.Assigned.Value ? q.Where(a => a.EmployeeId != null) : q.Where(a => a.EmployeeId == null);

        if (query.PurchasedOnOrBefore is not null)
            q = q.Where(a => a.PurchaseDate <= query.PurchasedOnOrBefore);

        if (query.PurchasedAfter is not null)
            q = q.Where(a => a.PurchaseDate > query.PurchasedAfter);

        return q.OrderBy(a => a.Id);
    }
}
