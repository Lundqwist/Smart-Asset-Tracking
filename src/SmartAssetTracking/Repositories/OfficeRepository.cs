// ============================================================================
// OfficeRepository.cs
// ----------------------------------------------------------------------------
// Reads and writes offices in the database. It works the same way as
// AssetRepository (see the comments there for a longer explanation of the
// repository pattern, interfaces and DbContext factories).
// ============================================================================

using Microsoft.EntityFrameworkCore;
using SmartAssetTracking.Data;
using SmartAssetTracking.Models;

namespace SmartAssetTracking.Repositories;

// The contract: everything you can do with offices in the database.
public interface IOfficeRepository
{
    Task<List<Office>> GetAllAsync(CancellationToken ct = default);
    Task<Office?> GetByIdAsync(int id, CancellationToken ct = default);
    Task<bool> NameExistsAsync(string name, int? excludeOfficeId = null, CancellationToken ct = default);
    Task<int> CountAssetsAsync(int officeId, CancellationToken ct = default);
    Task<Office> AddAsync(Office office, CancellationToken ct = default);
    Task UpdateAsync(Office office, CancellationToken ct = default);
    Task<bool> DeleteAsync(int id, CancellationToken ct = default);
}

public class OfficeRepository(IDbContextFactory<AppDbContext> contextFactory) : IOfficeRepository
{
    public async Task<List<Office>> GetAllAsync(CancellationToken ct = default)
    {
        await using var db = await contextFactory.CreateDbContextAsync(ct);
        // SELECT * FROM Offices ORDER BY Id
        return await db.Offices.AsNoTracking().OrderBy(o => o.Id).ToListAsync(ct);
    }

    public async Task<Office?> GetByIdAsync(int id, CancellationToken ct = default)
    {
        await using var db = await contextFactory.CreateDbContextAsync(ct);
        return await db.Offices.AsNoTracking().FirstOrDefaultAsync(o => o.Id == id, ct);
    }

    // Used to stop two offices from getting the same name.
    public async Task<bool> NameExistsAsync(string name, int? excludeOfficeId = null, CancellationToken ct = default)
    {
        await using var db = await contextFactory.CreateDbContextAsync(ct);
        var normalized = name.Trim().ToLower();
        return await db.Offices.AnyAsync(o => o.Name.ToLower() == normalized && (excludeOfficeId == null || o.Id != excludeOfficeId), ct);
    }

    // How many assets are in this office? (We refuse to delete offices that still have assets.)
    public async Task<int> CountAssetsAsync(int officeId, CancellationToken ct = default)
    {
        await using var db = await contextFactory.CreateDbContextAsync(ct);
        return await db.Assets.CountAsync(a => a.OfficeId == officeId, ct);
    }

    public async Task<Office> AddAsync(Office office, CancellationToken ct = default)
    {
        await using var db = await contextFactory.CreateDbContextAsync(ct);
        db.Offices.Add(office);
        await db.SaveChangesAsync(ct);
        return office;
    }

    public async Task UpdateAsync(Office office, CancellationToken ct = default)
    {
        await using var db = await contextFactory.CreateDbContextAsync(ct);
        var existing = await db.Offices.FirstOrDefaultAsync(o => o.Id == office.Id, ct)
                       ?? throw new KeyNotFoundException($"Office {office.Id} was not found.");
        db.Entry(existing).CurrentValues.SetValues(office); // copy the new values onto the loaded row
        await db.SaveChangesAsync(ct);
    }

    public async Task<bool> DeleteAsync(int id, CancellationToken ct = default)
    {
        await using var db = await contextFactory.CreateDbContextAsync(ct);
        var existing = await db.Offices.FirstOrDefaultAsync(o => o.Id == id, ct);
        if (existing is null)
            return false;
        db.Offices.Remove(existing);
        await db.SaveChangesAsync(ct);
        return true;
    }
}
