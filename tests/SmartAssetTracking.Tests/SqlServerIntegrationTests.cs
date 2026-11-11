// ============================================================================
// SqlServerIntegrationTests.cs - one test against a REAL SQL Server database
// ----------------------------------------------------------------------------
// A "unit test" checks one piece in isolation (with the fake InMemory database).
// An "integration test" checks that pieces work TOGETHER with real systems.
// This test creates a temporary database on SQL Server Express, runs the real
// migrations, tests the queries and CRUD, and deletes the database afterwards.
//
// It is skipped by default, because it needs SQL Server installed. To run it:
//     $env:SMARTASSET_SQL_TESTS = "1"; dotnet test
// ============================================================================

using SmartAssetTracking.Models;
using SmartAssetTracking.Repositories;
using SmartAssetTracking.Services;

namespace SmartAssetTracking.Tests;

/// <summary>
/// Runs only when the environment variable SMARTASSET_SQL_TESTS=1 is set.
/// Creates a throw-away database on SQL Server Express, applies the migrations and checks
/// that all LINQ queries translate to SQL. The database is dropped afterwards.
/// </summary>
public sealed class SqlServerFactAttribute : FactAttribute
{
    public SqlServerFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("SMARTASSET_SQL_TESTS") != "1")
            Skip = "Set SMARTASSET_SQL_TESTS=1 to run SQL Server integration tests.";
    }
}

public class SqlServerIntegrationTests
{
    private static string ConnectionString =>
        Environment.GetEnvironmentVariable("SMARTASSET_SQL_CONNECTION")
        ?? $@"Server=.\SQLEXPRESS;Database=SmartAssetTracking_Test_{Guid.NewGuid():N};Trusted_Connection=True;TrustServerCertificate=True";

    [SqlServerFact]
    public async Task Migrations_seed_and_queries_work_on_sql_server()
    {
        await using var host = await TestHost.CreateAsync(seed: true, sqlServerConnectionString: ConnectionString);
        var assets = host.Get<IAssetService>();

        Assert.Equal(20, (await assets.GetAllSortedAsync()).Count);
        Assert.NotEmpty(await assets.SearchAsync(new AssetQuery { Brand = "LENOVO" }));
        Assert.NotEmpty(await assets.SearchAsync(new AssetQuery { Model = "iphone" }));
        Assert.NotEmpty(await assets.SearchAsync(new AssetQuery { OfficeName = "germany" }));
        Assert.NotEmpty(await assets.SearchAsync(new AssetQuery { PurchaseYear = TestHost.Today.Year - 1 }));
        Assert.All(await assets.SearchAsync(new AssetQuery { Category = AssetCategory.Computer }), a => Assert.IsAssignableFrom<ComputerAsset>(a));
        Assert.NotEmpty(await assets.SearchAsync(new AssetQuery { Assigned = false }));
        Assert.NotEmpty(await assets.GetExpiredAsync());
        Assert.NotEmpty(await assets.GetExpiringSoonAsync());
        Assert.NotEmpty(await assets.GetMaintenanceScheduleAsync());

        // CRUD round trip
        var created = await assets.CreateAsync(new AssetInput(AssetKind.Tablet, "Apple", "iPad Pro", "SQL-TEST-1",
            new DateTime(2025, 3, 1), 1099m, 4));
        var updated = await assets.UpdateAsync(created.Id, new AssetInput(AssetKind.Tablet, "Apple", "iPad Pro M4", "SQL-TEST-1",
            new DateTime(2025, 3, 1), 1199m, 1));
        Assert.Equal("Sweden Office", updated.Office.Name);
        await assets.DeleteAsync(created.Id);

        // Deleting an employee un-assigns their assets (ON DELETE SET NULL)
        var employees = host.Get<IEmployeeService>();
        var sarah = (await employees.GetAllAsync()).Single(e => e.FullName == "Sarah Johnson");
        await employees.DeleteAsync(sarah.Id);
        Assert.Empty(await assets.SearchAsync(new AssetQuery { EmployeeId = sarah.Id }));

        // Offices with assets cannot be deleted
        await Assert.ThrowsAsync<ValidationException>(() => host.Get<IOfficeService>().DeleteAsync(1));

        var stats = await host.Get<IReportService>().GetDashboardAsync();
        Assert.Equal(20, stats.TotalAssets);
    }
}
