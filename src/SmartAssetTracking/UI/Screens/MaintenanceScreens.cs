using SmartAssetTracking.Helpers;
using SmartAssetTracking.Models;
using SmartAssetTracking.Services;
using Spectre.Console;

namespace SmartAssetTracking.UI.Screens;

/// <summary>Maintenance tracking: record maintenance and view the schedule.</summary>
public class MaintenanceScreens(
    IAssetService assets,
    ILifecycleService lifecycle,
    AssetScreens assetScreens,
    AssetView view,
    MenuRunner menu)
{
    public Task RunAsync() => menu.RunAsync("Maintenance",
    [
        new MenuItem("Maintenance schedule", ScheduleAsync),
        new MenuItem("Record maintenance", RecordAsync, UserRole.Manager),
    ]);

    private async Task ScheduleAsync()
    {
        var scheduled = await assets.GetMaintenanceScheduleAsync();
        if (scheduled.Count == 0)
        {
            ConsoleUi.Info("No maintenance is scheduled.");
            return;
        }

        var table = new Table().Border(TableBorder.Rounded).BorderColor(Color.Grey)
            .AddColumn("[bold]ID[/]").AddColumn("[bold]Asset[/]").AddColumn("[bold]Office[/]")
            .AddColumn("[bold]Last maintenance[/]").AddColumn("[bold]Next maintenance[/]").AddColumn("[bold]Notes[/]");

        foreach (var a in scheduled)
            table.AddRow(a.Id.ToString(), Markup.Escape(a.DisplayName), Markup.Escape(a.Office.Name),
                a.LastMaintenanceDate?.ToString("yyyy-MM-dd") ?? "[grey]never[/]",
                view.NextMaintenanceMarkup(a),
                Markup.Escape(a.MaintenanceNotes ?? ""));

        AnsiConsole.Write(table);
    }

    private async Task RecordAsync()
    {
        var asset = await assetScreens.AskForAssetAsync("Asset ID");
        view.ShowDetails(asset);
        AnsiConsole.WriteLine();

        var today = lifecycle.Today;
        var performed = ConsoleInput.Date("Maintenance performed on", today, asset.PurchaseDate, today);
        var next = ConsoleInput.OptionalDate("Next maintenance date", performed.AddMonths(6), performed.AddDays(1));
        var notes = ConsoleInput.OptionalText("Maintenance notes", asset.MaintenanceNotes, 500);

        await assets.RecordMaintenanceAsync(asset.Id, performed, next, notes);
        ConsoleUi.Success($"Maintenance recorded for {asset.DisplayName}.");
    }
}
