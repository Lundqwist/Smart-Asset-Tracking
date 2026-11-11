// ============================================================================
// EmployeeRepository.cs
// ----------------------------------------------------------------------------
// Reads and writes employees in the database. It follows the same pattern as
// AssetRepository (see the comments there for a longer explanation).
// ============================================================================

using Microsoft.EntityFrameworkCore;
using SmartAssetTracking.Data;
using SmartAssetTracking.Models;

namespace SmartAssetTracking.Repositories;

public interface IEmployeeRepository
{
    Task<List<Employee>> GetAllAsync(CancellationToken ct = default);
    Task<Employee?> GetByIdAsync(int id, CancellationToken ct = default);
    Task<bool> EmailExistsAsync(string email, int? excludeEmployeeId = null, CancellationToken ct = default);
    Task<Employee> AddAsync(Employee employee, CancellationToken ct = default);
    Task UpdateAsync(Employee employee, CancellationToken ct = default);
    Task<bool> DeleteAsync(int id, CancellationToken ct = default);
}

public class EmployeeRepository(IDbContextFactory<AppDbContext> contextFactory) : IEmployeeRepository
{
    public async Task<List<Employee>> GetAllAsync(CancellationToken ct = default)
    {
        await using var db = await contextFactory.CreateDbContextAsync(ct);
        return await db.Employees.AsNoTracking()
            // Load each employee's assets, and for each asset also its office.
            // ThenInclude goes one step deeper: Employee -> AssignedAssets -> Office.
            .Include(e => e.AssignedAssets).ThenInclude(a => a.Office)
            .OrderBy(e => e.FullName)   // alphabetical order
            .ToListAsync(ct);
    }

    public async Task<Employee?> GetByIdAsync(int id, CancellationToken ct = default)
    {
        await using var db = await contextFactory.CreateDbContextAsync(ct);
        return await db.Employees.AsNoTracking()
            .Include(e => e.AssignedAssets).ThenInclude(a => a.Office)
            .FirstOrDefaultAsync(e => e.Id == id, ct);
    }

    // Used to stop two employees from sharing the same email address.
    public async Task<bool> EmailExistsAsync(string email, int? excludeEmployeeId = null, CancellationToken ct = default)
    {
        await using var db = await contextFactory.CreateDbContextAsync(ct);
        var normalized = email.Trim().ToLower();
        return await db.Employees.AnyAsync(e => e.Email.ToLower() == normalized && (excludeEmployeeId == null || e.Id != excludeEmployeeId), ct);
    }

    public async Task<Employee> AddAsync(Employee employee, CancellationToken ct = default)
    {
        await using var db = await contextFactory.CreateDbContextAsync(ct);
        db.Employees.Add(employee);
        await db.SaveChangesAsync(ct);
        return employee;
    }

    public async Task UpdateAsync(Employee employee, CancellationToken ct = default)
    {
        await using var db = await contextFactory.CreateDbContextAsync(ct);
        var existing = await db.Employees.FirstOrDefaultAsync(e => e.Id == employee.Id, ct)
                       ?? throw new KeyNotFoundException($"Employee {employee.Id} was not found.");
        db.Entry(existing).CurrentValues.SetValues(employee);
        await db.SaveChangesAsync(ct);
    }

    public async Task<bool> DeleteAsync(int id, CancellationToken ct = default)
    {
        await using var db = await contextFactory.CreateDbContextAsync(ct);
        // Load assigned assets so EF also clears the foreign keys for providers without SET NULL support.
        // SQL Server already sets EmployeeId to NULL by itself (ON DELETE SET NULL), but loading
        // the assets makes EF Core do it too. That way it also works with the in-memory test database.
        var existing = await db.Employees.Include(e => e.AssignedAssets).FirstOrDefaultAsync(e => e.Id == id, ct);
        if (existing is null)
            return false;
        db.Employees.Remove(existing);
        await db.SaveChangesAsync(ct);
        return true;
    }
}
