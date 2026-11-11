using SmartAssetTracking.Models;
using SmartAssetTracking.Services;
using Spectre.Console;

namespace SmartAssetTracking.UI;

/// <summary>Common screen elements: headers, messages and colours.</summary>
public static class ConsoleUi
{
    public const string AppTitle = "Smart Asset Tracking";

    public static void Banner()
    {
        AnsiConsole.Write(new FigletText("Asset Tracker").Color(Color.Aqua).Centered());
        AnsiConsole.Write(new Rule($"[grey]{AppTitle} · Entity Framework Core · SQL Server Express[/]").RuleStyle("grey").Centered());
        AnsiConsole.WriteLine();
    }

    /// <summary>Clears the screen and draws the title bar with the logged-in user.</summary>
    public static void Header(string title, UserSession? session = null)
    {
        AnsiConsole.Clear();
        var user = session?.CurrentUser;
        var right = user is null
            ? ""
            : $"[grey]Logged in as[/] [bold]{Markup.Escape(user.Username)}[/] [black on {RoleColor(user.Role)}] {user.Role} [/]";

        var grid = new Grid().AddColumn(new GridColumn().NoWrap()).AddColumn(new GridColumn().RightAligned());
        grid.AddRow($"[bold aqua]▣ {AppTitle}[/]", right);
        AnsiConsole.Write(grid);
        AnsiConsole.Write(new Rule($"[bold yellow]{Markup.Escape(title.ToUpperInvariant())}[/]").RuleStyle("aqua").LeftJustified());
        AnsiConsole.WriteLine();
    }

    public static void Success(string message) => AnsiConsole.MarkupLine($"[green]✔ {Markup.Escape(message)}[/]");

    public static void Error(string message) => AnsiConsole.MarkupLine($"[red]✖ {Markup.Escape(message)}[/]");

    public static void Warning(string message) => AnsiConsole.MarkupLine($"[yellow]! {Markup.Escape(message)}[/]");

    public static void Info(string message) => AnsiConsole.MarkupLine($"[grey]{Markup.Escape(message)}[/]");

    public static void Errors(IEnumerable<string> errors)
    {
        var panel = new Panel(new Rows(errors.Select(e => new Markup($"[red]• {Markup.Escape(e)}[/]"))))
            .Header("[red bold] Please correct the following [/]")
            .BorderColor(Color.Red)
            .RoundedBorder();
        AnsiConsole.Write(panel);
    }

    public static void Pause()
    {
        AnsiConsole.WriteLine();
        AnsiConsole.Markup("[grey]Press any key to continue...[/]");
        AnsiConsole.Console.Input.ReadKey(intercept: true);
        AnsiConsole.WriteLine();
    }

    public static string RoleColor(UserRole role) => role switch
    {
        UserRole.Admin => "red",
        UserRole.Manager => "yellow",
        _ => "green"
    };

    public static string StatusColor(LifecycleStatus status) => status switch
    {
        LifecycleStatus.Yellow => "yellow",
        LifecycleStatus.Red => "red",
        LifecycleStatus.Expired => "grey",
        _ => "white"
    };

    public static string StatusBadge(LifecycleStatus status) => status switch
    {
        LifecycleStatus.Yellow => "[black on yellow] YELLOW [/]",
        LifecycleStatus.Red => "[white on red] RED [/]",
        LifecycleStatus.Expired => "[white on grey] EXPIRED [/]",
        _ => "[black on green] NORMAL [/]"
    };

    public static void StatusLegend(Helpers.LifecycleOptions options)
    {
        AnsiConsole.MarkupLine(
            $"[grey]Lifetime {options.LifetimeYears} years ·[/] {StatusBadge(LifecycleStatus.Yellow)} [grey]< {options.YellowThresholdMonths} months left ·[/] " +
            $"{StatusBadge(LifecycleStatus.Red)} [grey]< {options.RedThresholdMonths} months left ·[/] {StatusBadge(LifecycleStatus.Expired)} [grey]end of life reached[/]");
    }
}
