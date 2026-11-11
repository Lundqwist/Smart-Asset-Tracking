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
if (!AnsiConsole.Profile.Capabilities.Interactive || Console.IsInputRedirected || Console.IsOutputRedirected)
{
    Console.Error.WriteLine("Smart Asset Tracking is an interactive console application. " +
                            "Please run it in a terminal window (e.g. 'dotnet run --project src/SmartAssetTracking').");
    return 2;
}

// Unicode output so currency symbols such as €, ₺ and kr display correctly.
Console.OutputEncoding = Encoding.UTF8;
Console.InputEncoding = Encoding.UTF8;
CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
CultureInfo.DefaultThreadCurrentUICulture = CultureInfo.InvariantCulture;
Console.Title = ConsoleUi.AppTitle;

var configuration = new ConfigurationBuilder()
    .SetBasePath(AppContext.BaseDirectory)
    .AddJsonFile("appsettings.json", optional: false, reloadOnChange: false)
    .AddJsonFile("appsettings.Local.json", optional: true) // personal overrides, not committed
    .AddEnvironmentVariables(prefix: "SMARTASSET_")
    .Build();

var appSettings = configuration.GetSection("App").Get<AppSettings>() ?? new AppSettings();
var logFolder = Path.IsPathRooted(appSettings.LogFolder)
    ? appSettings.LogFolder
    : Path.Combine(AppContext.BaseDirectory, appSettings.LogFolder);

// Serilog writes to a rolling log file so the console UI stays clean.
Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .MinimumLevel.Override("Microsoft", Serilog.Events.LogEventLevel.Warning)
    .WriteTo.File(Path.Combine(logFolder, "smartasset-.log"),
        rollingInterval: RollingInterval.Day,
        retainedFileCountLimit: 14,
        outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss} [{Level:u3}] {SourceContext}: {Message:lj}{NewLine}{Exception}")
    .CreateLogger();

try
{
    var services = new ServiceCollection()
        .AddLogging(builder => builder.ClearProviders().AddSerilog(dispose: false))
        .AddSmartAssetTracking(configuration);

    await using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true });
    Log.Information("Smart Asset Tracking started");
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
