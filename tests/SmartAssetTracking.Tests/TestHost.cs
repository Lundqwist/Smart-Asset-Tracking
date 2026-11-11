// ============================================================================
// TestHost.cs - shared setup for the automated tests
// ----------------------------------------------------------------------------
// WHAT ARE AUTOMATED TESTS?
// Tests are small programs that check our real code and report pass/fail.
// Run them all with "dotnet test". If a change breaks something, a test
// fails and tells you what went wrong, before a user ever sees it.
//
// This class builds the REAL application (the same services, repositories
// and rules) with two differences, so the tests are fast and repeatable:
//   1. The database is EF Core's "InMemory" database: a fake database that
//      lives in memory and is thrown away after each test. SQL Server isn't needed.
//   2. "Today" is always 2026-06-15 (FixedTimeProvider), so lifecycle results
//      such as YELLOW or RED never change depending on the day the tests run.
// ============================================================================

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SmartAssetTracking.Data;
using SmartAssetTracking.Helpers;
using SmartAssetTracking.Models;
using SmartAssetTracking.Services;

namespace SmartAssetTracking.Tests;

/// <summary>A fixed clock so lifecycle calculations are deterministic.</summary>
public sealed class FixedTimeProvider(DateTime localNow) : TimeProvider
{
    public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
    public override DateTimeOffset GetUtcNow() => new(DateTime.SpecifyKind(localNow, DateTimeKind.Utc));
}

/// <summary>Builds the real service graph on top of an isolated EF Core in-memory database.</summary>
public sealed class TestHost : IAsyncDisposable
{
    public static readonly DateTime Today = new(2026, 6, 15);

    private readonly ServiceProvider _provider;
    private readonly bool _deleteDatabaseOnDispose;

    private TestHost(ServiceProvider provider, bool deleteDatabaseOnDispose)
    {
        _provider = provider;
        _deleteDatabaseOnDispose = deleteDatabaseOnDispose;
    }

    /// <param name="sqlServerConnectionString">When set, runs against a real SQL Server database using migrations.</param>
    public static async Task<TestHost> CreateAsync(UserRole? signedInAs = UserRole.Admin, bool seed = false,
        string? sqlServerConnectionString = null)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Lifecycle:LifetimeYears"] = "3",
                ["Lifecycle:YellowThresholdMonths"] = "3",
                ["Lifecycle:RedThresholdMonths"] = "6",
                ["Currency:ReportingCurrency"] = "SEK",
                ["Currency:Currencies:0:Code"] = "USD", ["Currency:Currencies:0:Symbol"] = "$", ["Currency:Currencies:0:Culture"] = "en-US", ["Currency:Currencies:0:RateFromUsd"] = "1",
                ["Currency:Currencies:1:Code"] = "SEK", ["Currency:Currencies:1:Symbol"] = "kr", ["Currency:Currencies:1:Culture"] = "sv-SE", ["Currency:Currencies:1:RateFromUsd"] = "10",
                ["Currency:Currencies:2:Code"] = "EUR", ["Currency:Currencies:2:Symbol"] = "€", ["Currency:Currencies:2:Culture"] = "de-DE", ["Currency:Currencies:2:RateFromUsd"] = "0.9",
                ["Currency:Currencies:3:Code"] = "TRY", ["Currency:Currencies:3:Symbol"] = "₺", ["Currency:Currencies:3:Culture"] = "tr-TR", ["Currency:Currencies:3:RateFromUsd"] = "40",
                ["App:ExportFolder"] = Path.Combine(Path.GetTempPath(), "smartasset-tests", Guid.NewGuid().ToString("N")),
            })
            .Build();

        var databaseName = Guid.NewGuid().ToString();
        var services = new ServiceCollection()
            .AddLogging(b => b.SetMinimumLevel(LogLevel.Warning))
            .AddSmartAssetTracking(configuration, options =>
            {
                if (sqlServerConnectionString is null) options.UseInMemoryDatabase(databaseName);
                else options.UseSqlServer(sqlServerConnectionString);
            });
        services.AddSingleton<TimeProvider>(new FixedTimeProvider(Today));

        var host = new TestHost(services.BuildServiceProvider(), deleteDatabaseOnDispose: sqlServerConnectionString is not null);

        await using (var db = await host.Get<IDbContextFactory<AppDbContext>>().CreateDbContextAsync())
        {
            if (sqlServerConnectionString is null)
                await db.Database.EnsureCreatedAsync(); // applies HasData (the four offices)
            else
                await db.Database.MigrateAsync();       // exercises the real migrations
        }

        if (seed)
            await host.Get<DbSeeder>().SeedAsync();

        if (signedInAs is not null)
            host.Get<UserSession>().SignIn(new User { Id = 999, Username = "test", Role = signedInAs.Value });

        return host;
    }

    public T Get<T>() where T : notnull => _provider.GetRequiredService<T>();

    public async ValueTask DisposeAsync()
    {
        if (_deleteDatabaseOnDispose)
        {
            await using var db = await Get<IDbContextFactory<AppDbContext>>().CreateDbContextAsync();
            await db.Database.EnsureDeletedAsync();
        }

        await _provider.DisposeAsync();
    }
}
