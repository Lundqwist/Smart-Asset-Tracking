// ============================================================================
// AssetView.cs
// ----------------------------------------------------------------------------
// Draws assets on the screen. Several screens (show all, search, filter) need
// the same thing, so it lives in one reusable class:
//
//   * ShowListAsync  - a coloured table, page by page (Level 4: pagination),
//                      with buttons for next/previous page, details and export
//   * BuildTable     - the table itself, grouped by category (Level 2)
//   * ShowDetails    - a panel with every field of one asset
// ============================================================================

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
        // Work out how many pages we need. Math.Ceiling rounds UP: 20 assets / 12 per page = 1.67 -> 2 pages.
        var pageSize = Math.Max(5, settings.PageSize);
        var pageCount = Math.Max(1, (int)Math.Ceiling(assets.Count / (double)pageSize));
        var page = 0; // pages are counted from 0 in code, but shown as "Page 1" to the user

        // "while (true)" loops forever. We leave it with "return" when the user picks Back.
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

            // Skip the rows of earlier pages, then Take one page of rows (LINQ pagination).
            var pageItems = assets.Skip(page * pageSize).Take(pageSize).ToList();
            AnsiConsole.Write(BuildTable(pageItems, groupByCategory));
            AnsiConsole.MarkupLine($"[grey]Page {page + 1} of {pageCount} · {assets.Count} asset(s) · Total value {Markup.Escape(TotalInReportingCurrency(assets))}[/]");
            ConsoleUi.StatusLegend(lifecycleOptions);
            AnsiConsole.WriteLine();

            // Only offer the actions that make sense right now.
            var choices = new List<string>();
            if (page < pageCount - 1) choices.Add("Next page ▶");
            if (page > 0) choices.Add("◀ Previous page");
            choices.Add("View asset details");
            if (session.HasAtLeast(UserRole.Manager)) choices.Add("Export this list (TXT / CSV / JSON)");
            choices.Add("↩ Back");

            // "c => c" means each choice is shown exactly as it is written.
            var choice = ConsoleInput.Select("Choose an action", choices, c => c);

            // A classic "switch statement": run the block that matches the choice.
            // "break" ends the case. "return" leaves the whole method.
            switch (choice)
            {
                case "Next page ▶": page++; break;
                case "◀ Previous page": page--; break;
                case "View asset details":
                    // {a.Id,-4} pads to 4 characters, left-aligned, so the columns line up.
                    var selected = ConsoleInput.Select("Select an asset", assets, a => $"#{a.Id,-4} {a.TypeName,-16} {a.DisplayName}", 15);
                    ShowDetails(selected);
                    ConsoleUi.Pause();
                    break;
                case "↩ Back": return;
                default:
                    // The only other choice is "Export".
                    await ExportInteractiveAsync(exporter.ToRows(assets), title);
                    break;
            }
        }
    }

    // Builds the coloured table. Assets are already sorted by category, so we add
    // a heading row each time the category changes ("Computers", "Mobile Devices"...).
    public Table BuildTable(IReadOnlyList<Asset> assets, bool groupByCategory = true)
    {
        // Each AddColumn adds a column header. "c => c.RightAligned()" right-aligns numbers.
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

        AssetCategory? currentCategory = null; // the category of the previous row
        foreach (var asset in assets)
        {
            // A new category starts: add an empty row as a gap, then a heading row.
            if (groupByCategory && asset.Category != currentCategory)
            {
                if (currentCategory is not null)
                    table.AddEmptyRow();
                currentCategory = asset.Category;
                table.AddRow($"", $"[bold aqua underline]{asset.Category.ToDisplayName()}[/]", "", "", "", "", "", "", "", "", "");
            }

            // The whole row gets the status colour (yellow, red, grey or white).
            var status = lifecycle.GetStatus(asset);
            var color = ConsoleUi.StatusColor(status);

            // A local function that wraps any value in the row's colour tag.
            string Cell(string value) => $"[{color}]{Markup.Escape(value)}[/]";

            table.AddRow(
                Cell(asset.Id.ToString()),
                Cell(asset.TypeName),
                Cell(asset.Brand),
                Cell(asset.ModelName),
                Cell(asset.Office.Name),
                Cell(asset.PurchaseDate.ToString("yyyy-MM-dd")),
                Cell(currency.Format(asset.PurchasePriceUsd, "USD")),
                Cell(currency.FormatLocalPrice(asset)),          // price in the office's own currency
                Cell(lifecycle.DescribeRemaining(asset)),        // e.g. "1y 4m"
                ConsoleUi.StatusBadge(status),
                asset.Employee is null ? "[grey]—[/]" : Cell(asset.Employee.FullName));
        }

        return table;
    }

    // Shows every detail of one asset in a box with two columns: label | value.
    public void ShowDetails(Asset asset)
    {
        var status = lifecycle.GetStatus(asset);
        var grid = new Grid().AddColumn(new GridColumn().PadRight(3)).AddColumn();

        // Two small local helper functions to keep the lines below short.
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

        // A Panel draws a box around the grid. Expand() makes it use the full width.
        AnsiConsole.Write(new Panel(grid)
            .Header($"[bold] Asset #{asset.Id} [/]")
            .BorderColor(Color.Aqua)
            .RoundedBorder()
            .Expand());
    }

    // The next maintenance date: red if overdue, yellow if soon, normal otherwise.
    public string NextMaintenanceMarkup(Asset asset)
    {
        if (asset.NextMaintenanceDate is null)
            return "[grey]not scheduled[/]";
        var date = asset.NextMaintenanceDate.Value;
        var days = (date - lifecycle.Today).Days;
        return days < 0 ? $"[yellow]{date:yyyy-MM-dd} (overdue {-days}d)[/]"
            : days <= lifecycleOptions.MaintenanceDueSoonDays ? $"[red]{date:yyyy-MM-dd} (in {days}d)[/]"
            : $"{date:yyyy-MM-dd} [grey](in {days}d)[/]";
    }

    // Green while the warranty is valid, grey once it has run out.
    private string WarrantyMarkup(Asset asset)
    {
        var expired = asset.WarrantyExpirationDate < lifecycle.Today;
        return expired
            ? $"[grey]{asset.WarrantyExpirationDate:yyyy-MM-dd} (expired)[/]"
            : $"[green]{asset.WarrantyExpirationDate:yyyy-MM-dd} (active)[/]";
    }

    // The total value of a list, converted to the reporting currency (SEK by default).
    private string TotalInReportingCurrency(IEnumerable<Asset> assets)
    {
        var code = currency.ReportingCurrency;
        return currency.Format(currency.ConvertFromUsd(assets.Sum(a => a.PurchasePriceUsd), code), code);
    }

    /// <summary>Asks for a format and writes the rows to the export folder.</summary>
    public async Task ExportInteractiveAsync<T>(IReadOnlyList<T> rows, string reportName)
    {
        // Enum.GetValues<ExportFormat>() gives all enum values: Txt, Csv, Json.
        var format = ConsoleInput.Select("Export format", Enum.GetValues<ExportFormat>(), f => f.ToString().ToUpperInvariant());
        var path = await exporter.ExportAsync(rows, format, reportName);
        ConsoleUi.Success($"Exported {rows.Count} row(s) to:");
        // TextPath prints a file path, highlighting the file name.
        AnsiConsole.Write(new TextPath(path).LeafColor(Color.Aqua));
        AnsiConsole.WriteLine();
        ConsoleUi.Pause();
    }
}
