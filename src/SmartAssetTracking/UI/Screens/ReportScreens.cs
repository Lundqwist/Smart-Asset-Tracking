// ============================================================================
// ReportScreens.cs
// ----------------------------------------------------------------------------
// Draws the dashboard (Level 5) and the "Reports & export" menu (Levels 3-4):
//   * Office report, assets near expiration, most expensive, per employee
//   * Export any report to TXT / CSV / JSON
//   * Exchange rates (and updating them from the internet, Admin only)
//
// The numbers come from ReportService. This class only draws them, using
// Spectre.Console tables, panels and bar charts.
// ============================================================================

using SmartAssetTracking.Helpers;
using SmartAssetTracking.Models;
using SmartAssetTracking.Reports;
using SmartAssetTracking.Services;
using Spectre.Console;

namespace SmartAssetTracking.UI.Screens;

/// <summary>Dashboard, reports, exports and exchange rates.</summary>
public class ReportScreens(
    IReportService reports,
    IAssetService assets,
    ICurrencyService currency,
    ILifecycleService lifecycle,
    IExportService exporter,
    AssetView view,
    MenuRunner menu)
{
    public Task RunAsync() => menu.RunAsync("Reports",
    [
        new MenuItem("Office report (asset count & value per office)", OfficeReportAsync),
        new MenuItem("Assets near expiration", NearExpirationAsync),
        new MenuItem("Most expensive assets", MostExpensiveAsync),
        new MenuItem("Assets per employee", AssetsPerEmployeeAsync),
        new MenuItem("Export a report (TXT / CSV / JSON)", ExportAsync, UserRole.Manager, PauseAfter: false),
        new MenuItem("Exchange rates", ExchangeRatesAsync),
        new MenuItem("Update exchange rates from online API", RefreshRatesAsync, UserRole.Admin),
    ]);

    // ------------------------------------------------------------------ Dashboard

    public async Task DashboardAsync()
    {
        // Show a spinner while the statistics are calculated. "_ =>" ignores the status context parameter.
        var stats = await AnsiConsole.Status().Spinner(Spinner.Known.Dots)
            .StartAsync("Calculating statistics...", _ => reports.GetDashboardAsync());

        // Local helper: format an amount in the reporting currency (e.g. "227 210,00 kr").
        string Value(decimal amount) => Markup.Escape(currency.Format(amount, stats.ReportingCurrency));

        // Left box: the key figures (label on the left, value on the right).
        var keyFigures = new Grid().AddColumn(new GridColumn().PadRight(2)).AddColumn(new GridColumn().RightAligned());
        keyFigures.AddRow("[grey]Total assets[/]", $"[bold]{stats.TotalAssets}[/]");
        keyFigures.AddRow("[grey]Total employees[/]", $"[bold]{stats.TotalEmployees}[/]");
        keyFigures.AddRow("[grey]Expiring soon[/]", $"[bold red]{stats.ExpiringSoon}[/]");
        keyFigures.AddRow("[grey]Expired[/]", $"[bold grey]{stats.Expired}[/]");
        keyFigures.AddRow("[grey]Unassigned assets[/]", $"[bold]{stats.UnassignedAssets}[/]");
        keyFigures.AddRow("[grey]Maintenance due (30d)[/]", stats.MaintenanceDue > 0 ? $"[bold yellow]{stats.MaintenanceDue}[/]" : "[bold]0[/]");
        keyFigures.AddEmptyRow();
        keyFigures.AddRow("[grey]Total company value[/]", $"[bold green]{Value(stats.TotalCompanyValue)}[/]");
        keyFigures.AddRow("[grey]Top asset type[/]", $"[bold]{Markup.Escape(stats.TopAssetType ?? "—")}[/] [grey]({stats.TopAssetTypeCount})[/]");
        keyFigures.AddRow("[grey]Most valuable office[/]", $"[bold]{Markup.Escape(stats.MostValuableOffice ?? "—")}[/]");

        // Right box: two bar charts.
        var officeChart = new BarChart().Width(50).Label("[bold]Assets per office[/]").LeftAlignLabel();
        var colors = new[] { Color.Aqua, Color.Yellow, Color.Green, Color.Fuchsia, Color.Orange1, Color.Blue };

        // .Select((o, i) => (o, i)) pairs each item with its position (0, 1, 2...),
        // so each bar can get a different colour. "%" (modulo) restarts the colour list if it runs out.
        foreach (var (office, i) in stats.Offices.Select((o, i) => (o, i)))
            officeChart.AddItem(office.Office, office.AssetCount, colors[i % colors.Length]);

        var typeChart = new BarChart().Width(50).Label("[bold]Assets per type[/]").LeftAlignLabel();
        foreach (var (type, i) in stats.AssetsPerType.Select((t, i) => (t, i)))
            typeChart.AddItem(type.Type, type.Count, colors[(i + 2) % colors.Length]);

        // Columns places the two panels side by side.
        AnsiConsole.Write(new Columns(
            new Panel(keyFigures).Header("[bold] DASHBOARD [/]").BorderColor(Color.Aqua).RoundedBorder(),
            new Panel(new Rows(officeChart, new Text(""), typeChart)).BorderColor(Color.Grey).RoundedBorder()));

        // Below: a table of assets per employee.
        var employees = new Table().Border(TableBorder.Rounded).BorderColor(Color.Grey).Title("[bold]Assets per employee[/]")
            .AddColumn("[bold]Employee[/]").AddColumn("[bold]Department[/]").AddColumn("[bold]Assets[/]", c => c.RightAligned());
        foreach (var e in stats.AssetsPerEmployee)
            employees.AddRow(Markup.Escape(e.Employee), Markup.Escape(e.Department), e.AssetCount.ToString());
        AnsiConsole.Write(employees);

        AnsiConsole.MarkupLine($"[grey]Values shown in {stats.ReportingCurrency}. Exchange rates: " +
                               (currency.LastLiveUpdate is null ? "fixed (appsettings.json)" : $"live, updated {currency.LastLiveUpdate:yyyy-MM-dd HH:mm}") + "[/]");
    }

    // ------------------------------------------------------------------ Reports

    // Level 3: total asset value and asset count per office.
    private async Task OfficeReportAsync()
    {
        var summaries = await reports.GetOfficeSummariesAsync();

        var table = new Table().Border(TableBorder.Rounded).BorderColor(Color.Grey).Title("[bold]Office asset counts & values[/]")
            .AddColumn("[bold]Office[/]").AddColumn("[bold]Assets[/]", c => c.RightAligned())
            .AddColumn("[bold]Expiring soon[/]", c => c.RightAligned()).AddColumn("[bold]Expired[/]", c => c.RightAligned())
            .AddColumn("[bold]Total value (local)[/]", c => c.RightAligned()).AddColumn("[bold]Total value (USD)[/]", c => c.RightAligned());

        foreach (var s in summaries)
            table.AddRow(Markup.Escape(s.Office), s.AssetCount.ToString(), s.ExpiringSoon.ToString(), s.Expired.ToString(),
                Markup.Escape(currency.Format(s.TotalValueLocal, s.Currency)), Markup.Escape(currency.Format(s.TotalValueUsd, "USD")));

        // A totals row at the bottom. Local currencies differ, so only the USD total makes sense.
        table.AddEmptyRow();
        table.AddRow("[bold]Total[/]", $"[bold]{summaries.Sum(s => s.AssetCount)}[/]", $"[bold]{summaries.Sum(s => s.ExpiringSoon)}[/]",
            $"[bold]{summaries.Sum(s => s.Expired)}[/]", "",
            $"[bold]{Markup.Escape(currency.Format(summaries.Sum(s => s.TotalValueUsd), "USD"))}[/]");
        AnsiConsole.Write(table);

        // BarChart values must be "double", so we convert the decimal with (double).
        var chart = new BarChart().Width(70).Label("[bold]Total value per office (USD)[/]").LeftAlignLabel();
        foreach (var s in summaries)
            chart.AddItem(s.Office, (double)s.TotalValueUsd, Color.Aqua);
        AnsiConsole.Write(chart);
    }

    // Level 3: assets close to expiration (YELLOW or RED), most urgent first.
    private async Task NearExpirationAsync()
    {
        var list = await assets.GetExpiringSoonAsync();
        if (list.Count == 0)
        {
            ConsoleUi.Success("No assets are close to their end of life.");
            return;
        }

        var table = new Table().Border(TableBorder.Rounded).BorderColor(Color.Grey).Title("[bold]Assets near expiration[/]")
            .AddColumn("[bold]ID[/]").AddColumn("[bold]Asset[/]").AddColumn("[bold]Office[/]").AddColumn("[bold]Purchased[/]")
            .AddColumn("[bold]End of life[/]").AddColumn("[bold]Remaining[/]", c => c.RightAligned()).AddColumn("[bold]Status[/]", c => c.Centered());

        foreach (var a in list)
            table.AddRow(a.Id.ToString(), Markup.Escape(a.DisplayName), Markup.Escape(a.Office.Name), a.PurchaseDate.ToString("yyyy-MM-dd"),
                lifecycle.GetEndOfLife(a).ToString("yyyy-MM-dd"), Markup.Escape(lifecycle.DescribeRemaining(a)),
                ConsoleUi.StatusBadge(lifecycle.GetStatus(a)));
        AnsiConsole.Write(table);
    }

    // Level 3: most expensive assets. The user chooses how many to show.
    private async Task MostExpensiveAsync()
    {
        var top = ConsoleInput.Integer("How many assets", 1, 100, 10);
        var list = await reports.GetMostExpensiveAsync(top);

        var table = new Table().Border(TableBorder.Rounded).BorderColor(Color.Grey).Title($"[bold]Top {list.Count} most expensive assets[/]")
            .AddColumn("[bold]Rank[/]", c => c.RightAligned()).AddColumn("[bold]ID[/]").AddColumn("[bold]Asset[/]").AddColumn("[bold]Type[/]")
            .AddColumn("[bold]Office[/]").AddColumn("[bold]Price (USD)[/]", c => c.RightAligned()).AddColumn("[bold]Local price[/]", c => c.RightAligned());

        var rank = 1;
        foreach (var a in list)
            table.AddRow((rank++).ToString(), a.Id.ToString(), Markup.Escape(a.DisplayName), Markup.Escape(a.TypeName), Markup.Escape(a.Office.Name),
                Markup.Escape(currency.Format(a.PurchasePriceUsd, "USD")), Markup.Escape(currency.FormatLocalPrice(a)));
        AnsiConsole.Write(table);
    }

    private async Task AssetsPerEmployeeAsync()
    {
        var list = await reports.GetAssetsPerEmployeeAsync();
        var chart = new BarChart().Width(70).Label("[bold]Assigned assets per employee[/]").LeftAlignLabel();
        foreach (var e in list)
            chart.AddItem($"{e.Employee} ({e.Department})", e.AssetCount, Color.Aqua);
        AnsiConsole.Write(chart);
    }

    // ------------------------------------------------------------------ Export

    // Level 4: pick a report, then a format (TXT / CSV / JSON). The file is saved in the "exports" folder.
    private async Task ExportAsync()
    {
        var reportTypes = new[]
        {
            "All assets",
            "Office summary",
            "Assets near expiration",
            "Expired assets",
            "Most expensive assets (top 10)",
            "Assets per employee",
        };
        var choice = ConsoleInput.Select("Which report do you want to export?", reportTypes, r => r);

        // Asset lists are converted to flat rows first (exporter.ToRows). Summary records can be exported as they are.
        switch (choice)
        {
            case "All assets":
                await view.ExportInteractiveAsync(exporter.ToRows(await assets.GetAllSortedAsync()), "all-assets");
                break;
            case "Office summary":
                await view.ExportInteractiveAsync(await reports.GetOfficeSummariesAsync(), "office-summary");
                break;
            case "Assets near expiration":
                await view.ExportInteractiveAsync(exporter.ToRows(await assets.GetExpiringSoonAsync()), "near-expiration");
                break;
            case "Expired assets":
                await view.ExportInteractiveAsync(exporter.ToRows(await assets.GetExpiredAsync()), "expired-assets");
                break;
            case "Most expensive assets (top 10)":
                await view.ExportInteractiveAsync(exporter.ToRows(await reports.GetMostExpensiveAsync(10)), "most-expensive");
                break;
            default:
                await view.ExportInteractiveAsync(await reports.GetAssetsPerEmployeeAsync(), "assets-per-employee");
                break;
        }
    }

    // ------------------------------------------------------------------ Currency

    // Shows the exchange rate table. The method has nothing to "await", so it returns
    // Task.CompletedTask (an already-finished task) to match the menu's async signature.
    private Task ExchangeRatesAsync()
    {
        var table = new Table().Border(TableBorder.Rounded).BorderColor(Color.Grey).Title("[bold]Exchange rates (base USD)[/]")
            .AddColumn("[bold]Currency[/]").AddColumn("[bold]Symbol[/]").AddColumn("[bold]1 USD =[/]", c => c.RightAligned())
            .AddColumn("[bold]Example: 1 000 USD[/]", c => c.RightAligned());

        foreach (var c in currency.Currencies.OrderBy(c => c.Code))
            table.AddRow(c.Code, Markup.Escape(c.Symbol), c.RateFromUsd.ToString("0.####"),
                Markup.Escape(currency.Format(currency.ConvertFromUsd(1000m, c.Code), c.Code)));

        AnsiConsole.Write(table);
        ConsoleUi.Info(currency.LastLiveUpdate is null
            ? "Using fixed rates from appsettings.json."
            : $"Live rates downloaded {currency.LastLiveUpdate:yyyy-MM-dd HH:mm} (valid until the program is closed).");
        return Task.CompletedTask;
    }

    // Admin only: download current rates from the internet, then show them.
    private async Task RefreshRatesAsync()
    {
        var updated = await AnsiConsole.Status().Spinner(Spinner.Known.Dots)
            .StartAsync("Downloading exchange rates...", _ => currency.RefreshRatesFromApiAsync());
        ConsoleUi.Success($"{updated} exchange rate(s) updated.");
        AnsiConsole.WriteLine();
        await ExchangeRatesAsync();
    }
}
