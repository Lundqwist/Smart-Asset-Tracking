// ============================================================================
// OfficeScreens.cs
// ----------------------------------------------------------------------------
// The "Offices" menu (Level 3: global office management).
//   * Offices overview       - every office with currency, rate, asset count and value
//   * Show assets in office  - like the "STOCKHOLM OFFICE" example in the PDF,
//                              with prices in local currency and a total value
//   * Add / Edit / Delete    - Admin only
// ============================================================================

using SmartAssetTracking.Helpers;
using SmartAssetTracking.Models;
using SmartAssetTracking.Repositories;
using SmartAssetTracking.Services;
using Spectre.Console;

namespace SmartAssetTracking.UI.Screens;

/// <summary>Global office management: per-office asset views and office CRUD (Admin).</summary>
public class OfficeScreens(
    IOfficeService offices,
    IAssetService assets,
    IReportService reports,
    ICurrencyService currency,
    ILifecycleService lifecycle,
    AssetScreens assetScreens,
    MenuRunner menu)
{
    public Task RunAsync() => menu.RunAsync("Office management",
    [
        new MenuItem("Offices overview", OverviewAsync),
        new MenuItem("Show assets in an office", OfficeAssetsAsync),
        new MenuItem("Add office", AddAsync, UserRole.Admin),
        new MenuItem("Edit office", EditAsync, UserRole.Admin),
        new MenuItem("Delete office", DeleteAsync, UserRole.Admin),
    ]);

    // A table with one row per office.
    private async Task OverviewAsync()
    {
        var summaries = await reports.GetOfficeSummariesAsync();
        var table = new Table().Border(TableBorder.Rounded).BorderColor(Color.Grey)
            .AddColumn("[bold]ID[/]").AddColumn("[bold]Office[/]").AddColumn("[bold]Country[/]").AddColumn("[bold]Currency[/]")
            .AddColumn("[bold]Rate (1 USD)[/]", c => c.RightAligned())
            .AddColumn("[bold]Assets[/]", c => c.RightAligned())
            .AddColumn("[bold]Expiring[/]", c => c.RightAligned())
            .AddColumn("[bold]Total value (local)[/]", c => c.RightAligned())
            .AddColumn("[bold]Total value (USD)[/]", c => c.RightAligned());

        foreach (var s in summaries)
        {
            var info = currency.Get(s.Currency);
            table.AddRow(
                s.OfficeId.ToString(), Markup.Escape(s.Office), Markup.Escape(s.Country), $"{info.Code} ({Markup.Escape(info.Symbol)})",
                info.RateFromUsd.ToString("0.####"),   // "0.####" = up to 4 decimals, without trailing zeros
                s.AssetCount.ToString(),
                s.ExpiringSoon > 0 ? $"[red]{s.ExpiringSoon}[/]" : "0",
                Markup.Escape(currency.Format(s.TotalValueLocal, s.Currency)),
                Markup.Escape(currency.Format(s.TotalValueUsd, "USD")));
        }

        AnsiConsole.Write(table);
    }

    // The assets of one office, with prices in that office's currency and a total at the bottom.
    private async Task OfficeAssetsAsync()
    {
        var office = await assetScreens.SelectOfficeAsync("Select office");
        var list = await assets.SearchAsync(new AssetQuery { OfficeId = office.Id });

        var table = new Table().Border(TableBorder.Rounded).BorderColor(Color.Grey)
            .Title($"[bold aqua]{Markup.Escape(office.Name.ToUpperInvariant())}[/] [grey]· {Markup.Escape(office.Country)} · {office.CurrencyCode}[/]")
            .AddColumn("[bold]ID[/]").AddColumn("[bold]Asset[/]").AddColumn("[bold]Brand[/]").AddColumn("[bold]Model[/]")
            .AddColumn("[bold]Price[/]", c => c.RightAligned())
            .AddColumn("[bold]Status[/]", c => c.Centered());

        foreach (var a in list)
        {
            // Each row is coloured by the asset's lifecycle status.
            var status = lifecycle.GetStatus(a);
            var color = ConsoleUi.StatusColor(status);
            table.AddRow($"[{color}]{a.Id}[/]", $"[{color}]{Markup.Escape(a.TypeName)}[/]", $"[{color}]{Markup.Escape(a.Brand)}[/]",
                $"[{color}]{Markup.Escape(a.ModelName)}[/]", $"[{color}]{Markup.Escape(currency.FormatLocalPrice(a))}[/]",
                ConsoleUi.StatusBadge(status));
        }

        // Sum all USD prices, then convert the total once to the office currency.
        var total = currency.ConvertFromUsd(list.Sum(a => a.PurchasePriceUsd), office.CurrencyCode);
        table.Caption($"[bold]Total office value: {Markup.Escape(currency.Format(total, office.CurrencyCode))}[/] [grey]({list.Count} assets)[/]");
        AnsiConsole.Write(table);
    }

    private async Task AddAsync()
    {
        var name = ConsoleInput.Text("Office name", maxLength: 100);
        var country = ConsoleInput.Text("Country", maxLength: 60);
        var code = SelectCurrency(null);

        var office = await offices.CreateAsync(name, country, code);
        ConsoleUi.Success($"Office '{office.Name}' was created with ID {office.Id}.");
    }

    private async Task EditAsync()
    {
        var office = await assetScreens.SelectOfficeAsync("Office to edit");
        // The current values are shown as defaults. Press Enter to keep them.
        var name = ConsoleInput.Text("Office name", office.Name, 100);
        var country = ConsoleInput.Text("Country", office.Country, 60);
        var code = SelectCurrency(office.CurrencyCode);

        await offices.UpdateAsync(office.Id, name, country, code);
        ConsoleUi.Success($"Office #{office.Id} was updated.");
    }

    private async Task DeleteAsync()
    {
        var office = await assetScreens.SelectOfficeAsync("Office to delete");
        if (!ConsoleInput.Confirm($"Delete office '{office.Name}'?"))
            return;
        // The service refuses if the office still has assets (MenuRunner shows that message).
        await offices.DeleteAsync(office.Id);
        ConsoleUi.Success($"Office '{office.Name}' was deleted.");
    }

    // Lets the user pick one of the currencies configured in appsettings.json.
    private string SelectCurrency(string? current)
    {
        var list = currency.Currencies.OrderByDescending(c => c.Code == current).ThenBy(c => c.Code).ToList();
        return ConsoleInput.Select("Currency", list,
            c => $"{c.Code} ({c.Symbol}) · 1 USD = {c.RateFromUsd:0.####} {c.Code}{(c.Code == current ? "  (current)" : "")}").Code;
    }
}
