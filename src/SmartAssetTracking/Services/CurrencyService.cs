// ============================================================================
// CurrencyService.cs
// ----------------------------------------------------------------------------
// Level 3: "Assets are entered in USD. Convert prices into local currency,
// display currency symbols correctly, use exchange rates."
//
// The rates are read from appsettings.json ("Currency" section). For example,
// "RateFromUsd": 9.50 for SEK means 1 USD = 9.50 SEK, so a laptop costing
// 1000 USD is shown as 9 500,00 kr in the Sweden office.
//
// An Admin can also download today's real rates from a free online API. Those
// rates are only kept in memory until the program closes.
// ============================================================================

using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using SmartAssetTracking.Helpers;
using SmartAssetTracking.Models;

namespace SmartAssetTracking.Services;

public interface ICurrencyService
{
    IReadOnlyCollection<CurrencyInfo> Currencies { get; }
    string ReportingCurrency { get; }
    DateTimeOffset? LastLiveUpdate { get; }
    bool IsSupported(string currencyCode);
    CurrencyInfo Get(string currencyCode);
    decimal ConvertFromUsd(decimal amountUsd, string currencyCode);
    string Format(decimal amount, string currencyCode);

    /// <summary>Converts the asset's USD price to the currency of its office.</summary>
    decimal GetLocalPrice(Asset asset);
    string FormatLocalPrice(Asset asset);

    /// <summary>Downloads current rates from the configured API. Returns the number of rates updated.</summary>
    Task<int> RefreshRatesFromApiAsync(CancellationToken ct = default);
}

/// <summary>
/// Converts USD prices into local currencies. Uses fixed rates from appsettings.json by default,
/// and can optionally refresh them from a public exchange-rate API.
/// </summary>
// This class uses a classic constructor (not a primary constructor) because it
// needs to do some setup work when it is created.
public class CurrencyService : ICurrencyService
{
    // "private readonly" fields: only this class can see them, and they are set once
    // in the constructor. By convention, private fields start with an underscore.
    // A Dictionary is a lookup table: give it a key ("SEK") and it returns the value (CurrencyInfo).
    private readonly Dictionary<string, CurrencyInfo> _currencies;
    private readonly CurrencyOptions _options;
    private readonly HttpClient _httpClient;   // used to call the online exchange-rate API
    private readonly ILogger<CurrencyService> _logger;

    public CurrencyService(CurrencyOptions options, HttpClient httpClient, ILogger<CurrencyService> logger)
    {
        _options = options;
        _httpClient = httpClient;
        _logger = logger;

        // Build the dictionary from the list in appsettings.json.
        // StringComparer.OrdinalIgnoreCase makes "sek" and "SEK" find the same entry.
        _currencies = options.Currencies.ToDictionary(c => c.Code.ToUpperInvariant(), StringComparer.OrdinalIgnoreCase);

        // USD must always exist, because all prices are stored in USD.
        if (!_currencies.ContainsKey("USD"))
            _currencies["USD"] = new CurrencyInfo { Code = "USD", Symbol = "$", Culture = "en-US", RateFromUsd = 1m };

        // Fail early with a clear message if the settings file is wrong.
        if (!_currencies.ContainsKey(options.ReportingCurrency))
            throw new InvalidOperationException($"Reporting currency '{options.ReportingCurrency}' is not configured.");
    }

    public IReadOnlyCollection<CurrencyInfo> Currencies => _currencies.Values;

    // The currency used for company-wide totals on the dashboard (SEK by default).
    public string ReportingCurrency => _options.ReportingCurrency.ToUpperInvariant();

    // When live rates were last downloaded (null = never, the fixed rates are in use).
    public DateTimeOffset? LastLiveUpdate { get; private set; }

    public bool IsSupported(string currencyCode) => _currencies.ContainsKey(currencyCode);

    // TryGetValue tries to find the key. If it succeeds, "out var info" receives the value.
    public CurrencyInfo Get(string currencyCode) =>
        _currencies.TryGetValue(currencyCode, out var info)
            ? info
            : throw new NotFoundException($"Currency '{currencyCode}' is not supported.");

    // The actual conversion: USD amount × rate, rounded to 2 decimals.
    // MidpointRounding.AwayFromZero means 0.125 rounds to 0.13 (the way people
    // usually round money).
    public decimal ConvertFromUsd(decimal amountUsd, string currencyCode) =>
        Math.Round(amountUsd * Get(currencyCode).RateFromUsd, 2, MidpointRounding.AwayFromZero);

    // Turns a number into a money string with the correct symbol and layout:
    //   USD -> $1,200.00      SEK -> 11 400,00 kr      EUR -> 1.032,00 €      TRY -> ₺50.400,00
    // Each country writes money differently (symbol before or after the number,
    // comma or dot for decimals). A "CultureInfo" holds those rules for a country.
    public string Format(decimal amount, string currencyCode)
    {
        var info = Get(currencyCode);
        CultureInfo culture;

        // try/catch: if the culture name in appsettings.json is wrong, we don't crash.
        // We fall back to a neutral format instead.
        try
        {
            culture = CultureInfo.GetCultureInfo(info.Culture);
        }
        catch (CultureNotFoundException)
        {
            culture = CultureInfo.InvariantCulture;
        }

        // Use the culture's layout (symbol position, separators) but force the configured symbol.
        // Clone() makes a copy, because the original culture settings can't be changed.
        var format = (NumberFormatInfo)culture.NumberFormat.Clone();
        format.CurrencySymbol = info.Symbol;

        // "C2" = currency format with 2 decimals.
        return amount.ToString("C2", format);
    }

    // The office decides the currency: an asset in "Germany Office" is shown in EUR.
    public decimal GetLocalPrice(Asset asset) => ConvertFromUsd(asset.PurchasePriceUsd, asset.Office.CurrencyCode);

    public string FormatLocalPrice(Asset asset) => Format(GetLocalPrice(asset), asset.Office.CurrencyCode);

    // Downloads current exchange rates from the internet.
    public async Task<int> RefreshRatesFromApiAsync(CancellationToken ct = default)
    {
        // GetFromJsonAsync downloads the web page and converts the JSON text into a
        // RatesResponse object for us ("deserialization").
        var response = await _httpClient.GetFromJsonAsync<RatesResponse>(_options.LiveRatesUrl, ct)
                       ?? throw new InvalidOperationException("The exchange-rate API returned no data.");

        if (response.Rates is null || response.Rates.Count == 0)
            throw new InvalidOperationException("The exchange-rate API returned no rates.");

        // Only update the currencies we actually use (the API returns 150+ of them).
        var updated = 0;
        foreach (var currency in _currencies.Values)
        {
            if (response.Rates.TryGetValue(currency.Code, out var rate) && rate > 0)
            {
                currency.RateFromUsd = rate;
                updated++; // "++" adds 1
            }
        }

        LastLiveUpdate = DateTimeOffset.Now;
        _logger.LogInformation("Updated {Count} exchange rates from {Url}", updated, _options.LiveRatesUrl);
        return updated;
    }

    // A small private class that matches the shape of the API's JSON answer:
    //   { "rates": { "SEK": 9.5, "EUR": 0.86, ... } }
    // [JsonPropertyName("rates")] says the JSON field "rates" maps to this property.
    private sealed class RatesResponse
    {
        [JsonPropertyName("rates")]
        public Dictionary<string, decimal>? Rates { get; set; }
    }
}
