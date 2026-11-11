// ============================================================================
// CurrencyServiceTests.cs - tests for USD -> local currency conversion (Level 3)
// ----------------------------------------------------------------------------
// The test settings use simple rates (1 USD = 10 SEK, 0.9 EUR, 40 TRY),
// so the expected results are easy to check in your head.
// "await using var host = ..." creates a fresh test application and
// automatically cleans it up when the test ends.
// ============================================================================

using SmartAssetTracking.Models;
using SmartAssetTracking.Services;

namespace SmartAssetTracking.Tests;

public class CurrencyServiceTests
{
    [Fact]
    public async Task Converts_usd_to_local_currency()
    {
        await using var host = await TestHost.CreateAsync();
        var sut = host.Get<ICurrencyService>();

        Assert.Equal(15500m, sut.ConvertFromUsd(1550m, "SEK"));
        Assert.Equal(900m, sut.ConvertFromUsd(1000m, "EUR"));
        Assert.Equal(1200m, sut.ConvertFromUsd(1200m, "USD"));
        Assert.Equal(33.33m, sut.ConvertFromUsd(3.3333m, "SEK")); // rounded to 2 decimals
    }

    [Fact]
    public async Task Formats_with_correct_currency_symbols()
    {
        await using var host = await TestHost.CreateAsync();
        var sut = host.Get<ICurrencyService>();

        Assert.Equal("$1,200.00", sut.Format(1200m, "USD"));
        Assert.Contains("kr", sut.Format(15500m, "SEK"));
        Assert.Contains("€", sut.Format(900m, "EUR"));
        Assert.Contains("₺", sut.Format(40m, "TRY"));
    }

    [Fact]
    public async Task Local_price_uses_the_asset_office_currency()
    {
        await using var host = await TestHost.CreateAsync();
        var sut = host.Get<ICurrencyService>();
        var asset = new Laptop("Lenovo", "X1", "SN1", new DateTime(2025, 1, 1), 1000m, 1)
        {
            Office = new Office { Id = 1, Name = "Sweden Office", Country = "Sweden", CurrencyCode = "SEK" }
        };

        Assert.Equal(10000m, sut.GetLocalPrice(asset));
    }

    [Fact]
    public async Task Unknown_currency_throws()
    {
        await using var host = await TestHost.CreateAsync();

        Assert.Throws<NotFoundException>(() => host.Get<ICurrencyService>().ConvertFromUsd(1m, "XYZ"));
    }
}
