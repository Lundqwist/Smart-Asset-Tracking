// ============================================================================
// UiTests.cs - automated tests that "click through" the real menus
// ----------------------------------------------------------------------------
// Spectre.Console.Testing provides a fake console (TestConsole). Instead of a
// person pressing keys, the test pushes key presses into it in advance:
//     PushTextWithEnter("admin")      -> types "admin" and presses Enter
//     PushKey(ConsoleKey.DownArrow)   -> moves down one item in a menu
// Then it runs the real Application and checks the text that was drawn.
// This catches crashes and drawing errors in screens without anyone sitting
// at the keyboard.
// ============================================================================

using SmartAssetTracking.UI;
using Spectre.Console;
using Spectre.Console.Testing;

namespace SmartAssetTracking.Tests;

/// <summary>
/// Drives the real menus with scripted key presses through Spectre's TestConsole.
/// When the scripted input runs out the console throws, which ends the session.
/// Any markup error or crash inside a screen would show up in the output.
/// </summary>
[CollectionDefinition(nameof(UiTests), DisableParallelization = true)]
public class UiCollection;

[Collection(nameof(UiTests))]
public class UiTests
{
    // Main menu order for an Admin: 0 Dashboard, 1 My assets, 2 Assets, 3 Offices, 4 Employees,
    // 5 Maintenance, 6 Reports, 7 User administration, 8 Change my password, 9 Exit, 10 Log out
    private static async Task<string> RunAppAsync(string username, string password, Action<TestConsoleInput> script)
    {
        var console = new TestConsole().Interactive().Width(180);
        console.Profile.Capabilities.Unicode = true;
        var previous = AnsiConsole.Console;
        AnsiConsole.Console = console;
        try
        {
            await using var host = await TestHost.CreateAsync(signedInAs: null);
            console.Input.PushTextWithEnter(username);
            console.Input.PushTextWithEnter(password);
            script(console.Input);

            try
            {
                await host.Get<Application>().RunAsync();
            }
            catch (InvalidOperationException ex) when (ex.Message.Contains("input", StringComparison.OrdinalIgnoreCase))
            {
                // Scripted input exhausted - expected.
            }

            // Optional: write the rendered screens to a folder for manual inspection.
            if (Environment.GetEnvironmentVariable("SMARTASSET_UI_DUMP") is { Length: > 0 } dumpFolder)
            {
                Directory.CreateDirectory(dumpFolder);
                await File.WriteAllTextAsync(Path.Combine(dumpFolder, $"{username}-{Guid.NewGuid():N}.txt"), console.Output);
            }

            return console.Output;
        }
        finally
        {
            AnsiConsole.Console = previous;
        }
    }

    private static void Down(TestConsoleInput input, int times)
    {
        for (var i = 0; i < times; i++)
            input.PushKey(ConsoleKey.DownArrow);
    }

    private static void AssertNoErrors(string output)
    {
        // "No input available" is the scripted session ending inside a screen - anything else is a real error.
        var unexpected = System.Text.RegularExpressions.Regex.Matches(output, @"Unexpected error: (?!No input)[^\r\n]*");
        Assert.True(unexpected.Count == 0, string.Join(Environment.NewLine, unexpected.Select(m => m.Value)));
        Assert.DoesNotContain("fatal", output, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Admin_sees_dashboard_and_paged_asset_list()
    {
        var output = await RunAppAsync("admin", "1234", input =>
        {
            input.PushKey(ConsoleKey.Enter);            // Dashboard
            input.PushKey(ConsoleKey.Spacebar);         // pause
            Down(input, 2); input.PushKey(ConsoleKey.Enter); // Assets
            Down(input, 1); input.PushKey(ConsoleKey.Enter); // Show all assets
            input.PushKey(ConsoleKey.Enter);            // Next page
        });

        AssertNoErrors(output);
        Assert.Contains("Database ready", output);
        Assert.Contains("DASHBOARD", output);
        Assert.Contains("Total company value", output);
        Assert.Contains("COMPANY ASSETS", output);
        Assert.Contains("Computers", output);
        Assert.Contains("Mobile Devices", output);
        Assert.Contains("Page 2 of 2", output);
        Assert.Contains("YELLOW", output);
        Assert.Contains("RED", output);
        Assert.Contains("EXPIRED", output);
    }

    [Fact]
    public async Task Office_overview_and_reports_render()
    {
        var output = await RunAppAsync("admin", "1234", input =>
        {
            Down(input, 3); input.PushKey(ConsoleKey.Enter);  // Offices
            input.PushKey(ConsoleKey.Enter);                  // Offices overview
            input.PushKey(ConsoleKey.Spacebar);
            Down(input, 1); input.PushKey(ConsoleKey.Enter);  // Show assets in an office
            input.PushKey(ConsoleKey.Enter);                  // first office (Sweden)
            input.PushKey(ConsoleKey.Spacebar);
            Down(input, 5); input.PushKey(ConsoleKey.Enter);  // Back to main menu
            Down(input, 6); input.PushKey(ConsoleKey.Enter);  // Reports
            input.PushKey(ConsoleKey.Enter);                  // Office report
            input.PushKey(ConsoleKey.Spacebar);
            Down(input, 1); input.PushKey(ConsoleKey.Enter);  // Assets near expiration
            input.PushKey(ConsoleKey.Spacebar);
            Down(input, 2); input.PushKey(ConsoleKey.Enter);  // Most expensive
            input.PushTextWithEnter("5");
            input.PushKey(ConsoleKey.Spacebar);
            Down(input, 5); input.PushKey(ConsoleKey.Enter);  // Exchange rates
        });

        AssertNoErrors(output);
        Assert.Contains("SWEDEN OFFICE", output);
        Assert.Contains("Total office value", output);
        Assert.Contains("Office asset counts & values", output);
        Assert.Contains("Assets near expiration", output);
        Assert.Contains("Top 5 most expensive assets", output);
        Assert.Contains("Exchange rates (base USD)", output);
        Assert.Contains("€", output);
        Assert.Contains("₺", output);
    }

    [Fact]
    public async Task Employee_role_sees_limited_menu_and_own_assets()
    {
        var output = await RunAppAsync("employee", "1234", input =>
        {
            Down(input, 1); input.PushKey(ConsoleKey.Enter);  // My assets
        });

        AssertNoErrors(output);
        Assert.Contains("EMPLOYEE ASSETS", output);
        Assert.Contains("Sarah Johnson", output);
        Assert.Contains("ThinkPad X1", output);
        Assert.DoesNotContain("User administration", output);
        Assert.DoesNotContain("Employees & assignments", output);
    }

    [Fact]
    public async Task Wrong_password_is_rejected()
    {
        var output = await RunAppAsync("admin", "nope", _ => { });

        Assert.Contains("Invalid username or password", output);
    }
}
