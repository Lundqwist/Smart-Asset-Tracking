// ============================================================================
// ReportAndExportTests.cs - tests for reports, the dashboard and file export
// ----------------------------------------------------------------------------
// "seed: true" fills the test database with the same 20 demo assets as the
// real program, so the reports have realistic data to work on.
// The export tests write real TXT/CSV/JSON files to a temporary folder and
// check their contents.
// ============================================================================

using SmartAssetTracking.Reports;
using SmartAssetTracking.Services;

namespace SmartAssetTracking.Tests;

public class ReportAndExportTests
{
    [Fact]
    public async Task Office_summaries_count_and_total_assets()
    {
        await using var host = await TestHost.CreateAsync(seed: true);
        var all = await host.Get<IAssetService>().GetAllSortedAsync();

        var summaries = await host.Get<IReportService>().GetOfficeSummariesAsync();

        Assert.Equal(4, summaries.Count);
        Assert.Equal(all.Count, summaries.Sum(s => s.AssetCount));
        var sweden = summaries.Single(s => s.Office == "Sweden Office");
        Assert.Equal(sweden.TotalValueUsd * 10m, sweden.TotalValueLocal); // SEK rate = 10 in tests
    }

    [Fact]
    public async Task Dashboard_contains_key_figures()
    {
        await using var host = await TestHost.CreateAsync(seed: true);

        var stats = await host.Get<IReportService>().GetDashboardAsync();

        Assert.Equal(20, stats.TotalAssets);
        Assert.Equal(5, stats.TotalEmployees);
        Assert.True(stats.ExpiringSoon > 0);
        Assert.True(stats.Expired > 0);
        Assert.Equal("SEK", stats.ReportingCurrency);
        Assert.False(string.IsNullOrEmpty(stats.TopAssetType));
        Assert.False(string.IsNullOrEmpty(stats.MostValuableOffice));
    }

    [Fact]
    public async Task Most_expensive_is_sorted_descending()
    {
        await using var host = await TestHost.CreateAsync(seed: true);

        var top = await host.Get<IReportService>().GetMostExpensiveAsync(5);

        Assert.Equal(5, top.Count);
        Assert.Equal(top.OrderByDescending(a => a.PurchasePriceUsd).Select(a => a.Id), top.Select(a => a.Id));
    }

    [Theory]
    [InlineData(ExportFormat.Csv)]
    [InlineData(ExportFormat.Json)]
    [InlineData(ExportFormat.Txt)]
    public async Task Export_writes_file_in_each_format(ExportFormat format)
    {
        await using var host = await TestHost.CreateAsync(seed: true);
        var assets = await host.Get<IAssetService>().GetAllSortedAsync();

        var path = await host.Get<IExportService>().ExportAssetsAsync(assets, format, "All assets");

        Assert.True(File.Exists(path));
        var content = await File.ReadAllTextAsync(path);
        Assert.Contains("ThinkPad X1", content);
        if (format == ExportFormat.Csv)
            Assert.StartsWith("Id,Type,Category,Brand,Model", content.TrimStart('﻿'));
    }

    [Theory]
    [InlineData("plain", "plain")]
    [InlineData("a,b", "\"a,b\"")]
    [InlineData("say \"hi\"", "\"say \"\"hi\"\"\"")]
    public void Csv_values_are_escaped(string input, string expected) =>
        Assert.Equal(expected, ExportService.EscapeCsv(input));
}
