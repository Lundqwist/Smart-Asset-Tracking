// ============================================================================
// AssetRepository.cs
// ----------------------------------------------------------------------------
// The "Repository pattern": a class whose only job is to read and write one
// kind of data (here: assets) in the database. The rest of the program never
// talks to EF Core directly. It asks the repository: "give me all assets",
// "save this asset". This keeps database code in one place and makes the
// services easier to test.
//
// First we declare an INTERFACE (IAssetRepository): a list of methods that
// any asset repository must have. Then we write the CLASS (AssetRepository)
// that does the real work with EF Core. Other classes depend on the
// interface, not the class, so the real implementation could be swapped out
// (for example in tests).
// ============================================================================

using Microsoft.EntityFrameworkCore;
using SmartAssetTracking.Data;
using SmartAssetTracking.Models;

namespace SmartAssetTracking.Repositories;

// Interface names start with "I" by convention. They contain no code, only method signatures.
// Task<List<Asset>> means: "an asynchronous operation that will eventually give a List of Assets".
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

// ": IAssetRepository" = this class implements (fulfils) the interface above.
// We use IDbContextFactory to create a NEW, short-lived database context for each
// operation. A console app runs for a long time, and one long-lived context would
// keep old copies of data in memory. A fresh context each time always sees the latest data.
public class AssetRepository(IDbContextFactory<AppDbContext> contextFactory) : IAssetRepository
{
    // Getting all assets is the same as searching with no criteria at all.
    public Task<List<Asset>> GetAllAsync(CancellationToken ct = default) => FindAsync(new AssetQuery(), ct);

    public async Task<Asset?> GetByIdAsync(int id, CancellationToken ct = default)
    {
        await using var db = await contextFactory.CreateDbContextAsync(ct);

        // FirstOrDefaultAsync returns the first match, or null if nothing matches.
        // That's why the return type is "Asset?" (an asset or null).
        return await WithIncludes(db.Assets).FirstOrDefaultAsync(a => a.Id == id, ct);
    }

    public async Task<List<Asset>> FindAsync(AssetQuery query, CancellationToken ct = default)
    {
        await using var db = await contextFactory.CreateDbContextAsync(ct);

        // ToListAsync() is the moment the SQL query is actually sent to the database.
        // Everything before it only BUILDS the query.
        return await ApplyQuery(WithIncludes(db.Assets), query).ToListAsync(ct);
    }

    // Checks whether another asset already uses this serial number.
    // When updating an asset we pass its own Id in excludeAssetId, so it doesn't "collide with itself".
    public async Task<bool> SerialNumberExistsAsync(string serialNumber, int? excludeAssetId = null, CancellationToken ct = default)
    {
        await using var db = await contextFactory.CreateDbContextAsync(ct);

        // Compare in lower case, so "ABC-1" and "abc-1" count as the same serial number.
        var normalized = serialNumber.Trim().ToLower();
        return await db.Assets.AnyAsync(
            a => a.SerialNumber.ToLower() == normalized && (excludeAssetId == null || a.Id != excludeAssetId), ct);
    }

    public async Task<Asset> AddAsync(Asset asset, CancellationToken ct = default)
    {
        await using var db = await contextFactory.CreateDbContextAsync(ct);
        db.Assets.Add(asset);           // mark as "to be inserted"
        await db.SaveChangesAsync(ct);  // run the INSERT. EF Core then fills in asset.Id automatically
        return asset;
    }

    public async Task UpdateAsync(Asset asset, CancellationToken ct = default)
    {
        await using var db = await contextFactory.CreateDbContextAsync(ct);

        // Load the current row from the database. If it doesn't exist, throw an error.
        // "?? throw ..." means "if the left side is null, throw this exception".
        var existing = await db.Assets.FirstOrDefaultAsync(a => a.Id == asset.Id, ct)
                       ?? throw new KeyNotFoundException($"Asset {asset.Id} was not found.");

        // Copies scalar values and foreign keys only - navigation properties are left untouched.
        // SetValues copies every simple value (Brand, Price, OfficeId, ...) from our
        // object onto the loaded one. EF Core notices which values changed and only
        // updates those columns.
        db.Entry(existing).CurrentValues.SetValues(asset);
        await db.SaveChangesAsync(ct);
    }

    public async Task<bool> DeleteAsync(int id, CancellationToken ct = default)
    {
        await using var db = await contextFactory.CreateDbContextAsync(ct);
        var existing = await db.Assets.FirstOrDefaultAsync(a => a.Id == id, ct);
        if (existing is null)
            return false; // nothing to delete

        db.Assets.Remove(existing);
        await db.SaveChangesAsync(ct);
        return true;
    }

    /// <summary>Discriminator values belonging to a category.</summary>
    // The database doesn't know about our C# categories. It only has the
    // "AssetType" column. So "Computers" means "AssetType is Laptop or Desktop".
    public static string[] KindsInCategory(AssetCategory category) => category switch
    {
        AssetCategory.Computer => [nameof(AssetKind.Laptop), nameof(AssetKind.Desktop)],
        AssetCategory.Mobile => [nameof(AssetKind.MobilePhone), nameof(AssetKind.Tablet)],
        AssetCategory.OfficeEquipment => [nameof(AssetKind.OfficeEquipment)],
        _ => []
    };

    // IQueryable<Asset> is a query that has NOT run yet. We can keep adding to it
    // (Where, OrderBy, Include...), and EF Core turns the whole thing into one SQL statement.
    private static IQueryable<Asset> WithIncludes(IQueryable<Asset> source) =>
        // AsNoTracking: we only read the data, so EF Core doesn't need to watch it for changes (faster).
        // Include: also load the related Office and Employee (a SQL JOIN), so asset.Office.Name works.
        source.AsNoTracking().Include(a => a.Office).Include(a => a.Employee);

    /// <summary>Builds the LINQ query from the criteria. Everything here is translated to SQL.</summary>
    // LINQ (Language Integrated Query) lets us write database queries in C#.
    // Each "q = q.Where(...)" adds another condition. They are combined with AND.
    // Because we only add the conditions that were filled in, one method handles every search.
    private static IQueryable<Asset> ApplyQuery(IQueryable<Asset> q, AssetQuery query)
    {
        if (!string.IsNullOrWhiteSpace(query.Brand))
        {
            var brand = query.Brand.Trim().ToLower();
            // Contains -> SQL "LIKE '%lenovo%'": the brand contains the text anywhere.
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
            // Match either the office name or the country, so "germany" finds "Germany Office".
            var office = query.OfficeName.Trim().ToLower();
            q = q.Where(a => a.Office.Name.ToLower().Contains(office) || a.Office.Country.ToLower().Contains(office));
        }

        if (query.PurchaseYear is not null)
            q = q.Where(a => a.PurchaseDate.Year == query.PurchaseYear);

        if (query.Category is not null)
        {
            // Filter on the TPH discriminator column, e.g. AssetType IN ('Laptop', 'Desktop').
            // EF.Property<string>(a, "AssetType") reads the hidden discriminator column,
            // which has no C# property of its own.
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

        // Always return the results in a predictable order (by Id).
        return q.OrderBy(a => a.Id);
    }
}
