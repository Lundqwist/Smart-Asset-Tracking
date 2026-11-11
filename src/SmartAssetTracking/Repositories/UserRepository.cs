using Microsoft.EntityFrameworkCore;
using SmartAssetTracking.Data;
using SmartAssetTracking.Models;

namespace SmartAssetTracking.Repositories;

public interface IUserRepository
{
    Task<List<User>> GetAllAsync(CancellationToken ct = default);
    Task<User?> GetByIdAsync(int id, CancellationToken ct = default);
    Task<User?> GetByUsernameAsync(string username, CancellationToken ct = default);
    Task<User> AddAsync(User user, CancellationToken ct = default);
    Task UpdateAsync(User user, CancellationToken ct = default);
    Task<bool> DeleteAsync(int id, CancellationToken ct = default);
}

public class UserRepository(IDbContextFactory<AppDbContext> contextFactory) : IUserRepository
{
    public async Task<List<User>> GetAllAsync(CancellationToken ct = default)
    {
        await using var db = await contextFactory.CreateDbContextAsync(ct);
        return await db.Users.AsNoTracking().Include(u => u.Employee).OrderBy(u => u.Username).ToListAsync(ct);
    }

    public async Task<User?> GetByIdAsync(int id, CancellationToken ct = default)
    {
        await using var db = await contextFactory.CreateDbContextAsync(ct);
        return await db.Users.AsNoTracking().Include(u => u.Employee).FirstOrDefaultAsync(u => u.Id == id, ct);
    }

    public async Task<User?> GetByUsernameAsync(string username, CancellationToken ct = default)
    {
        await using var db = await contextFactory.CreateDbContextAsync(ct);
        var normalized = username.Trim().ToLower();
        return await db.Users.AsNoTracking().Include(u => u.Employee)
            .FirstOrDefaultAsync(u => u.Username.ToLower() == normalized, ct);
    }

    public async Task<User> AddAsync(User user, CancellationToken ct = default)
    {
        await using var db = await contextFactory.CreateDbContextAsync(ct);
        db.Users.Add(user);
        await db.SaveChangesAsync(ct);
        return user;
    }

    public async Task UpdateAsync(User user, CancellationToken ct = default)
    {
        await using var db = await contextFactory.CreateDbContextAsync(ct);
        var existing = await db.Users.FirstOrDefaultAsync(u => u.Id == user.Id, ct)
                       ?? throw new KeyNotFoundException($"User {user.Id} was not found.");
        db.Entry(existing).CurrentValues.SetValues(user);
        await db.SaveChangesAsync(ct);
    }

    public async Task<bool> DeleteAsync(int id, CancellationToken ct = default)
    {
        await using var db = await contextFactory.CreateDbContextAsync(ct);
        var existing = await db.Users.FirstOrDefaultAsync(u => u.Id == id, ct);
        if (existing is null)
            return false;
        db.Users.Remove(existing);
        await db.SaveChangesAsync(ct);
        return true;
    }
}
