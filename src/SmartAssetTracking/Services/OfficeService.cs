// ============================================================================
// OfficeService.cs
// ----------------------------------------------------------------------------
// Business rules for offices (Level 3: global office management).
// Only Admins may create, edit or delete offices, and an office that still
// has assets can't be deleted. See AssetService.cs for a longer explanation
// of how services work.
// ============================================================================

using Microsoft.Extensions.Logging;
using SmartAssetTracking.Models;
using SmartAssetTracking.Repositories;

namespace SmartAssetTracking.Services;

public interface IOfficeService
{
    Task<IReadOnlyList<Office>> GetAllAsync(CancellationToken ct = default);
    Task<Office> GetByIdAsync(int id, CancellationToken ct = default);
    Task<Office> CreateAsync(string name, string country, string currencyCode, CancellationToken ct = default);
    Task UpdateAsync(int id, string name, string country, string currencyCode, CancellationToken ct = default);
    Task DeleteAsync(int id, CancellationToken ct = default);
}

public class OfficeService(
    IOfficeRepository offices,
    ICurrencyService currency,
    UserSession session,
    ILogger<OfficeService> logger) : IOfficeService
{
    // Reading offices is allowed for everyone, so there is no role check here.
    public async Task<IReadOnlyList<Office>> GetAllAsync(CancellationToken ct = default) => await offices.GetAllAsync(ct);

    public async Task<Office> GetByIdAsync(int id, CancellationToken ct = default) =>
        await offices.GetByIdAsync(id, ct) ?? throw new NotFoundException($"No office with ID {id} exists.");

    public async Task<Office> CreateAsync(string name, string country, string currencyCode, CancellationToken ct = default)
    {
        session.Require(UserRole.Admin);
        await ValidateAsync(name, country, currencyCode, null, ct);

        var office = new Office { Name = name.Trim(), Country = country.Trim(), CurrencyCode = currencyCode.Trim().ToUpperInvariant() };
        await offices.AddAsync(office, ct);
        logger.LogInformation("User {User} created office {Office}", session.CurrentUser?.Username, office);
        return office;
    }

    public async Task UpdateAsync(int id, string name, string country, string currencyCode, CancellationToken ct = default)
    {
        session.Require(UserRole.Admin);
        var office = await GetByIdAsync(id, ct);
        await ValidateAsync(name, country, currencyCode, id, ct);

        office.Name = name.Trim();
        office.Country = country.Trim();
        office.CurrencyCode = currencyCode.Trim().ToUpperInvariant();
        await offices.UpdateAsync(office, ct);
        logger.LogInformation("User {User} updated office {Office}", session.CurrentUser?.Username, office);
    }

    public async Task DeleteAsync(int id, CancellationToken ct = default)
    {
        session.Require(UserRole.Admin);

        // Protect the data: assets must always belong to an office.
        var count = await offices.CountAssetsAsync(id, ct);
        if (count > 0)
            throw new ValidationException($"The office still has {count} asset(s). Move or delete them first.");

        if (!await offices.DeleteAsync(id, ct))
            throw new NotFoundException($"No office with ID {id} exists.");
        logger.LogInformation("User {User} deleted office {OfficeId}", session.CurrentUser?.Username, id);
    }

    private async Task ValidateAsync(string name, string country, string currencyCode, int? existingId, CancellationToken ct)
    {
        var errors = new ValidationErrors()
            .RequireText(name, "Office name", 100)
            .RequireText(country, "Country", 60)
            // The currency must exist in appsettings.json, otherwise we can't convert prices.
            .Require(!string.IsNullOrWhiteSpace(currencyCode) && currency.IsSupported(currencyCode.Trim()),
                $"Currency '{currencyCode}' is not supported. Configure it in appsettings.json first.");

        if (!string.IsNullOrWhiteSpace(name) && await offices.NameExistsAsync(name, existingId, ct))
            errors.Add($"An office named '{name.Trim()}' already exists.");

        errors.ThrowIfAny();
    }
}
