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
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public string ExportFolder => Path.IsPathRooted(settings.ExportFolder)
        ? settings.ExportFolder
        : Path.Combine(AppContext.BaseDirectory, settings.ExportFolder);

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
                a.Employee?.FullName ?? ""))
            .ToList();

    public Task<string> ExportAssetsAsync(IEnumerable<Asset> assets, ExportFormat format, string reportName, CancellationToken ct = default) =>
        ExportAsync(ToRows(assets), format, reportName, ct);

    public async Task<string> ExportAsync<T>(IReadOnlyList<T> rows, ExportFormat format, string reportName, CancellationToken ct = default)
    {
        Directory.CreateDirectory(ExportFolder);

        var timestamp = timeProvider.GetLocalNow().ToString("yyyyMMdd-HHmmss");
        var fileName = $"{Sanitize(reportName)}-{timestamp}.{format.ToString().ToLowerInvariant()}";
        var path = Path.Combine(ExportFolder, fileName);

        var content = format switch
        {
            ExportFormat.Csv => ToCsv(rows),
            ExportFormat.Json => JsonSerializer.Serialize(rows, JsonOptions),
            ExportFormat.Txt => ToText(rows, reportName),
            _ => throw new ArgumentOutOfRangeException(nameof(format))
        };

        await File.WriteAllTextAsync(path, content, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true), ct);
        logger.LogInformation("Exported {Count} rows to {Path}", rows.Count, path);
        return path;
    }

    public static string ToCsv<T>(IReadOnlyList<T> rows)
    {
        var props = GetProperties<T>();
        var sb = new StringBuilder();
        sb.AppendLine(string.Join(",", props.Select(p => EscapeCsv(p.Name))));
        foreach (var row in rows)
            sb.AppendLine(string.Join(",", props.Select(p => EscapeCsv(FormatValue(p.GetValue(row))))));
        return sb.ToString();
    }

    public string ToText<T>(IReadOnlyList<T> rows, string title)
    {
        var props = GetProperties<T>();
        var cells = rows.Select(r => props.Select(p => FormatValue(p.GetValue(r))).ToArray()).ToList();
        var widths = props.Select((p, i) => Math.Max(p.Name.Length, cells.Count == 0 ? 0 : cells.Max(c => c[i].Length))).ToArray();
        var totalWidth = widths.Sum() + (widths.Length - 1) * 2;

        var sb = new StringBuilder();
        var header = $" {title.ToUpperInvariant()} ";
        var pad = Math.Max(4, (totalWidth - header.Length) / 2);
        sb.AppendLine(new string('=', pad) + header + new string('=', pad));
        sb.AppendLine($"Generated: {timeProvider.GetLocalNow():yyyy-MM-dd HH:mm}   Rows: {rows.Count}");
        sb.AppendLine();
        sb.AppendLine(string.Join("  ", props.Select((p, i) => p.Name.PadRight(widths[i]))).TrimEnd());
        sb.AppendLine(new string('-', totalWidth));
        foreach (var row in cells)
            sb.AppendLine(string.Join("  ", row.Select((c, i) => c.PadRight(widths[i]))).TrimEnd());
        sb.AppendLine();
        sb.AppendLine(new string('=', Math.Max(totalWidth, header.Length + 8)));
        return sb.ToString();
    }

    private static PropertyInfo[] GetProperties<T>() =>
        typeof(T).GetProperties(BindingFlags.Public | BindingFlags.Instance).Where(p => p.CanRead).ToArray();

    private static string FormatValue(object? value) => value switch
    {
        null => "",
        decimal d => d.ToString("0.00", CultureInfo.InvariantCulture),
        double d => d.ToString("0.##", CultureInfo.InvariantCulture),
        DateTime dt => dt.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString() ?? ""
    };

    public static string EscapeCsv(string value) =>
        value.IndexOfAny([',', '"', '\n', '\r']) >= 0
            ? $"\"{value.Replace("\"", "\"\"")}\""
            : value;

    private static string Sanitize(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var cleaned = new string(name.Select(c => invalid.Contains(c) || char.IsWhiteSpace(c) ? '-' : c).ToArray());
        return string.IsNullOrWhiteSpace(cleaned) ? "report" : cleaned.ToLowerInvariant();
    }
}
