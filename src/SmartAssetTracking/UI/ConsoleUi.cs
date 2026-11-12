// ============================================================================
// ConsoleUi.cs
// ----------------------------------------------------------------------------
// Small drawing helpers that every screen uses: the big banner, the title bar,
// coloured success/error messages, status badges and "press any key".
// Keeping them in one place means every screen looks the same, and a colour
// only needs changing here ("avoid duplicated code" from the assignment).
//
// Spectre.Console markup reminder: "[red]text[/]" prints red text,
// "[black on yellow] X [/]" prints black text on a yellow background.
// ============================================================================

using SmartAssetTracking.Models;
using SmartAssetTracking.Services;
using Spectre.Console;

namespace SmartAssetTracking.UI;

/// <summary>Common screen elements: headers, messages and colours.</summary>
public static class ConsoleUi
{
    public const string AppTitle = "Smart Asset Tracking";

    // The big "Asset Tracker" logo shown at start-up and on the login screen.
    public static void Banner()
    {
        // FigletText draws big letters made of characters.
        AnsiConsole.Write(new FigletText("Asset Tracker").Color(Color.Aqua).Centered());
        // A Rule is a horizontal line with optional text in it.
        AnsiConsole.Write(new Rule($"[grey]{AppTitle} · Entity Framework Core · SQL Server Express[/]").RuleStyle("grey").Centered());
        AnsiConsole.WriteLine();
    }

    /// <summary>Clears the screen and draws the title bar with the logged-in user.</summary>
    public static void Header(string title, UserSession? session = null)
    {
        AnsiConsole.Clear();
        var user = session?.CurrentUser;

        // Right side of the title bar: "Logged in as admin [Admin]" (empty if nobody is logged in).
        var right = user is null
            ? ""
            : $"[grey]Logged in as[/] [bold]{Markup.Escape(user.Username)}[/] [black on {RoleColor(user.Role)}] {user.Role} [/]";

        // A Grid is an invisible table, used here to put text on the left AND on the right of one line.
        var grid = new Grid().AddColumn(new GridColumn().NoWrap()).AddColumn(new GridColumn().RightAligned());
        grid.AddRow($"[bold aqua]▣ {AppTitle}[/]", right);
        AnsiConsole.Write(grid);
        AnsiConsole.Write(new Rule($"[bold yellow]{Markup.Escape(title.ToUpperInvariant())}[/]").RuleStyle("aqua").LeftJustified());
        AnsiConsole.WriteLine();
    }

    // Message helpers. Markup.Escape is important: if a message contains "[" or "]",
    // Spectre would otherwise think it's a colour tag and crash.
    public static void Success(string message) => AnsiConsole.MarkupLine($"[green]✔ {Markup.Escape(message)}[/]");

    public static void Error(string message) => AnsiConsole.MarkupLine($"[yellow]✖ {Markup.Escape(message)}[/]");

    public static void Warning(string message) => AnsiConsole.MarkupLine($"[red]! {Markup.Escape(message)}[/]");

    public static void Info(string message) => AnsiConsole.MarkupLine($"[grey]{Markup.Escape(message)}[/]");

    // Shows a list of validation errors inside a red box.
    public static void Errors(IEnumerable<string> errors)
    {
        // Rows stacks several pieces of text on top of each other.
        var panel = new Panel(new Rows(errors.Select(e => new Markup($"[red]• {Markup.Escape(e)}[/]"))))
            .Header("[red bold] Please correct the following [/]")
            .BorderColor(Color.Red)
            .RoundedBorder();
        AnsiConsole.Write(panel);
    }

    // Waits for any key, so the user has time to read the screen before it is cleared.
    public static void Pause()
    {
        AnsiConsole.WriteLine();
        AnsiConsole.Markup("[grey]Press any key to continue...[/]");
        // Reading the key through Spectre (instead of Console.ReadKey) lets the
        // automated UI tests "press" keys for us.
        AnsiConsole.Console.Input.ReadKey(intercept: true);
        AnsiConsole.WriteLine();
    }

    // One colour per role, used for the badge in the title bar.
    public static string RoleColor(UserRole role) => role switch
    {
        UserRole.Admin => "red",
        UserRole.Manager => "yellow",
        _ => "green"
    };

    // Text colour for a table row, based on the asset's lifecycle status (Level 2: highlight assets).
    public static string StatusColor(LifecycleStatus status) => status switch
    {
        LifecycleStatus.Yellow => "red",
        LifecycleStatus.Red => "yellow",
        LifecycleStatus.Expired => "grey",
        _ => "white"
    };

    // A small coloured "badge" with the status name, e.g. a yellow box saying YELLOW.
    public static string StatusBadge(LifecycleStatus status) => status switch
    {
        LifecycleStatus.Yellow => "[white on red] YELLOW [/]",
        LifecycleStatus.Red => "[black on yellow] RED [/]",
        LifecycleStatus.Expired => "[white on grey] EXPIRED [/]",
        _ => "[black on green] NORMAL [/]"
    };

    // The explanation line under asset tables, so users know what the colours mean.
    public static void StatusLegend(Helpers.LifecycleOptions options)
    {
        AnsiConsole.MarkupLine(
            $"[grey]Lifetime {options.LifetimeYears} years ·[/] {StatusBadge(LifecycleStatus.Yellow)} [grey]< {options.YellowThresholdMonths} months left ·[/] " +
            $"{StatusBadge(LifecycleStatus.Red)} [grey]< {options.RedThresholdMonths} months left ·[/] {StatusBadge(LifecycleStatus.Expired)} [grey]end of life reached[/]");
    }
}
