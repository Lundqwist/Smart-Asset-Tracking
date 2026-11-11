using SmartAssetTracking.Helpers;
using SmartAssetTracking.Models;
using SmartAssetTracking.Reports;
using SmartAssetTracking.Services;
using Spectre.Console;

namespace SmartAssetTracking.UI;

/// <summary>Renders assets as coloured tables with pagination, details panels and export.</summary>
public class AssetView(
    ILifecycleService lifecycle,
    ICurrencyService currency,
    IExportService exporter,
    UserSession session,
    AppSettings settings,
    LifecycleOptions lifecycleOptions)
{
    /// <summary>Shows a list of assets page by page. Offers export for Managers and Admins.</summary>
    public async Task ShowListAsync(IReadOnlyList<Asset> assets, string title, string? subtitle = null, bool groupByCategory = true)
    {
        var pageSize = Math.Max(5, settings.PageSize);
        var pageCount = Math.Max(1, (int)Math.Ceiling(assets.Count / (double)pageSize));
        var page = 0;

        while (true)
        {
            ConsoleUi.Header(title, session);
            if (subtitle is not null)
                AnsiConsole.MarkupLine($"[bold]{Markup.Escape(subtitle)}[/]\n");

            if (assets.Count == 0)
            {
                ConsoleUi.Warning("No assets found.");
                ConsoleUi.Pause();
                return;
            }

            var pageItems = assets.Skip(page * pageSize).Take(pageSize).ToList();
            AnsiConsole.Write(BuildTable(pageItems, groupByCategory));
            AnsiConsole.MarkupLine($"[grey]Page {page + 1} of {pageCount} · {assets.Count} asset(s) · Total value {Markup.Escape(TotalInReportingCurrency(assets))}[/]");
            ConsoleUi.StatusLegend(lifecycleOptions);
            AnsiConsole.WriteLine();

            var choices = new List<string>();
            if (page < pageCount - 1) choices.Add("Next page ▶");
            if (page > 0) choices.Add("◀ Previous page");
            choices.Add("View asset details");
            if (session.HasAtLeast(UserRole.Manager)) choices.Add("Export this list (TXT / CSV / JSON)");
            choices.Add("↩ Back");

            var choice = ConsoleInput.Select("Choose an action", choices, c => c);
            switch (choice)
            {
                case "Next page ▶": page++; break;
                case "◀ Previous page": page--; break;
                case "View asset details":
                    var selected = ConsoleInput.Select("Select an asset", assets, a => $"#{a.Id,-4} {a.TypeName,-16} {a.DisplayName}", 15);
                    ShowDetails(selected);
                    ConsoleUi.Pause();
                    break;
                case "↩ Back": return;
                default:
                    await ExportInteractiveAsync(exporter.ToRows(assets), title);
                    break;
            }
        }
    }

    public Table BuildTable(IReadOnlyList<Asset> assets, bool groupByCategory = true)
    {
        var table = new Table()
            .Border(TableBorder.Rounded)
            .BorderColor(Color.Grey)
            .AddColumn("[bold]ID[/]", c => c.RightAligned())
            .AddColumn("[bold]Type[/]")
            .AddColumn("[bold]Brand[/]")
            .AddColumn("[bold]Model[/]")
            .AddColumn("[bold]Office[/]")
            .AddColumn("[bold]Purchased[/]")
            .AddColumn("[bold]Price (USD)[/]", c => c.RightAligned())
            .AddColumn("[bold]Local price[/]", c => c.RightAligned())
            .AddColumn("[bold]Remaining[/]", c => c.RightAligned())
            .AddColumn("[bold]Status[/]", c => c.Centered())
            .AddColumn("[bold]Assigned to[/]");

        AssetCategory? currentCategory = null;
        foreach (var asset in assets)
        {
            if (groupByCategory && asset.Category != currentCategory)
            {
                if (currentCategory is not null)
                    table.AddEmptyRow();
                currentCategory = asset.Category;
                table.AddRow($"", $"[bold aqua underline]{asset.Category.ToDisplayName()}[/]", "", "", "", "", "", "", "", "", "");
            }

            var status = lifecycle.GetStatus(asset);
            var color = ConsoleUi.StatusColor(status);
            string Cell(string value) => $"[{color}]{Markup.Escape(value)}[/]";

            table.AddRow(
                Cell(asset.Id.ToString()),
                Cell(asset.TypeName),
                Cell(asset.Brand),
                Cell(asset.ModelName),
                Cell(asset.Office.Name),
                Cell(asset.PurchaseDate.ToString("yyyy-MM-dd")),
                Cell(currency.Format(asset.PurchasePriceUsd, "USD")),
                Cell(currency.FormatLocalPrice(asset)),
                Cell(lifecycle.DescribeRemaining(asset)),
                ConsoleUi.StatusBadge(status),
                asset.Employee is null ? "[grey]—[/]" : Cell(asset.Employee.FullName));
        }

        return table;
    }

    public void ShowDetails(Asset asset)
    {
        var status = lifecycle.GetStatus(asset);
        var grid = new Grid().AddColumn(new GridColumn().PadRight(3)).AddColumn();

        void Row(string label, string valueMarkup) => grid.AddRow($"[grey]{label}[/]", valueMarkup);
        string E(string? s) => Markup.Escape(s ?? "");

        Row("ID", asset.Id.ToString());
        Row("Type", $"{E(asset.TypeName)} [grey]({asset.Category.ToDisplayName()})[/]");
        Row("Brand / Model", $"[bold]{E(asset.Brand)} {E(asset.ModelName)}[/]");
        Row("Serial number", E(asset.SerialNumber));
        Row("Office", E(asset.Office.ToString()));
        Row("Assigned to", asset.Employee is null ? "[grey]Not assigned[/]" : E(asset.Employee.ToString()));
        Row("Purchase date", asset.PurchaseDate.ToString("yyyy-MM-dd"));
        Row("Purchase price", E(currency.Format(asset.PurchasePriceUsd, "USD")));
        Row("Local price", $"{E(currency.FormatLocalPrice(asset))} [grey]({asset.Office.CurrencyCode})[/]");
        Row("Warranty expires", WarrantyMarkup(asset));
        Row("End of life", $"{lifecycle.GetEndOfLife(asset):yyyy-MM-dd} [grey]({E(lifecycle.DescribeRemaining(asset))})[/]");
        Row("Status", ConsoleUi.StatusBadge(status));
        Row("Last maintenance", asset.LastMaintenanceDate?.ToString("yyyy-MM-dd") ?? "[grey]never[/]");
        Row("Next maintenance", NextMaintenanceMarkup(asset));
        Row("Maintenance notes", asset.MaintenanceNotes is null ? "[grey]—[/]" : E(asset.MaintenanceNotes));

        AnsiConsole.Write(new Panel(grid)
            .Header($"[bold] Asset #{asset.Id} [/]")
            .BorderColor(Color.Aqua)
            .RoundedBorder()
            .Expand());
    }

    public string NextMaintenanceMarkup(Asset asset)
    {
        if (asset.NextMaintenanceDate is null)
            return "[grey]not scheduled[/]";
        var date = asset.NextMaintenanceDate.Value;
        var days = (date - lifecycle.Today).Days;
        return days < 0 ? $"[red]{date:yyyy-MM-dd} (overdue {-days}d)[/]"
            : days <= lifecycleOptions.MaintenanceDueSoonDays ? $"[yellow]{date:yyyy-MM-dd} (in {days}d)[/]"
            : $"{date:yyyy-MM-dd} [grey](in {days}d)[/]";
    }

    private string WarrantyMarkup(Asset asset)
    {
        var expired = asset.WarrantyExpirationDate < lifecycle.Today;
        return expired
            ? $"[grey]{asset.WarrantyExpirationDate:yyyy-MM-dd} (expired)[/]"
            : $"[green]{asset.WarrantyExpirationDate:yyyy-MM-dd} (active)[/]";
    }

    private string TotalInReportingCurrency(IEnumerable<Asset> assets)
    {
        var code = currency.ReportingCurrency;
        return currency.Format(currency.ConvertFromUsd(assets.Sum(a => a.PurchasePriceUsd), code), code);
    }

    /// <summary>Asks for a format and writes the rows to the export folder.</summary>
    public async Task ExportInteractiveAsync<T>(IReadOnlyList<T> rows, string reportName)
    {
        var format = ConsoleInput.Select("Export format", Enum.GetValues<ExportFormat>(), f => f.ToString().ToUpperInvariant());
        var path = await exporter.ExportAsync(rows, format, reportName);
        ConsoleUi.Success($"Exported {rows.Count} row(s) to:");
        AnsiConsole.Write(new TextPath(path).LeafColor(Color.Aqua));
        AnsiConsole.WriteLine();
        ConsoleUi.Pause();
    }
}
