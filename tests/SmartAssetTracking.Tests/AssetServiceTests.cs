using SmartAssetTracking.Models;
using SmartAssetTracking.Repositories;
using SmartAssetTracking.Services;

namespace SmartAssetTracking.Tests;

public class AssetServiceTests
{
    private static AssetInput ValidInput(string serial = "SN-1", AssetKind kind = AssetKind.Laptop) =>
        new(kind, "Lenovo", "ThinkPad X1", serial, new DateTime(2025, 1, 10), 1500m, OfficeId: 1);

    [Fact]
    public async Task Create_saves_asset_with_correct_type_and_office()
    {
        await using var host = await TestHost.CreateAsync();
        var sut = host.Get<IAssetService>();

        var created = await sut.CreateAsync(ValidInput());

        Assert.IsType<Laptop>(created);
        Assert.IsAssignableFrom<ComputerAsset>(created);
        Assert.Equal("Sweden Office", created.Office.Name);
        Assert.Equal(new DateTime(2027, 1, 10), created.WarrantyExpirationDate); // default 2-year warranty
    }

    [Fact]
    public async Task Create_rejects_invalid_input_with_all_errors()
    {
        await using var host = await TestHost.CreateAsync();
        var sut = host.Get<IAssetService>();
        var input = ValidInput() with
        {
            Brand = " ",
            PurchasePriceUsd = 0,
            PurchaseDate = TestHost.Today.AddDays(1),
            OfficeId = 42
        };

        var ex = await Assert.ThrowsAsync<ValidationException>(() => sut.CreateAsync(input));

        Assert.Contains(ex.Errors, e => e.Contains("Brand"));
        Assert.Contains(ex.Errors, e => e.Contains("price"));
        Assert.Contains(ex.Errors, e => e.Contains("future"));
        Assert.Contains(ex.Errors, e => e.Contains("Office"));
    }

    [Fact]
    public async Task Create_rejects_duplicate_serial_number()
    {
        await using var host = await TestHost.CreateAsync();
        var sut = host.Get<IAssetService>();
        await sut.CreateAsync(ValidInput("DUP-1"));

        var ex = await Assert.ThrowsAsync<ValidationException>(() => sut.CreateAsync(ValidInput("dup-1")));
        Assert.Contains(ex.Errors, e => e.Contains("already registered"));
    }

    [Fact]
    public async Task Employees_cannot_modify_assets()
    {
        await using var host = await TestHost.CreateAsync(UserRole.Employee);

        await Assert.ThrowsAsync<AccessDeniedException>(() => host.Get<IAssetService>().CreateAsync(ValidInput()));
    }

    [Fact]
    public async Task Update_changes_values_but_not_type()
    {
        await using var host = await TestHost.CreateAsync();
        var sut = host.Get<IAssetService>();
        var created = await sut.CreateAsync(ValidInput());

        var updated = await sut.UpdateAsync(created.Id, ValidInput() with { ModelName = "ThinkPad X1 Gen 12", OfficeId = 3 });
        Assert.Equal("ThinkPad X1 Gen 12", updated.ModelName);
        Assert.Equal("Germany Office", updated.Office.Name);

        await Assert.ThrowsAsync<ValidationException>(() =>
            sut.UpdateAsync(created.Id, ValidInput(kind: AssetKind.Tablet)));
    }

    [Fact]
    public async Task Delete_removes_asset()
    {
        await using var host = await TestHost.CreateAsync();
        var sut = host.Get<IAssetService>();
        var created = await sut.CreateAsync(ValidInput());

        await sut.DeleteAsync(created.Id);

        await Assert.ThrowsAsync<NotFoundException>(() => sut.GetByIdAsync(created.Id));
        await Assert.ThrowsAsync<NotFoundException>(() => sut.DeleteAsync(created.Id));
    }

    [Fact]
    public async Task List_is_sorted_by_category_then_purchase_date()
    {
        await using var host = await TestHost.CreateAsync();
        var sut = host.Get<IAssetService>();
        await sut.CreateAsync(ValidInput("A", AssetKind.MobilePhone) with { PurchaseDate = new DateTime(2024, 1, 1) });
        await sut.CreateAsync(ValidInput("B", AssetKind.Laptop) with { PurchaseDate = new DateTime(2025, 1, 1) });
        await sut.CreateAsync(ValidInput("C", AssetKind.OfficeEquipment) with { PurchaseDate = new DateTime(2023, 1, 1) });
        await sut.CreateAsync(ValidInput("D", AssetKind.Desktop) with { PurchaseDate = new DateTime(2023, 6, 1) });

        var serials = (await sut.GetAllSortedAsync()).Select(a => a.SerialNumber).ToList();

        Assert.Equal(["D", "B", "A", "C"], serials);
    }

    [Fact]
    public async Task Search_and_filters_use_linq_criteria()
    {
        await using var host = await TestHost.CreateAsync(seed: true);
        var sut = host.Get<IAssetService>();

        var lenovo = await sut.SearchAsync(new AssetQuery { Brand = "lenovo" });
        Assert.NotEmpty(lenovo);
        Assert.All(lenovo, a => Assert.Equal("Lenovo", a.Brand));

        var mobiles = await sut.SearchAsync(new AssetQuery { Category = AssetCategory.Mobile });
        Assert.All(mobiles, a => Assert.IsAssignableFrom<MobileAsset>(a));

        var germany = await sut.SearchAsync(new AssetQuery { OfficeId = 3 });
        Assert.All(germany, a => Assert.Equal("Germany Office", a.Office.Name));

        var lifecycle = host.Get<ILifecycleService>();
        var expired = await sut.GetExpiredAsync();
        Assert.NotEmpty(expired);
        Assert.All(expired, a => Assert.True(lifecycle.IsExpired(a)));

        var expiring = await sut.GetExpiringSoonAsync();
        Assert.NotEmpty(expiring);
        Assert.All(expiring, a => Assert.True(lifecycle.IsExpiringSoon(a)));
    }

    [Fact]
    public async Task Assign_and_record_maintenance()
    {
        await using var host = await TestHost.CreateAsync(seed: true);
        var sut = host.Get<IAssetService>();
        var employee = (await host.Get<IEmployeeService>().GetAllAsync()).First();
        var asset = await sut.CreateAsync(ValidInput("M-1"));

        await sut.AssignAsync(asset.Id, employee.Id);
        await sut.RecordMaintenanceAsync(asset.Id, TestHost.Today, TestHost.Today.AddMonths(6), "Cleaned fans");

        var reloaded = await sut.GetByIdAsync(asset.Id);
        Assert.Equal(employee.Id, reloaded.EmployeeId);
        Assert.Equal(TestHost.Today, reloaded.LastMaintenanceDate);
        Assert.Equal("Cleaned fans", reloaded.MaintenanceNotes);

        await Assert.ThrowsAsync<ValidationException>(() =>
            sut.RecordMaintenanceAsync(asset.Id, TestHost.Today, TestHost.Today.AddDays(-1), null));
    }
}
