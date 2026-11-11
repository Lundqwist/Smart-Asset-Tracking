// ============================================================================
// ServiceRegistration.cs
// ----------------------------------------------------------------------------
// DEPENDENCY INJECTION (DI), explained simply:
//
// Many classes need other classes to do their work. AssetService needs an
// AssetRepository, which needs a database context factory, and so on.
// Instead of every class creating its own helpers with "new", we register
// all classes ONCE in a "service collection" (below). When a class is
// needed, the DI container builds it and automatically passes in everything
// its constructor asks for.
//
// Benefits:
//   * Classes don't need to know how to build their helpers.
//   * Tests can swap a real piece for a fake one (e.g. an in-memory database).
//   * All the wiring of the application is visible in this one file.
//
// "AddSingleton<IAssetService, AssetService>()" means: "when someone asks for
// an IAssetService, give them an AssetService, and reuse the SAME object
// every time" (singleton = only one instance for the whole program).
// ============================================================================

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SmartAssetTracking.Data;
using SmartAssetTracking.Reports;
using SmartAssetTracking.Repositories;
using SmartAssetTracking.Services;
using SmartAssetTracking.UI;
using SmartAssetTracking.UI.Screens;

namespace SmartAssetTracking.Helpers;

/// <summary>Dependency-injection setup for the whole application.</summary>
public static class ServiceRegistration
{
    /// <param name="configureDatabase">
    /// Optional override of the database provider (used by the unit tests). Defaults to SQL Server.
    /// </param>
    // An extension method on IServiceCollection, so Program.cs can write services.AddSmartAssetTracking(configuration).
    // Action<DbContextOptionsBuilder> is a "delegate": a parameter that holds a piece of code (a function).
    public static IServiceCollection AddSmartAssetTracking(this IServiceCollection services, IConfiguration configuration,
        Action<DbContextOptionsBuilder>? configureDatabase = null)
    {
        // "??=" means "if it is null, give it this value". If the caller (the tests)
        // didn't pass a database setup, we use SQL Server with the connection string from appsettings.json.
        configureDatabase ??= options =>
        {
            var connectionString = configuration.GetConnectionString("DefaultConnection")
                                   ?? throw new InvalidOperationException("ConnectionStrings:DefaultConnection is missing in appsettings.json.");

            // EnableRetryOnFailure: if SQL Server has a short hiccup, try again up to 3 times.
            options.UseSqlServer(connectionString, sql => sql.EnableRetryOnFailure(3));
        };

        // Configuration sections bound to strongly typed options.
        // GetSection("Lifecycle").Get<LifecycleOptions>() reads the "Lifecycle" part of
        // appsettings.json and copies the values into a LifecycleOptions object.
        // If the section is missing we use the defaults (new LifecycleOptions()).
        services.AddSingleton(configuration.GetSection("Lifecycle").Get<LifecycleOptions>() ?? new LifecycleOptions());
        services.AddSingleton(configuration.GetSection("Currency").Get<CurrencyOptions>() ?? new CurrencyOptions());
        services.AddSingleton(configuration.GetSection("App").Get<AppSettings>() ?? new AppSettings());

        // The real clock. Tests register a fake clock with a fixed date instead.
        services.AddSingleton(TimeProvider.System);

        // Data access: a short-lived DbContext per operation suits a long-running console app.
        services.AddDbContextFactory<AppDbContext>(configureDatabase);
        services.AddSingleton<DbSeeder>();

        // Repositories (database access)
        services.AddSingleton<IAssetRepository, AssetRepository>();
        services.AddSingleton<IOfficeRepository, OfficeRepository>();
        services.AddSingleton<IEmployeeRepository, EmployeeRepository>();
        services.AddSingleton<IUserRepository, UserRepository>();

        // Services (business rules)
        services.AddSingleton<UserSession>();
        services.AddSingleton<ILifecycleService, LifecycleService>();

        // CurrencyService also needs an HttpClient (for the live exchange rates), so we
        // build it ourselves with a "factory function". "sp" is the service provider,
        // and GetRequiredService fetches the other services it needs.
        services.AddSingleton<ICurrencyService>(sp => new CurrencyService(
            sp.GetRequiredService<CurrencyOptions>(),
            new HttpClient { Timeout = TimeSpan.FromSeconds(10) },
            sp.GetRequiredService<ILogger<CurrencyService>>()));
        services.AddSingleton<IAssetService, AssetService>();
        services.AddSingleton<IOfficeService, OfficeService>();
        services.AddSingleton<IEmployeeService, EmployeeService>();
        services.AddSingleton<IAuthService, AuthService>();
        services.AddSingleton<IReportService, ReportService>();
        services.AddSingleton<IExportService, ExportService>();

        // Console UI (menus and screens)
        services.AddSingleton<MenuRunner>();
        services.AddSingleton<AssetView>();
        services.AddSingleton<AssetScreens>();
        services.AddSingleton<OfficeScreens>();
        services.AddSingleton<EmployeeScreens>();
        services.AddSingleton<MaintenanceScreens>();
        services.AddSingleton<ReportScreens>();
        services.AddSingleton<UserScreens>();
        services.AddSingleton<Application>();

        // Returning the collection allows chaining: new ServiceCollection().AddLogging(...).AddSmartAssetTracking(...)
        return services;
    }
}
