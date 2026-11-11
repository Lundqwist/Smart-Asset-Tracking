// ============================================================================
// Application.cs
// ----------------------------------------------------------------------------
// The "shell" of the program. Program.cs starts this class, and it controls
// the overall flow:
//
//   1. Connect to SQL Server, create or upgrade the database (migrations)
//      and add demo data.
//   2. Show the login screen.
//   3. Show the main menu until the user logs out or exits.
//   4. Logged out? Go back to step 2. Exit? Say goodbye and stop.
// ============================================================================

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
// The constructor asks for every screen. The dependency injection container
// creates them all and passes them in (see ServiceRegistration.cs).
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
    // Set to true when the user chooses "Exit program" (rather than just "Log out").
    private bool _exitRequested;

    // Returns an "exit code": 0 = everything OK, 1 = something went wrong.
    public async Task<int> RunAsync()
    {
        AnsiConsole.Clear();
        ConsoleUi.Banner();

        // Without a database we can't do anything, so stop if it fails.
        if (!await InitializeDatabaseAsync())
            return 1;

        // The login -> main menu -> logout cycle repeats until the user exits.
        while (!_exitRequested)
        {
            if (!await LoginAsync())
                return 0; // the user gave up logging in

            await MainMenuAsync();  // returns when the user logs out or exits
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
            // Status() shows a spinning animation with a message while the slow work runs.
            // "async ctx => { ... }" is an async lambda. ctx lets us change the message.
            await AnsiConsole.Status().Spinner(Spinner.Known.Dots).StartAsync("Connecting to SQL Server...", async ctx =>
            {
                await using var db = await contextFactory.CreateDbContextAsync();
                ctx.Status("Applying database migrations...");

                // MigrateAsync creates the database if it doesn't exist and runs any migrations
                // that haven't been applied yet. You never need to create tables by hand.
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
        // "when (...)" is an exception filter: only catch these specific database-related errors.
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

            // A "for" loop: start at 1, keep going while attempt <= max, add 1 each time.
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
        // The main menu. Each item links a label to a method in one of the screens.
        // "reportScreens.DashboardAsync" (without parentheses) passes the METHOD itself, without calling it yet.
        // UserRole.Manager / UserRole.Admin hide the item for users with a lower role.
        // PauseAfter: false for sub-menus, because they have their own navigation.
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
            // A lambda as the action: set the exit flag and log out, which ends the menu loop.
            new("Exit program", () => { _exitRequested = true; auth.Logout(); return Task.CompletedTask; }, PauseAfter: false),
        };

        // The back item of the main menu is "Log out". The last lambda prints a welcome line under the title.
        return menu.RunAsync("Main menu", items, "Log out", () =>
        {
            AnsiConsole.MarkupLine($"[grey]Welcome, [bold]{Markup.Escape(session.CurrentUser!.Username)}[/]! Use the arrow keys and Enter to navigate.[/]\n");
        });
    }
}
