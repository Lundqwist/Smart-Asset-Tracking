using SmartAssetTracking.Helpers;
using SmartAssetTracking.Models;
using SmartAssetTracking.Repositories;
using SmartAssetTracking.Services;
using Spectre.Console;

namespace SmartAssetTracking.UI.Screens;

/// <summary>Asset CRUD, search and filtering screens.</summary>
public class AssetScreens(
    IAssetService assets,
    IOfficeService offices,
    IEmployeeService employees,
    ILifecycleService lifecycle,
    ICurrencyService currency,
    AssetView view,
    MenuRunner menu,
    LifecycleOptions lifecycleOptions)
{
    public Task RunAsync() => menu.RunAsync("Asset management",
    [
        new MenuItem("Add asset", AddAsync, UserRole.Manager),
        new MenuItem("Show all assets", ShowAllAsync, PauseAfter: false),
        new MenuItem("Update asset", UpdateAsync, UserRole.Manager),
        new MenuItem("Delete asset", DeleteAsync, UserRole.Manager),
        new MenuItem("Search assets", SearchAsync, PauseAfter: false),
        new MenuItem("Filter assets", FilterAsync, PauseAfter: false),
        new MenuItem("View asset details", DetailsAsync),
    ]);

    public async Task ShowAllAsync()
    {
        var all = await assets.GetAllSortedAsync();
        await view.ShowListAsync(all, "Company assets", "Sorted by category, then purchase date");
    }

    private async Task DetailsAsync()
    {
        var asset = await AskForAssetAsync("Asset ID to view");
        view.ShowDetails(asset);
    }

    // ------------------------------------------------------------------ Create

    private async Task AddAsync()
    {
        AnsiConsole.MarkupLine("[grey]Prices are entered in USD and converted to the office's local currency automatically.[/]\n");

        var kind = ConsoleInput.Select("Asset type", Enum.GetValues<AssetKind>(), k => k.ToDisplayName());
        var input = await AskAssetFieldsAsync(kind, existing: null);

        AnsiConsole.WriteLine();
        AnsiConsole.Write(SummaryTable(input, await offices.GetByIdAsync(input.OfficeId)));
        if (!ConsoleInput.Confirm("Save this asset?", defaultValue: true))
        {
            ConsoleUi.Warning("Nothing was saved.");
            return;
        }

        var created = await assets.CreateAsync(input);
        ConsoleUi.Success($"Asset #{created.Id} {created.DisplayName} was saved to the database.");
        AnsiConsole.WriteLine();
        view.ShowDetails(created);
    }

    // ------------------------------------------------------------------ Update

    private async Task UpdateAsync()
    {
        var asset = await AskForAssetAsync("Asset ID to update");
        view.ShowDetails(asset);
        AnsiConsole.MarkupLine("\n[grey]Press Enter to keep the current value shown in parentheses.[/]\n");

        var input = await AskAssetFieldsAsync(asset.Kind, asset);
        if (!ConsoleInput.Confirm("Save changes?", defaultValue: true))
        {
            ConsoleUi.Warning("No changes were saved.");
            return;
        }

        var updated = await assets.UpdateAsync(asset.Id, input);
        ConsoleUi.Success($"Asset #{updated.Id} was updated.");
        AnsiConsole.WriteLine();
        view.ShowDetails(updated);
    }

    // ------------------------------------------------------------------ Delete

    private async Task DeleteAsync()
    {
        var asset = await AskForAssetAsync("Asset ID to delete");
        view.ShowDetails(asset);

        if (!ConsoleInput.Confirm($"Permanently delete asset #{asset.Id} {asset.DisplayName}?"))
        {
            ConsoleUi.Warning("The asset was not deleted.");
            return;
        }

        await assets.DeleteAsync(asset.Id);
        ConsoleUi.Success($"Asset #{asset.Id} was deleted.");
    }

    // ------------------------------------------------------------------ Search

    private async Task SearchAsync()
    {
        var options = new[] { "Brand", "Model", "Office", "Purchase year", "Combined search (several criteria)" };
        var by = ConsoleInput.Select("Search by", options, o => o);

        AssetQuery query = by switch
        {
            "Brand" => new AssetQuery { Brand = ConsoleInput.Text("Brand contains") },
            "Model" => new AssetQuery { Model = ConsoleInput.Text("Model contains") },
            "Office" => await AskOfficeQueryAsync(),
            "Purchase year" => new AssetQuery { PurchaseYear = ConsoleInput.Integer("Purchase year", 1990, lifecycle.Today.Year) },
            _ => await AskCombinedQueryAsync()
        };

        var results = await assets.SearchAsync(query);
        await view.ShowListAsync(results, "Search result", $"Search: {query.Describe()}");
    }

    private async Task<AssetQuery> AskOfficeQueryAsync()
    {
        var office = await SelectOfficeAsync("Office");
        return new AssetQuery { OfficeId = office.Id, OfficeName = office.Name };
    }

    private async Task<AssetQuery> AskCombinedQueryAsync()
    {
        AnsiConsole.MarkupLine("[grey]Leave a field empty to ignore it.[/]");
        var brand = ConsoleInput.OptionalText("Brand contains", maxLength: 50);
        var model = ConsoleInput.OptionalText("Model contains", maxLength: 100);
        var yearText = ConsoleInput.OptionalText("Purchase year", maxLength: 4);
        int? year = int.TryParse(yearText, out var y) ? y : null;
        if (yearText is not null && year is null)
            ConsoleUi.Warning($"'{yearText}' is not a valid year and will be ignored.");

        Office? office = null;
        if (ConsoleInput.Confirm("Limit to one office?"))
            office = await SelectOfficeAsync("Office");

        return new AssetQuery { Brand = brand, Model = model, PurchaseYear = year, OfficeId = office?.Id, OfficeName = office?.Name };
    }

    // ------------------------------------------------------------------ Filter

    private async Task FilterAsync()
    {
        var filters = new[]
        {
            "Only expired assets",
            "Only assets expiring soon (YELLOW / RED)",
            "Only computers",
            "Only mobile devices",
            "Only office equipment",
            "Assets by office",
            "Unassigned assets",
        };
        var filter = ConsoleInput.Select("Filter", filters, f => f);

        IReadOnlyList<Asset> result;
        string description;
        switch (filter)
        {
            case "Only expired assets":
                result = await assets.GetExpiredAsync();
                description = "Assets that have passed their end of life";
                break;
            case "Only assets expiring soon (YELLOW / RED)":
                result = AssetService.SortByCategoryThenPurchaseDate(await assets.GetExpiringSoonAsync());
                description = "Assets close to end of life";
                break;
            case "Only computers":
                result = await assets.SearchAsync(new AssetQuery { Category = AssetCategory.Computer });
                description = "Computers (laptops and desktops)";
                break;
            case "Only mobile devices":
                result = await assets.SearchAsync(new AssetQuery { Category = AssetCategory.Mobile });
                description = "Mobile devices (phones and tablets)";
                break;
            case "Only office equipment":
                result = await assets.SearchAsync(new AssetQuery { Category = AssetCategory.OfficeEquipment });
                description = "Office equipment";
                break;
            case "Assets by office":
                var query = await AskOfficeQueryAsync();
                result = await assets.SearchAsync(query);
                description = query.Describe();
                break;
            default:
                result = await assets.SearchAsync(new AssetQuery { Assigned = false });
                description = "Assets not assigned to an employee";
                break;
        }

        await view.ShowListAsync(result, "Filtered assets", $"Filter: {description}");
    }

    // ------------------------------------------------------------------ Shared input helpers

    public async Task<Asset> AskForAssetAsync(string label)
    {
        var id = ConsoleInput.Integer(label, 1);
        return await assets.GetByIdAsync(id);
    }

    private async Task<AssetInput> AskAssetFieldsAsync(AssetKind kind, Asset? existing)
    {
        var today = lifecycle.Today;

        var brand = kind == AssetKind.MobilePhone && existing is null
            ? AskPhoneBrand()
            : ConsoleInput.Text("Brand", existing?.Brand, 50);
        var model = ConsoleInput.Text("Model name", existing?.ModelName, 100);
        var serial = ConsoleInput.Text("Serial number", existing?.SerialNumber, 50);
        var purchaseDate = ConsoleInput.Date("Purchase date", existing?.PurchaseDate ?? today, new DateTime(1990, 1, 1), today);
        var price = ConsoleInput.Decimal("Purchase price (USD)", 0.01m, 1_000_000m, existing?.PurchasePriceUsd);

        var office = await SelectOfficeAsync("Office location", existing?.OfficeId);
        AnsiConsole.MarkupLine($"[grey]  → Local price: {Markup.Escape(currency.Format(currency.ConvertFromUsd(price, office.CurrencyCode), office.CurrencyCode))}[/]");

        var defaultWarranty = existing?.WarrantyExpirationDate ?? purchaseDate.AddYears(Asset.DefaultWarrantyYears);
        var warranty = ConsoleInput.Date("Warranty expiration date", defaultWarranty, purchaseDate);

        var employee = await SelectEmployeeOrNoneAsync("Assign to employee", existing?.EmployeeId);

        return new AssetInput(kind, brand, model, serial, purchaseDate, price, office.Id, warranty, employee?.Id);
    }

    private static string AskPhoneBrand()
    {
        const string other = "Other brand…";
        var choice = ConsoleInput.Select("Phone brand", MobilePhone.KnownBrands.Append(other), b => b);
        return choice == other ? ConsoleInput.Text("Brand", maxLength: 50) : choice;
    }

    public async Task<Office> SelectOfficeAsync(string title, int? preselectId = null)
    {
        var all = await offices.GetAllAsync();
        var ordered = all.OrderByDescending(o => o.Id == preselectId).ThenBy(o => o.Id).ToList();
        return ConsoleInput.Select(title, ordered,
            o => $"{o.Name} · {o.Country} · {o.CurrencyCode}{(o.Id == preselectId ? "  (current)" : "")}");
    }

    public async Task<Employee?> SelectEmployeeOrNoneAsync(string title, int? preselectId = null)
    {
        var all = await employees.GetAllAsync();
        var none = new Employee { Id = 0, FullName = "(not assigned)" };
        var ordered = all.OrderByDescending(e => e.Id == preselectId).ThenBy(e => e.FullName).ToList();
        if (preselectId is null) ordered.Insert(0, none); else ordered.Add(none);

        var selected = ConsoleInput.Select(title, ordered,
            e => e.Id == 0 ? e.FullName : $"{e.FullName} · {e.Department}{(e.Id == preselectId ? "  (current)" : "")}");
        return selected.Id == 0 ? null : selected;
    }

    private Table SummaryTable(AssetInput input, Office office)
    {
        var table = new Table().Border(TableBorder.Rounded).BorderColor(Color.Aqua).HideHeaders()
            .AddColumn("Field").AddColumn("Value");
        table.Title("[bold]New asset[/]");
        void Row(string field, string value) => table.AddRow($"[grey]{field}[/]", Markup.Escape(value));

        Row("Type", input.Kind.ToDisplayName());
        Row("Brand / Model", $"{input.Brand} {input.ModelName}");
        Row("Serial number", input.SerialNumber);
        Row("Purchase date", input.PurchaseDate.ToString("yyyy-MM-dd"));
        Row("Price (USD)", currency.Format(input.PurchasePriceUsd, "USD"));
        Row("Local price", currency.Format(currency.ConvertFromUsd(input.PurchasePriceUsd, office.CurrencyCode), office.CurrencyCode));
        Row("Office", office.ToString());
        Row("Warranty expires", input.WarrantyExpirationDate?.ToString("yyyy-MM-dd") ?? "");
        Row("End of life", input.PurchaseDate.AddYears(lifecycleOptions.LifetimeYears).ToString("yyyy-MM-dd"));
        return table;
    }
}
