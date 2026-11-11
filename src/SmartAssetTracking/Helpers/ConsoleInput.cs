// ============================================================================
// ConsoleInput.cs
// ----------------------------------------------------------------------------
// Reusable methods for asking the user for input: text, numbers, dates,
// yes/no and lists to pick from with the arrow keys.
//
// The assignment requires "validation for incorrect inputs" and handling
// "invalid user inputs gracefully". Every method here keeps asking until the
// input is valid and shows a red hint when it isn't, so a typo never crashes
// the program.
//
// The prompts are built with the Spectre.Console library (a NuGet package).
// It draws colours, tables and menus in the terminal. Text in square
// brackets like "[aqua]...[/]" is Spectre "markup": it colours the text
// between the tags.
// ============================================================================

using System.Globalization;
using Spectre.Console;

namespace SmartAssetTracking.Helpers;

/// <summary>
/// Reusable, validated console prompts. Every prompt re-asks until the input is valid,
/// so invalid input is handled gracefully instead of crashing the program.
/// </summary>
public static class ConsoleInput
{
    // The date formats we accept: 2024-02-11, 2024-2-1 and 20240211.
    private static readonly string[] DateFormats = ["yyyy-MM-dd", "yyyy-M-d", "yyyyMMdd"];

    // Asks for text that can't be empty. If defaultValue is given, pressing Enter keeps it.
    public static string Text(string label, string? defaultValue = null, int maxLength = 100)
    {
        // TextPrompt<string> is Spectre's input box. .Validate(...) runs our check
        // every time the user presses Enter. On an error, Spectre shows the message and asks again.
        var prompt = new TextPrompt<string>(Label(label))
            .Validate(value =>
            {
                if (string.IsNullOrWhiteSpace(value)) return ValidationResult.Error("[red]This field cannot be empty.[/]");
                if (value.Trim().Length > maxLength) return ValidationResult.Error($"[red]Maximum {maxLength} characters.[/]");
                return ValidationResult.Success();
            });

        if (!string.IsNullOrEmpty(defaultValue))
            prompt.DefaultValue(defaultValue).ShowDefaultValue();

        // AnsiConsole.Prompt shows the prompt and waits until valid text is entered.
        return AnsiConsole.Prompt(prompt).Trim();
    }

    /// <summary>Optional text. Returns null when left empty.</summary>
    public static string? OptionalText(string label, string? defaultValue = null, int maxLength = 500)
    {
        var prompt = new TextPrompt<string>(Label(label) + " [grey](optional, Enter to skip)[/]")
            .AllowEmpty() // empty input is OK here
            .Validate(value => value.Trim().Length > maxLength
                ? ValidationResult.Error($"[red]Maximum {maxLength} characters.[/]")
                : ValidationResult.Success());

        if (!string.IsNullOrEmpty(defaultValue))
            prompt.DefaultValue(defaultValue).ShowDefaultValue();

        var result = AnsiConsole.Prompt(prompt).Trim();
        return string.IsNullOrEmpty(result) ? null : result;
    }

    // Password input: .Secret() shows "*" instead of the typed characters.
    public static string Secret(string label) =>
        AnsiConsole.Prompt(new TextPrompt<string>(Label(label)).Secret().AllowEmpty());

    // Asks for a whole number between min and max.
    // int.MinValue / int.MaxValue are the smallest and largest possible int, i.e. "no limit".
    public static int Integer(string label, int min = int.MinValue, int max = int.MaxValue, int? defaultValue = null)
    {
        var text = AnsiConsole.Prompt(WithDefault(new TextPrompt<string>(Label(label))
            .Validate(value =>
                // TryParse tries to convert text to a number. It returns false (instead of crashing) on bad input.
                int.TryParse(value.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) && n >= min && n <= max
                    ? ValidationResult.Success()
                    : ValidationResult.Error(RangeMessage("a whole number", min, max))), defaultValue?.ToString(CultureInfo.InvariantCulture)));

        // The text was already validated, so Parse can't fail here.
        return int.Parse(text.Trim(), CultureInfo.InvariantCulture);
    }

    // Asks for a decimal number, e.g. a price. Accepts both "1299.99" and "1299,99".
    public static decimal Decimal(string label, decimal min, decimal max, decimal? defaultValue = null)
    {
        var text = AnsiConsole.Prompt(WithDefault(new TextPrompt<string>(Label(label))
            .Validate(value =>
                TryParseDecimal(value, out var d) && d >= min && d <= max
                    ? ValidationResult.Success()
                    : ValidationResult.Error($"[red]Enter a number between {min:0.##} and {max:0.##} (e.g. 1299.99).[/]")),
            defaultValue?.ToString("0.00", CultureInfo.InvariantCulture)));
        TryParseDecimal(text, out var result);
        return result;
    }

    // Asks for a date in the format yyyy-MM-dd, optionally limited to a range (min..max).
    public static DateTime Date(string label, DateTime? defaultValue = null, DateTime? min = null, DateTime? max = null)
    {
        var text = AnsiConsole.Prompt(WithDefault(new TextPrompt<string>(Label(label) + " [grey](yyyy-MM-dd)[/]")
            .Validate(value =>
            {
                if (!TryParseDate(value, out var date))
                    return ValidationResult.Error("[red]Invalid date. Use the format yyyy-MM-dd, e.g. 2024-02-11.[/]");
                if (min is not null && date < min.Value.Date)
                    return ValidationResult.Error($"[red]The date cannot be before {min:yyyy-MM-dd}.[/]");
                if (max is not null && date > max.Value.Date)
                    return ValidationResult.Error($"[red]The date cannot be after {max:yyyy-MM-dd}.[/]");
                return ValidationResult.Success();
            }), defaultValue?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)));
        TryParseDate(text, out var result);
        return result;
    }

    /// <summary>Optional date. Returns null when left empty.</summary>
    public static DateTime? OptionalDate(string label, DateTime? defaultValue = null, DateTime? min = null)
    {
        var prompt = new TextPrompt<string>(Label(label) + " [grey](yyyy-MM-dd, Enter to skip)[/]")
            .AllowEmpty()
            .Validate(value =>
            {
                if (string.IsNullOrWhiteSpace(value)) return ValidationResult.Success();
                if (!TryParseDate(value, out var date))
                    return ValidationResult.Error("[red]Invalid date. Use the format yyyy-MM-dd.[/]");
                if (min is not null && date < min.Value.Date)
                    return ValidationResult.Error($"[red]The date cannot be before {min:yyyy-MM-dd}.[/]");
                return ValidationResult.Success();
            });
        if (defaultValue is not null)
            prompt.DefaultValue(defaultValue.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)).ShowDefaultValue();

        var text = AnsiConsole.Prompt(prompt);
        return TryParseDate(text, out var result) ? result : null;
    }

    // A yes/no question. The user types y or n.
    public static bool Confirm(string question, bool defaultValue = false) =>
        AnsiConsole.Prompt(new ConfirmationPrompt($"[yellow]{question}[/]") { DefaultValue = defaultValue });

    /// <summary>Shows an arrow-key selection list.</summary>
    // "<T>" makes this a GENERIC method: it works with any type T (offices, employees,
    // strings...). "display" is a function that turns each item into the text shown in the list.
    // "where T : notnull" means T can't be a nullable type.
    public static T Select<T>(string title, IEnumerable<T> choices, Func<T, string> display, int pageSize = 12) where T : notnull
    {
        var list = choices.ToList();
        if (list.Count == 0)
            throw new InvalidOperationException("There is nothing to choose from.");

        return AnsiConsole.Prompt(new SelectionPrompt<T>()
            .Title($"[bold]{title}[/]")
            // Math.Max/Min keep the page size between 3 and the number of choices.
            .PageSize(Math.Max(3, Math.Min(pageSize, list.Count)))
            .MoreChoicesText("[grey](Move up and down to reveal more choices)[/]")
            .HighlightStyle(new Style(Color.Black, Color.Aqua))
            // Markup.Escape makes sure names containing "[" or "]" aren't read as colour tags.
            .UseConverter(item => Markup.Escape(display(item)))
            .AddChoices(list));
    }

    // "out decimal result" is an OUTPUT parameter: the method gives back a second value through it.
    // The bool return value says whether parsing worked.
    public static bool TryParseDecimal(string? value, out decimal result)
    {
        result = 0;
        if (string.IsNullOrWhiteSpace(value)) return false;
        var cleaned = value.Trim().Replace(" ", "").Replace("$", "");
        // Accept "1299.99", "1,299.99" and the Swedish style "1299,99".
        // A single comma followed by exactly three digits ("1,299") is treated as a thousands separator.
        var commaIndex = cleaned.IndexOf(',');
        if (commaIndex >= 0 && cleaned.Count(c => c == ',') == 1 && !cleaned.Contains('.')
            && cleaned.Length - commaIndex - 1 != 3)
            cleaned = cleaned.Replace(',', '.');

        // InvariantCulture means "." is ALWAYS the decimal point, whatever the computer's language is.
        return decimal.TryParse(cleaned, NumberStyles.Number, CultureInfo.InvariantCulture, out result);
    }

    // TryParseExact only accepts the exact formats in DateFormats, so "11/02/2024"
    // (is that February or November?) is rejected instead of guessed.
    public static bool TryParseDate(string? value, out DateTime result) =>
        DateTime.TryParseExact(value?.Trim(), DateFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out result);

    // Adds a default value to a prompt, but only if there is one.
    private static TextPrompt<string> WithDefault(TextPrompt<string> prompt, string? defaultValue)
    {
        if (defaultValue is not null)
            prompt.DefaultValue(defaultValue).ShowDefaultValue();
        return prompt;
    }

    // All labels look the same: aqua coloured, followed by a colon.
    private static string Label(string label) => $"[aqua]{Markup.Escape(label)}:[/]";

    // Builds a helpful error message depending on which limits are set.
    // This "switch" matches on a tuple of two values at once.
    private static string RangeMessage(string what, int min, int max) =>
        (min, max) switch
        {
            (int.MinValue, int.MaxValue) => $"[red]Enter {what}.[/]",
            (_, int.MaxValue) => $"[red]Enter {what} of at least {min}.[/]",
            _ => $"[red]Enter {what} between {min} and {max}.[/]"
        };
}
