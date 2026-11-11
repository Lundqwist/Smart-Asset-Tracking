using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SmartAssetTracking.Data;
using SmartAssetTracking.Helpers;
using SmartAssetTracking.Models;
using SmartAssetTracking.Services;
using SmartAssetTracking.UI.Screens;
using Spectre.Console;

namespace SmartAssetTracking.UI;

/// <summary>Application shell: database start-up, login loop and main menu.</summary>
public class Application(
    IDbContextFactory<AppDbContext> contextFactory,
    DbSeeder seeder,
    IAuthService auth,
    UserSession session,
    MenuRunner menu,
    AssetScreens assetScreens,
    OfficeScreens officeScreens,
    EmployeeScreens employeeScreens,
    MaintenanceScreens maintenanceScreens,
    ReportScreens reportScreens,
    UserScreens userScreens,
    AppSettings settings,
    ILogger<Application> logger)
{
    private bool _exitRequested;

    public async Task<int> RunAsync()
    {
        AnsiConsole.Clear();
        ConsoleUi.Banner();

        if (!await InitializeDatabaseAsync())
            return 1;

        while (!_exitRequested)
        {
            if (!await LoginAsync())
                return 0;

            await MainMenuAsync();
            auth.Logout();
        }

        AnsiConsole.Clear();
        ConsoleUi.Banner();
        AnsiConsole.MarkupLine("[aqua]Goodbye![/]");
        return 0;
    }

    /// <summary>Applies pending EF Core migrations and seeds demo data.</summary>
    private async Task<bool> InitializeDatabaseAsync()
    {
        try
        {
            await AnsiConsole.Status().Spinner(Spinner.Known.Dots).StartAsync("Connecting to SQL Server...", async ctx =>
            {
                await using var db = await contextFactory.CreateDbContextAsync();
                ctx.Status("Applying database migrations...");
                if (db.Database.IsRelational())
                    await db.Database.MigrateAsync();
                else
                    await db.Database.EnsureCreatedAsync(); // non-relational providers (tests)
                ctx.Status("Seeding demo data...");
                await seeder.SeedAsync();
            });
            ConsoleUi.Success("Database ready.");
            return true;
        }
        catch (Exception ex) when (ex is SqlException or InvalidOperationException or DbUpdateException)
        {
            logger.LogCritical(ex, "Could not initialize the database");
            ConsoleUi.Error("Could not connect to or create the database.");
            AnsiConsole.MarkupLine($"[grey]{Markup.Escape(ex.Message)}[/]");
            AnsiConsole.MarkupLine("\n[yellow]Check that SQL Server Express is running and that the connection string in " +
                                   "[bold]appsettings.json[/] is correct (default: Server=.\\SQLEXPRESS).[/]");
            ConsoleUi.Pause();
            return false;
        }
    }

    /// <summary>Asks for credentials. Returns false if the user chooses to exit.</summary>
    private async Task<bool> LoginAsync()
    {
        while (true)
        {
            AnsiConsole.Clear();
            ConsoleUi.Banner();
            AnsiConsole.Write(new Panel(new Markup(
                    "[grey]Demo accounts:[/]  [bold]admin[/] / 1234   ·   [bold]manager[/] / 1234   ·   [bold]employee[/] / 1234"))
                .Header("[bold aqua] LOGIN [/]").BorderColor(Color.Aqua).RoundedBorder());

            for (var attempt = 1; attempt <= settings.MaxLoginAttempts; attempt++)
            {
                var username = AnsiConsole.Prompt(new TextPrompt<string>("[aqua]Username:[/]").AllowEmpty());
                var password = ConsoleInput.Secret("Password");

                if (await auth.LoginAsync(username, password))
                    return true;

                ConsoleUi.Error($"Invalid username or password ({attempt}/{settings.MaxLoginAttempts}).");
            }

            ConsoleUi.Warning("Too many failed attempts.");
            if (!ConsoleInput.Confirm("Try again?", true))
                return false;
        }
    }

    private Task MainMenuAsync()
    {
        var items = new List<MenuItem>
        {
            new("Dashboard", reportScreens.DashboardAsync),
            new("My assets", employeeScreens.MyAssetsAsync),
            new("Assets", assetScreens.RunAsync, PauseAfter: false),
            new("Offices", officeScreens.RunAsync, PauseAfter: false),
            new("Employees & assignments", employeeScreens.RunAsync, UserRole.Manager, PauseAfter: false),
            new("Maintenance", maintenanceScreens.RunAsync, PauseAfter: false),
            new("Reports & export", reportScreens.RunAsync, PauseAfter: false),
            new("User administration", userScreens.RunAsync, UserRole.Admin, PauseAfter: false),
            new("Change my password", userScreens.ChangeOwnPasswordAsync),
            new("Exit program", () => { _exitRequested = true; auth.Logout(); return Task.CompletedTask; }, PauseAfter: false),
        };

        return menu.RunAsync("Main menu", items, "Log out", () =>
        {
            AnsiConsole.MarkupLine($"[grey]Welcome, [bold]{Markup.Escape(session.CurrentUser!.Username)}[/]! Use the arrow keys and Enter to navigate.[/]\n");
        });
    }
}
