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
public class CurrencyService : ICurrencyService
{
    private readonly Dictionary<string, CurrencyInfo> _currencies;
    private readonly CurrencyOptions _options;
    private readonly HttpClient _httpClient;
    private readonly ILogger<CurrencyService> _logger;

    public CurrencyService(CurrencyOptions options, HttpClient httpClient, ILogger<CurrencyService> logger)
    {
        _options = options;
        _httpClient = httpClient;
        _logger = logger;
        _currencies = options.Currencies.ToDictionary(c => c.Code.ToUpperInvariant(), StringComparer.OrdinalIgnoreCase);

        if (!_currencies.ContainsKey("USD"))
            _currencies["USD"] = new CurrencyInfo { Code = "USD", Symbol = "$", Culture = "en-US", RateFromUsd = 1m };

        if (!_currencies.ContainsKey(options.ReportingCurrency))
            throw new InvalidOperationException($"Reporting currency '{options.ReportingCurrency}' is not configured.");
    }

    public IReadOnlyCollection<CurrencyInfo> Currencies => _currencies.Values;
    public string ReportingCurrency => _options.ReportingCurrency.ToUpperInvariant();
    public DateTimeOffset? LastLiveUpdate { get; private set; }

    public bool IsSupported(string currencyCode) => _currencies.ContainsKey(currencyCode);

    public CurrencyInfo Get(string currencyCode) =>
        _currencies.TryGetValue(currencyCode, out var info)
            ? info
            : throw new NotFoundException($"Currency '{currencyCode}' is not supported.");

    public decimal ConvertFromUsd(decimal amountUsd, string currencyCode) =>
        Math.Round(amountUsd * Get(currencyCode).RateFromUsd, 2, MidpointRounding.AwayFromZero);

    public string Format(decimal amount, string currencyCode)
    {
        var info = Get(currencyCode);
        CultureInfo culture;
        try
        {
            culture = CultureInfo.GetCultureInfo(info.Culture);
        }
        catch (CultureNotFoundException)
        {
            culture = CultureInfo.InvariantCulture;
        }

        // Use the culture's layout (symbol position, separators) but force the configured symbol.
        var format = (NumberFormatInfo)culture.NumberFormat.Clone();
        format.CurrencySymbol = info.Symbol;
        return amount.ToString("C2", format);
    }

    public decimal GetLocalPrice(Asset asset) => ConvertFromUsd(asset.PurchasePriceUsd, asset.Office.CurrencyCode);

    public string FormatLocalPrice(Asset asset) => Format(GetLocalPrice(asset), asset.Office.CurrencyCode);

    public async Task<int> RefreshRatesFromApiAsync(CancellationToken ct = default)
    {
        var response = await _httpClient.GetFromJsonAsync<RatesResponse>(_options.LiveRatesUrl, ct)
                       ?? throw new InvalidOperationException("The exchange-rate API returned no data.");

        if (response.Rates is null || response.Rates.Count == 0)
            throw new InvalidOperationException("The exchange-rate API returned no rates.");

        var updated = 0;
        foreach (var currency in _currencies.Values)
        {
            if (response.Rates.TryGetValue(currency.Code, out var rate) && rate > 0)
            {
                currency.RateFromUsd = rate;
                updated++;
            }
        }

        LastLiveUpdate = DateTimeOffset.Now;
        _logger.LogInformation("Updated {Count} exchange rates from {Url}", updated, _options.LiveRatesUrl);
        return updated;
    }

    private sealed class RatesResponse
    {
        [JsonPropertyName("rates")]
        public Dictionary<string, decimal>? Rates { get; set; }
    }
}
