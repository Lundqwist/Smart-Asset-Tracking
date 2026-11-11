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
    public static IServiceCollection AddSmartAssetTracking(this IServiceCollection services, IConfiguration configuration,
        Action<DbContextOptionsBuilder>? configureDatabase = null)
    {
        configureDatabase ??= options =>
        {
            var connectionString = configuration.GetConnectionString("DefaultConnection")
                                   ?? throw new InvalidOperationException("ConnectionStrings:DefaultConnection is missing in appsettings.json.");
            options.UseSqlServer(connectionString, sql => sql.EnableRetryOnFailure(3));
        };

        // Configuration sections bound to strongly typed options.
        services.AddSingleton(configuration.GetSection("Lifecycle").Get<LifecycleOptions>() ?? new LifecycleOptions());
        services.AddSingleton(configuration.GetSection("Currency").Get<CurrencyOptions>() ?? new CurrencyOptions());
        services.AddSingleton(configuration.GetSection("App").Get<AppSettings>() ?? new AppSettings());
        services.AddSingleton(TimeProvider.System);

        // Data access: a short-lived DbContext per operation suits a long-running console app.
        services.AddDbContextFactory<AppDbContext>(configureDatabase);
        services.AddSingleton<DbSeeder>();

        // Repositories
        services.AddSingleton<IAssetRepository, AssetRepository>();
        services.AddSingleton<IOfficeRepository, OfficeRepository>();
        services.AddSingleton<IEmployeeRepository, EmployeeRepository>();
        services.AddSingleton<IUserRepository, UserRepository>();

        // Services
        services.AddSingleton<UserSession>();
        services.AddSingleton<ILifecycleService, LifecycleService>();
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

        // Console UI
        services.AddSingleton<MenuRunner>();
        services.AddSingleton<AssetView>();
        services.AddSingleton<AssetScreens>();
        services.AddSingleton<OfficeScreens>();
        services.AddSingleton<EmployeeScreens>();
        services.AddSingleton<MaintenanceScreens>();
        services.AddSingleton<ReportScreens>();
        services.AddSingleton<UserScreens>();
        services.AddSingleton<Application>();

        return services;
    }
}
