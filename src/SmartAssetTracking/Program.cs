// ============================================================================
// Program.cs - THE STARTING POINT OF THE PROGRAM
// ----------------------------------------------------------------------------
// When you run the app, C# starts executing at the top of this file. It uses
// "top-level statements": there is no "class Program" or "static void Main".
// The compiler creates those for us behind the scenes.
//
// What happens here, step by step:
//   1. Check that we are running in a real terminal (the menus need one).
//   2. Set up text encoding so symbols like €, ₺ and kr display correctly.
//   3. Read the settings from appsettings.json.
//   4. Set up logging to a file (Serilog).
//   5. Register all classes for dependency injection.
//   6. Start the Application (database -> login -> main menu).
//
// The "return" values are "exit codes": 0 = success, anything else = a problem.
//
// HOW THE CODE IS ORGANISED (the layers):
//   Models/        - the data classes (Asset, Laptop, Office, Employee, User)
//   Data/          - the EF Core database setup and demo data
//   Repositories/  - read/write data in the database
//   Services/      - business rules, validation and permissions
//   Reports/       - exporting to TXT / CSV / JSON
//   Helpers/       - reusable tools (input prompts, password hashing, settings, DI)
//   UI/            - everything the user sees: menus, tables, screens
// ============================================================================

using System.Globalization;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Serilog;
using SmartAssetTracking.Helpers;
using SmartAssetTracking.UI;
using Spectre.Console;

// The menus need a real, interactive terminal (arrow keys, colours, clearing the screen).
// For example, VS Code's "Debug Console" can't read key presses, so we stop with a helpful message.
if (!AnsiConsole.Profile.Capabilities.Interactive || Console.IsInputRedirected || Console.IsOutputRedirected)
{
    Console.Error.WriteLine("Smart Asset Tracking is an interactive console application and needs a real terminal.");
    Console.Error.WriteLine("  - Terminal:  dotnet run --project src/SmartAssetTracking");
    Console.Error.WriteLine("  - VS Code:   use the 'Smart Asset Tracking' launch configuration (runs in the integrated terminal),");
    Console.Error.WriteLine("               not the Debug Console.");
    return 2;
}

// Unicode output so currency symbols such as €, ₺ and kr display correctly.
Console.OutputEncoding = Encoding.UTF8;
Console.InputEncoding = Encoding.UTF8;

// Use the same number/date rules on every computer. Money is formatted per office
// in CurrencyService, so the computer's own language settings don't matter.
CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
CultureInfo.DefaultThreadCurrentUICulture = CultureInfo.InvariantCulture;
Console.Title = ConsoleUi.AppTitle;

// ---- Configuration ----
// Settings are read from several sources. Later sources override earlier ones:
//   appsettings.json        - the default settings (in the repository)
//   appsettings.Local.json  - your own personal overrides (optional, not committed to git)
//   environment variables   - e.g. SMARTASSET_ConnectionStrings__DefaultConnection
var configuration = new ConfigurationBuilder()
    .SetBasePath(AppContext.BaseDirectory)  // the folder where the .exe is
    .AddJsonFile("appsettings.json", optional: false, reloadOnChange: false)
    .AddJsonFile("appsettings.Local.json", optional: true) // personal overrides, not committed
    .AddEnvironmentVariables(prefix: "SMARTASSET_")
    .Build();

var appSettings = configuration.GetSection("App").Get<AppSettings>() ?? new AppSettings();
var logFolder = Path.IsPathRooted(appSettings.LogFolder)
    ? appSettings.LogFolder
    : Path.Combine(AppContext.BaseDirectory, appSettings.LogFolder);

// ---- Logging (Level 4: "Add logging") ----
// Serilog writes to a rolling log file so the console UI stays clean.
// "Rolling" means a new file every day (smartasset-20261111.log). Files older than 14 days are deleted.
// Only warnings and errors from Microsoft's own libraries (like EF Core) are logged, to keep the file short.
Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .MinimumLevel.Override("Microsoft", Serilog.Events.LogEventLevel.Warning)
    .WriteTo.File(Path.Combine(logFolder, "smartasset-.log"),
        rollingInterval: RollingInterval.Day,
        retainedFileCountLimit: 14,
        outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss} [{Level:u3}] {SourceContext}: {Message:lj}{NewLine}{Exception}")
    .CreateLogger();

// try / catch / finally:
//   try     - run the program
//   catch   - if anything crashes that nobody else caught, log it and show it
//   finally - ALWAYS runs at the end (even after a crash) and makes sure the log file is saved
try
{
    // Register everything for dependency injection (see Helpers/ServiceRegistration.cs).
    var services = new ServiceCollection()
        .AddLogging(builder => builder.ClearProviders().AddSerilog(dispose: false))
        .AddSmartAssetTracking(configuration);

    // Build the "service provider", the object that creates classes for us.
    // ValidateOnBuild checks right away that every class can be created, so mistakes show up at start-up.
    await using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true });
    Log.Information("Smart Asset Tracking started");

    // Ask the provider for the Application (it builds all its dependencies too) and run it.
    return await provider.GetRequiredService<Application>().RunAsync();
}
catch (Exception ex)
{
    Log.Fatal(ex, "Application terminated unexpectedly");
    AnsiConsole.MarkupLine("[red bold]A fatal error occurred:[/]");
    AnsiConsole.WriteException(ex, ExceptionFormats.ShortenEverything);
    return 1;
}
finally
{
    Log.Information("Smart Asset Tracking stopped");
    await Log.CloseAndFlushAsync();
}
