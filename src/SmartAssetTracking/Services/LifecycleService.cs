using SmartAssetTracking.Helpers;
using SmartAssetTracking.Models;

namespace SmartAssetTracking.Services;

public interface ILifecycleService
{
    DateTime Today { get; }
    DateTime GetEndOfLife(Asset asset);
    int GetRemainingDays(Asset asset);
    LifecycleStatus GetStatus(Asset asset);
    string DescribeRemaining(Asset asset);
    bool IsExpired(Asset asset);
    bool IsExpiringSoon(Asset asset);

    /// <summary>Assets purchased on or before this date have reached end of life.</summary>
    DateTime ExpiredPurchaseCutoff { get; }
}

/// <summary>
/// Calculates remaining lifetime and status according to the project rules:
/// lifetime = 3 years, &lt; 3 months left = YELLOW, &lt; 6 months left = RED, otherwise NORMAL.
/// Thresholds are configurable in appsettings.json.
/// </summary>
public class LifecycleService(LifecycleOptions options, TimeProvider timeProvider) : ILifecycleService
{
    public DateTime Today => timeProvider.GetLocalNow().Date;

    public DateTime ExpiredPurchaseCutoff => Today.AddYears(-options.LifetimeYears);

    public DateTime GetEndOfLife(Asset asset) => asset.PurchaseDate.Date.AddYears(options.LifetimeYears);

    public int GetRemainingDays(Asset asset) => (GetEndOfLife(asset) - Today).Days;

    public LifecycleStatus GetStatus(Asset asset)
    {
        var endOfLife = GetEndOfLife(asset);
        var today = Today;

        if (endOfLife <= today)
            return LifecycleStatus.Expired;

        // Check the smaller threshold first so the most specific rule wins.
        var thresholds = new[]
            {
                (Months: options.YellowThresholdMonths, Status: LifecycleStatus.Yellow),
                (Months: options.RedThresholdMonths, Status: LifecycleStatus.Red)
            }
            .OrderBy(t => t.Months);

        foreach (var (months, status) in thresholds)
        {
            if (endOfLife < today.AddMonths(months))
                return status;
        }

        return LifecycleStatus.Normal;
    }

    public bool IsExpired(Asset asset) => GetStatus(asset) == LifecycleStatus.Expired;

    public bool IsExpiringSoon(Asset asset) => GetStatus(asset) is LifecycleStatus.Yellow or LifecycleStatus.Red;

    public string DescribeRemaining(Asset asset)
    {
        var endOfLife = GetEndOfLife(asset);
        var today = Today;
        if (endOfLife <= today)
            return $"expired {(today - endOfLife).Days}d ago";

        var months = (endOfLife.Year - today.Year) * 12 + endOfLife.Month - today.Month;
        if (today.AddMonths(months) > endOfLife)
            months--;
        var days = (endOfLife - today.AddMonths(months)).Days;

        var years = months / 12;
        months %= 12;
        return years > 0 ? $"{years}y {months}m" : months > 0 ? $"{months}m {days}d" : $"{days}d";
    }
}
