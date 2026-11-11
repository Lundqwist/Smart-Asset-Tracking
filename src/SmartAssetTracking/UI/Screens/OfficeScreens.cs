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
                info.RateFromUsd.ToString("0.####"),
                s.AssetCount.ToString(),
                s.ExpiringSoon > 0 ? $"[yellow]{s.ExpiringSoon}[/]" : "0",
                Markup.Escape(currency.Format(s.TotalValueLocal, s.Currency)),
                Markup.Escape(currency.Format(s.TotalValueUsd, "USD")));
        }

        AnsiConsole.Write(table);
    }

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
            var status = lifecycle.GetStatus(a);
            var color = ConsoleUi.StatusColor(status);
            table.AddRow($"[{color}]{a.Id}[/]", $"[{color}]{Markup.Escape(a.TypeName)}[/]", $"[{color}]{Markup.Escape(a.Brand)}[/]",
                $"[{color}]{Markup.Escape(a.ModelName)}[/]", $"[{color}]{Markup.Escape(currency.FormatLocalPrice(a))}[/]",
                ConsoleUi.StatusBadge(status));
        }

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
        await offices.DeleteAsync(office.Id);
        ConsoleUi.Success($"Office '{office.Name}' was deleted.");
    }

    private string SelectCurrency(string? current)
    {
        var list = currency.Currencies.OrderByDescending(c => c.Code == current).ThenBy(c => c.Code).ToList();
        return ConsoleInput.Select("Currency", list,
            c => $"{c.Code} ({c.Symbol}) · 1 USD = {c.RateFromUsd:0.####} {c.Code}{(c.Code == current ? "  (current)" : "")}").Code;
    }
}
