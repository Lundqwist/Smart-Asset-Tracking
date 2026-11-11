using SmartAssetTracking.Helpers;
using SmartAssetTracking.Models;
using SmartAssetTracking.Services;
using Spectre.Console;

namespace SmartAssetTracking.UI.Screens;

/// <summary>User accounts and roles (Admin), plus changing your own password.</summary>
public class UserScreens(
    IAuthService auth,
    AssetScreens assetScreens,
    MenuRunner menu,
    UserSession session)
{
    public Task RunAsync() => menu.RunAsync("User administration",
    [
        new MenuItem("List users", ListAsync, UserRole.Admin),
        new MenuItem("Create user", CreateAsync, UserRole.Admin),
        new MenuItem("Change user role", ChangeRoleAsync, UserRole.Admin),
        new MenuItem("Reset user password", ResetPasswordAsync, UserRole.Admin),
        new MenuItem("Delete user", DeleteAsync, UserRole.Admin),
    ]);

    public async Task ChangeOwnPasswordAsync()
    {
        var current = ConsoleInput.Secret("Current password");
        var next = AskNewPassword();
        await auth.ChangeOwnPasswordAsync(current, next);
        ConsoleUi.Success("Your password was changed.");
    }

    private async Task ListAsync()
    {
        var users = await auth.GetUsersAsync();
        var table = new Table().Border(TableBorder.Rounded).BorderColor(Color.Grey)
            .AddColumn("[bold]ID[/]").AddColumn("[bold]Username[/]").AddColumn("[bold]Role[/]").AddColumn("[bold]Linked employee[/]");
        foreach (var u in users)
            table.AddRow(u.Id.ToString(), Markup.Escape(u.Username), $"[{ConsoleUi.RoleColor(u.Role)}]{u.Role}[/]",
                u.Employee is null ? "[grey]—[/]" : Markup.Escape(u.Employee.ToString()));
        AnsiConsole.Write(table);
    }

    private async Task CreateAsync()
    {
        var username = ConsoleInput.Text("Username", maxLength: 50);
        var password = AskNewPassword();
        var role = SelectRole(null);
        var employee = await assetScreens.SelectEmployeeOrNoneAsync("Link to employee (for 'My assets')");

        var user = await auth.CreateUserAsync(username, password, role, employee?.Id);
        ConsoleUi.Success($"User '{user.Username}' ({user.Role}) was created.");
    }

    private async Task ChangeRoleAsync()
    {
        var user = await SelectUserAsync("User");
        var role = SelectRole(user.Role);
        await auth.ChangeRoleAsync(user.Id, role);
        ConsoleUi.Success($"{user.Username} is now {role}.");
    }

    private async Task ResetPasswordAsync()
    {
        var user = await SelectUserAsync("User");
        await auth.ResetPasswordAsync(user.Id, AskNewPassword());
        ConsoleUi.Success($"The password for {user.Username} was reset.");
    }

    private async Task DeleteAsync()
    {
        var user = await SelectUserAsync("User to delete");
        if (!ConsoleInput.Confirm($"Delete user '{user.Username}'?"))
            return;
        await auth.DeleteUserAsync(user.Id);
        ConsoleUi.Success($"User '{user.Username}' was deleted.");
    }

    private async Task<User> SelectUserAsync(string title)
    {
        var users = await auth.GetUsersAsync();
        return ConsoleInput.Select(title, users,
            u => $"{u.Username} · {u.Role}{(u.Id == session.CurrentUser?.Id ? "  (you)" : "")}");
    }

    private static UserRole SelectRole(UserRole? current) =>
        ConsoleInput.Select("Role", Enum.GetValues<UserRole>().OrderByDescending(r => r),
            r => r switch
            {
                UserRole.Admin => "Admin    – full access incl. users and offices",
                UserRole.Manager => "Manager  – manage assets, employees, maintenance, reports",
                _ => "Employee – view assets and own assigned assets"
            } + (r == current ? "  (current)" : ""));

    private static string AskNewPassword()
    {
        while (true)
        {
            var password = ConsoleInput.Secret($"New password (min {AuthService.MinPasswordLength} characters)");
            if (password.Length < AuthService.MinPasswordLength)
            {
                ConsoleUi.Error($"The password must be at least {AuthService.MinPasswordLength} characters.");
                continue;
            }

            if (ConsoleInput.Secret("Repeat new password") == password)
                return password;
            ConsoleUi.Error("The passwords do not match. Try again.");
        }
    }
}
