using SmartAssetTracking.Helpers;
using SmartAssetTracking.Models;
using SmartAssetTracking.Services;

namespace SmartAssetTracking.Tests;

public class LifecycleServiceTests
{
    private static readonly DateTime Today = new(2026, 6, 15);
    private readonly LifecycleService _sut = new(new LifecycleOptions(), new FixedTimeProvider(Today));

    private static Laptop PurchasedOn(DateTime date) => new("Lenovo", "ThinkPad", "SN", date, 1000m, 1);

    [Theory]
    [InlineData(-12, LifecycleStatus.Normal)]   // 2 years left
    [InlineData(-30, LifecycleStatus.Normal)]   // exactly 6 months left
    [InlineData(-31, LifecycleStatus.Red)]      // 5 months left -> less than 6 months
    [InlineData(-33, LifecycleStatus.Yellow)]   // exactly 3 months left -> not "less than 3"... see below
    [InlineData(-34, LifecycleStatus.Yellow)]   // 2 months left -> less than 3 months
    [InlineData(-36, LifecycleStatus.Expired)]  // end of life today
    [InlineData(-48, LifecycleStatus.Expired)]
    public void GetStatus_follows_project_rules(int purchasedMonthsAgo, LifecycleStatus expected)
    {
        var asset = PurchasedOn(Today.AddMonths(purchasedMonthsAgo).AddDays(purchasedMonthsAgo == -33 ? -1 : 0));

        Assert.Equal(expected, _sut.GetStatus(asset));
    }

    [Fact]
    public void End_of_life_is_three_years_after_purchase()
    {
        var asset = PurchasedOn(new DateTime(2024, 2, 11));

        Assert.Equal(new DateTime(2027, 2, 11), _sut.GetEndOfLife(asset));
    }

    [Fact]
    public void Expired_cutoff_matches_status()
    {
        var onCutoff = PurchasedOn(_sut.ExpiredPurchaseCutoff);
        var dayAfter = PurchasedOn(_sut.ExpiredPurchaseCutoff.AddDays(1));

        Assert.True(_sut.IsExpired(onCutoff));
        Assert.False(_sut.IsExpired(dayAfter));
        Assert.True(_sut.IsExpiringSoon(dayAfter));
    }

    [Fact]
    public void DescribeRemaining_is_human_readable()
    {
        Assert.Equal("2y 0m", _sut.DescribeRemaining(PurchasedOn(Today.AddYears(-1))));
        Assert.Equal("2m 0d", _sut.DescribeRemaining(PurchasedOn(Today.AddMonths(-34))));
        Assert.StartsWith("expired", _sut.DescribeRemaining(PurchasedOn(Today.AddYears(-4))));
    }

    [Fact]
    public void Thresholds_are_configurable()
    {
        var sut = new LifecycleService(new LifecycleOptions { LifetimeYears = 2, YellowThresholdMonths = 1, RedThresholdMonths = 2 },
            new FixedTimeProvider(Today));

        Assert.Equal(LifecycleStatus.Normal, sut.GetStatus(PurchasedOn(Today.AddMonths(-21))));
        Assert.Equal(LifecycleStatus.Red, sut.GetStatus(PurchasedOn(Today.AddMonths(-22).AddDays(-10))));
        Assert.Equal(LifecycleStatus.Yellow, sut.GetStatus(PurchasedOn(Today.AddMonths(-23).AddDays(-10))));
    }
}
