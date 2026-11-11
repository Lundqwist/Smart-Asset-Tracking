// ============================================================================
// AppOptions.cs
// ----------------------------------------------------------------------------
// "Options classes": plain classes whose properties mirror the sections of
// appsettings.json. At startup (see ServiceRegistration.cs) the JSON values
// are copied into these objects, so the rest of the code can write
// options.LifetimeYears instead of reading text out of a file.
//
// The "= 3", "= SEK" and similar values are DEFAULTS. They are used if
// appsettings.json doesn't contain that setting.
// ============================================================================

namespace SmartAssetTracking.Helpers;

/// <summary>Lifecycle rules (section "Lifecycle" in appsettings.json).</summary>
public class LifecycleOptions
{
    public int LifetimeYears { get; set; } = 3;

    /// <summary>Less than this many months remaining => YELLOW.</summary>
    public int YellowThresholdMonths { get; set; } = 3;

    /// <summary>Less than this many months remaining => RED.</summary>
    public int RedThresholdMonths { get; set; } = 6;

    /// <summary>Days ahead considered "maintenance due soon".</summary>
    public int MaintenanceDueSoonDays { get; set; } = 30;
}

/// <summary>Currency configuration (section "Currency" in appsettings.json).</summary>
public class CurrencyOptions
{
    /// <summary>Currency used for company-wide totals on the dashboard.</summary>
    public string ReportingCurrency { get; set; } = "SEK";

    /// <summary>Endpoint returning { "rates": { "SEK": 10.5, ... } } with USD as base.</summary>
    public string LiveRatesUrl { get; set; } = "https://open.er-api.com/v6/latest/USD";

    // The list of currencies from the "Currencies" array in appsettings.json.
    public List<CurrencyInfo> Currencies { get; set; } = [];
}

/// <summary>A single supported currency with a fixed USD conversion rate.</summary>
public class CurrencyInfo
{
    public string Code { get; set; } = "USD";
    public string Symbol { get; set; } = "$";

    /// <summary>Culture used for number formatting and symbol placement, e.g. "sv-SE".</summary>
    // A culture name is "language-COUNTRY": sv-SE = Swedish in Sweden, de-DE = German in Germany.
    public string Culture { get; set; } = "en-US";

    /// <summary>How many units of this currency one USD buys.</summary>
    public decimal RateFromUsd { get; set; } = 1m;
}

/// <summary>General UI / export settings (section "App" in appsettings.json).</summary>
public class AppSettings
{
    public int PageSize { get; set; } = 12;               // rows per page in asset lists
    public string ExportFolder { get; set; } = "exports"; // where TXT/CSV/JSON files are saved
    public string LogFolder { get; set; } = "logs";       // where the log files are written
    public int MaxLoginAttempts { get; set; } = 3;        // wrong passwords allowed before asking "try again?"
}
