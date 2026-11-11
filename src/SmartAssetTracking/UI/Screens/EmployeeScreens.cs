using SmartAssetTracking.Helpers;
using SmartAssetTracking.Models;
using SmartAssetTracking.Services;
using Spectre.Console;

namespace SmartAssetTracking.UI.Screens;

/// <summary>Employee management and asset assignment.</summary>
public class EmployeeScreens(
    IEmployeeService employees,
    IAssetService assets,
    ICurrencyService currency,
    ILifecycleService lifecycle,
    AssetScreens assetScreens,
    MenuRunner menu,
    UserSession session)
{
    public Task RunAsync() => menu.RunAsync("Employees & assignments",
    [
        new MenuItem("List employees", ListAsync, UserRole.Manager),
        new MenuItem("Show an employee's assets", ShowEmployeeAssetsAsync, UserRole.Manager),
        new MenuItem("Assign asset to employee", AssignAsync, UserRole.Manager),
        new MenuItem("Unassign asset", UnassignAsync, UserRole.Manager),
        new MenuItem("Add employee", AddAsync, UserRole.Manager),
        new MenuItem("Edit employee", EditAsync, UserRole.Manager),
        new MenuItem("Delete employee", DeleteAsync, UserRole.Admin),
    ]);

    /// <summary>"My assets" for the logged-in user (any role linked to an employee).</summary>
    public async Task MyAssetsAsync()
    {
        var employeeId = session.CurrentUser?.EmployeeId;
        if (employeeId is null)
        {
            ConsoleUi.Warning("Your user account is not linked to an employee record. Ask an Admin to link it.");
            return;
        }

        RenderEmployeeAssets(await employees.GetByIdAsync(employeeId.Value));
    }

    private async Task ListAsync()
    {
        var all = await employees.GetAllAsync();
        var table = new Table().Border(TableBorder.Rounded).BorderColor(Color.Grey)
            .AddColumn("[bold]ID[/]").AddColumn("[bold]Full name[/]").AddColumn("[bold]Department[/]").AddColumn("[bold]Email[/]")
            .AddColumn("[bold]Assets[/]", c => c.RightAligned())
            .AddColumn("[bold]Value (USD)[/]", c => c.RightAligned());

        foreach (var e in all)
            table.AddRow(e.Id.ToString(), Markup.Escape(e.FullName), Markup.Escape(e.Department), Markup.Escape(e.Email),
                e.AssignedAssets.Count.ToString(), Markup.Escape(currency.Format(e.AssignedAssets.Sum(a => a.PurchasePriceUsd), "USD")));

        AnsiConsole.Write(table);
        AnsiConsole.MarkupLine($"[grey]{all.Count} employee(s)[/]");
    }

    private async Task ShowEmployeeAssetsAsync()
    {
        var employee = await SelectEmployeeAsync("Select employee");
        RenderEmployeeAssets(await employees.GetByIdAsync(employee.Id));
    }

    private void RenderEmployeeAssets(Employee employee)
    {
        var grid = new Grid().AddColumn(new GridColumn().PadRight(2)).AddColumn();
        grid.AddRow("[grey]Employee[/]", $"[bold]{Markup.Escape(employee.FullName)}[/]");
        grid.AddRow("[grey]Department[/]", Markup.Escape(employee.Department));
        grid.AddRow("[grey]Email[/]", Markup.Escape(employee.Email));
        AnsiConsole.Write(new Panel(grid).Header("[bold] EMPLOYEE ASSETS [/]").BorderColor(Color.Aqua).RoundedBorder());

        if (employee.AssignedAssets.Count == 0)
        {
            ConsoleUi.Info("No assets are assigned to this employee.");
            return;
        }

        var table = new Table().Border(TableBorder.Rounded).BorderColor(Color.Grey).Title("[bold]Assigned assets[/]")
            .AddColumn("#").AddColumn("[bold]ID[/]").AddColumn("[bold]Type[/]").AddColumn("[bold]Asset[/]").AddColumn("[bold]Serial[/]")
            .AddColumn("[bold]Office[/]").AddColumn("[bold]Status[/]", c => c.Centered());

        var index = 1;
        foreach (var a in employee.AssignedAssets.OrderBy(a => a.Category).ThenBy(a => a.PurchaseDate))
        {
            table.AddRow((index++).ToString(), a.Id.ToString(), Markup.Escape(a.TypeName), Markup.Escape(a.DisplayName),
                Markup.Escape(a.SerialNumber), Markup.Escape(a.Office.Name), ConsoleUi.StatusBadge(lifecycle.GetStatus(a)));
        }

        AnsiConsole.Write(table);
    }

    private async Task AssignAsync()
    {
        var asset = await assetScreens.AskForAssetAsync("Asset ID to assign");
        AnsiConsole.MarkupLine($"Asset: [bold]{Markup.Escape(asset.DisplayName)}[/] · currently: " +
                               (asset.Employee is null ? "[grey]not assigned[/]" : Markup.Escape(asset.Employee.FullName)));
        var employee = await SelectEmployeeAsync("Assign to");

        await assets.AssignAsync(asset.Id, employee.Id);
        ConsoleUi.Success($"{asset.DisplayName} is now assigned to {employee.FullName}.");
    }

    private async Task UnassignAsync()
    {
        var asset = await assetScreens.AskForAssetAsync("Asset ID to unassign");
        if (asset.Employee is null)
        {
            ConsoleUi.Warning("That asset is not assigned to anyone.");
            return;
        }

        if (!ConsoleInput.Confirm($"Remove {asset.DisplayName} from {asset.Employee.FullName}?", true))
            return;

        await assets.AssignAsync(asset.Id, null);
        ConsoleUi.Success($"{asset.DisplayName} is no longer assigned.");
    }

    private async Task AddAsync()
    {
        var name = ConsoleInput.Text("Full name", maxLength: 100);
        var department = ConsoleInput.Text("Department", maxLength: 60);
        var email = AskEmail(null);

        var employee = await employees.CreateAsync(name, department, email);
        ConsoleUi.Success($"Employee {employee.FullName} was created with ID {employee.Id}.");
    }

    private async Task EditAsync()
    {
        var employee = await SelectEmployeeAsync("Employee to edit");
        var name = ConsoleInput.Text("Full name", employee.FullName, 100);
        var department = ConsoleInput.Text("Department", employee.Department, 60);
        var email = AskEmail(employee.Email);

        await employees.UpdateAsync(employee.Id, name, department, email);
        ConsoleUi.Success($"Employee #{employee.Id} was updated.");
    }

    private async Task DeleteAsync()
    {
        var employee = await SelectEmployeeAsync("Employee to delete");
        var count = employee.AssignedAssets.Count;
        var warning = count > 0 ? $" Their {count} asset(s) will become unassigned." : "";
        if (!ConsoleInput.Confirm($"Delete {employee.FullName}?{warning}"))
            return;

        await employees.DeleteAsync(employee.Id);
        ConsoleUi.Success($"{employee.FullName} was deleted.");
    }

    private async Task<Employee> SelectEmployeeAsync(string title)
    {
        var all = await employees.GetAllAsync();
        if (all.Count == 0)
            throw new NotFoundException("There are no employees yet. Add one first.");
        return ConsoleInput.Select(title, all, e => $"{e.FullName} · {e.Department} · {e.AssignedAssets.Count} asset(s)");
    }

    private static string AskEmail(string? current)
    {
        while (true)
        {
            var email = ConsoleInput.Text("Email", current, 150);
            if (EmployeeService.IsValidEmail(email))
                return email;
            ConsoleUi.Error($"'{email}' is not a valid email address.");
        }
    }
}
