// ============================================================================
// ExportService.cs
// ----------------------------------------------------------------------------
// Level 4: "Export reports to TXT, CSV and JSON files."
//
//   * TXT  - a neatly aligned table that is easy for people to read
//   * CSV  - "comma-separated values". Opens in Excel, one row per line
//   * JSON - a structured format used by programs and web APIs
//
// The export works for ANY list of records (assets, office summaries,
// employee counts...). It uses "reflection", which is code that looks at
// a type while the program runs and finds its properties. The property names
// become the column headers, so we don't need a separate exporter per report.
// ============================================================================

using System.Globalization;
using System.Reflection;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using SmartAssetTracking.Helpers;
using SmartAssetTracking.Models;
using SmartAssetTracking.Services;

namespace SmartAssetTracking.Reports;

public enum ExportFormat
{
    Txt,
    Csv,
    Json
}

/// <summary>Flat row used when exporting assets.</summary>
// An Asset has objects inside it (Office, Employee). A file needs simple flat
// values, so we copy each asset into this "flat" record first.
public record AssetExportRow(
    int Id,
    string Type,
    string Category,
    string Brand,
    string Model,
    string SerialNumber,
    string Office,
    string PurchaseDate,
    decimal PriceUsd,
    decimal LocalPrice,
    string Currency,
    string WarrantyExpires,
    string EndOfLife,
    string Status,
    string AssignedTo);

public interface IExportService
{
    string ExportFolder { get; }
    IReadOnlyList<AssetExportRow> ToRows(IEnumerable<Asset> assets);
    Task<string> ExportAssetsAsync(IEnumerable<Asset> assets, ExportFormat format, string reportName, CancellationToken ct = default);

    // A generic method: T can be any row type, e.g. AssetExportRow or OfficeSummary.
    Task<string> ExportAsync<T>(IReadOnlyList<T> rows, ExportFormat format, string reportName, CancellationToken ct = default);
}

/// <summary>Writes any list of records to TXT (aligned table), CSV or JSON using reflection over public properties.</summary>
public class ExportService(
    AppSettings settings,
    ILifecycleService lifecycle,
    ICurrencyService currency,
    TimeProvider timeProvider,
    ILogger<ExportService> logger) : IExportService
{
    // Settings for the JSON writer: indent the output (easier to read), and keep
    // characters like € and å as they are instead of turning them into € codes.
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    // The full folder path. If appsettings.json says "exports" (a relative path),
    // we put it next to the program's .exe file.
    public string ExportFolder => Path.IsPathRooted(settings.ExportFolder)
        ? settings.ExportFolder
        : Path.Combine(AppContext.BaseDirectory, settings.ExportFolder);

    // Converts assets into flat export rows. LINQ's Select transforms every item in a list.
    public IReadOnlyList<AssetExportRow> ToRows(IEnumerable<Asset> assets) =>
        assets.Select(a => new AssetExportRow(
                a.Id,
                a.TypeName,
                a.Category.ToDisplayName(),
                a.Brand,
                a.ModelName,
                a.SerialNumber,
                a.Office.Name,
                a.PurchaseDate.ToString("yyyy-MM-dd"),
                a.PurchasePriceUsd,
                currency.GetLocalPrice(a),
                a.Office.CurrencyCode,
                a.WarrantyExpirationDate.ToString("yyyy-MM-dd"),
                lifecycle.GetEndOfLife(a).ToString("yyyy-MM-dd"),
                lifecycle.GetStatus(a).ToDisplayName(),
                a.Employee?.FullName ?? ""))   // empty text if not assigned
            .ToList();

    public Task<string> ExportAssetsAsync(IEnumerable<Asset> assets, ExportFormat format, string reportName, CancellationToken ct = default) =>
        ExportAsync(ToRows(assets), format, reportName, ct);

    // Writes the rows to a file and returns the full path of that file.
    public async Task<string> ExportAsync<T>(IReadOnlyList<T> rows, ExportFormat format, string reportName, CancellationToken ct = default)
    {
        // Create the folder if it doesn't exist yet (does nothing if it already exists).
        Directory.CreateDirectory(ExportFolder);

        // File name with a timestamp so exports never overwrite each other,
        // e.g. "all-assets-20261111-143000.csv".
        var timestamp = timeProvider.GetLocalNow().ToString("yyyyMMdd-HHmmss");
        var fileName = $"{Sanitize(reportName)}-{timestamp}.{format.ToString().ToLowerInvariant()}";
        var path = Path.Combine(ExportFolder, fileName);

        var content = format switch
        {
            ExportFormat.Csv => ToCsv(rows),
            ExportFormat.Json => JsonSerializer.Serialize(rows, JsonOptions), // .NET turns the objects into JSON text
            ExportFormat.Txt => ToText(rows, reportName),
            _ => throw new ArgumentOutOfRangeException(nameof(format))
        };

        // UTF-8 with a "BOM" (a marker at the start of the file) so Excel shows €, ₺ and å correctly.
        await File.WriteAllTextAsync(path, content, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true), ct);
        logger.LogInformation("Exported {Count} rows to {Path}", rows.Count, path);
        return path;
    }

    // Builds CSV text: a header line with the property names, then one line per row.
    public static string ToCsv<T>(IReadOnlyList<T> rows)
    {
        var props = GetProperties<T>();

        // StringBuilder is an efficient way to build a long text piece by piece.
        var sb = new StringBuilder();
        sb.AppendLine(string.Join(",", props.Select(p => EscapeCsv(p.Name))));
        foreach (var row in rows)
            // p.GetValue(row) reads the property's value from the object (this is reflection).
            sb.AppendLine(string.Join(",", props.Select(p => EscapeCsv(FormatValue(p.GetValue(row))))));
        return sb.ToString();
    }

    // Builds a plain text table where every column is padded to the same width.
    public string ToText<T>(IReadOnlyList<T> rows, string title)
    {
        var props = GetProperties<T>();

        // Turn every value into text first: a list of rows, each row an array of strings.
        var cells = rows.Select(r => props.Select(p => FormatValue(p.GetValue(r))).ToArray()).ToList();

        // Column width = the longest text in that column (header or value).
        var widths = props.Select((p, i) => Math.Max(p.Name.Length, cells.Count == 0 ? 0 : cells.Max(c => c[i].Length))).ToArray();
        var totalWidth = widths.Sum() + (widths.Length - 1) * 2; // 2 spaces between columns

        var sb = new StringBuilder();
        var header = $" {title.ToUpperInvariant()} ";
        var pad = Math.Max(4, (totalWidth - header.Length) / 2);

        // new string('=', 10) creates "==========".
        sb.AppendLine(new string('=', pad) + header + new string('=', pad));
        sb.AppendLine($"Generated: {timeProvider.GetLocalNow():yyyy-MM-dd HH:mm}   Rows: {rows.Count}");
        sb.AppendLine();

        // PadRight(n) adds spaces at the end until the text is n characters long.
        sb.AppendLine(string.Join("  ", props.Select((p, i) => p.Name.PadRight(widths[i]))).TrimEnd());
        sb.AppendLine(new string('-', totalWidth));
        foreach (var row in cells)
            sb.AppendLine(string.Join("  ", row.Select((c, i) => c.PadRight(widths[i]))).TrimEnd());
        sb.AppendLine();
        sb.AppendLine(new string('=', Math.Max(totalWidth, header.Length + 8)));
        return sb.ToString();
    }

    // Reflection: get all public properties of type T, e.g. Id, Type, Brand...
    private static PropertyInfo[] GetProperties<T>() =>
        typeof(T).GetProperties(BindingFlags.Public | BindingFlags.Instance).Where(p => p.CanRead).ToArray();

    // Turns any value into text in a fixed format (dot as decimal point, ISO dates).
    // The "switch" with types ("decimal d =>") is "pattern matching": it checks
    // what kind of value we have and handles each kind differently.
    private static string FormatValue(object? value) => value switch
    {
        null => "",
        decimal d => d.ToString("0.00", CultureInfo.InvariantCulture),
        double d => d.ToString("0.##", CultureInfo.InvariantCulture),
        DateTime dt => dt.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString() ?? ""
    };

    // CSV rule: if a value contains a comma, a quote or a line break, wrap it in
    // quotes and double any quotes inside. Example: say "hi" becomes "say ""hi""".
    public static string EscapeCsv(string value) =>
        value.IndexOfAny([',', '"', '\n', '\r']) >= 0
            ? $"\"{value.Replace("\"", "\"\"")}\""
            : value;

    // Replaces characters that aren't allowed in file names (like / or :) and spaces with "-".
    private static string Sanitize(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var cleaned = new string(name.Select(c => invalid.Contains(c) || char.IsWhiteSpace(c) ? '-' : c).ToArray());
        return string.IsNullOrWhiteSpace(cleaned) ? "report" : cleaned.ToLowerInvariant();
    }
}
