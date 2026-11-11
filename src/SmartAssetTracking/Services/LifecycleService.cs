// ============================================================================
// LifecycleService.cs
// ----------------------------------------------------------------------------
// Works out how much "life" an asset has left and which colour status it gets.
// The rules come from the assignment:
//
//     Lifetime                = 3 years after the purchase date
//     less than 3 months left = YELLOW
//     less than 6 months left = RED
//     more than that          = NORMAL
//     lifetime already over   = EXPIRED (our addition, for "show expired assets")
//
// The numbers are read from appsettings.json (the "Lifecycle" section), so
// they can be changed without changing the code.
// ============================================================================

using SmartAssetTracking.Helpers;
using SmartAssetTracking.Models;

namespace SmartAssetTracking.Services;

// The interface lists what the service offers. Other classes depend on
// ILifecycleService instead of the class itself, which makes testing easier.
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
// We get "today" from a TimeProvider instead of calling DateTime.Now directly.
// In the real program it gives the real date, but the unit tests can pass in a
// FIXED date. That way the tests give the same result every day.
public class LifecycleService(LifecycleOptions options, TimeProvider timeProvider) : ILifecycleService
{
    public DateTime Today => timeProvider.GetLocalNow().Date;

    // Anything bought 3 or more years before today is expired.
    // Example: today 2026-11-11 gives a cutoff of 2023-11-11.
    public DateTime ExpiredPurchaseCutoff => Today.AddYears(-options.LifetimeYears);

    // The day the asset reaches the end of its life: purchase date + 3 years.
    public DateTime GetEndOfLife(Asset asset) => asset.PurchaseDate.Date.AddYears(options.LifetimeYears);

    // Subtracting two dates gives a TimeSpan. .Days is the number of whole days.
    // A negative number means the asset is already past its end of life.
    public int GetRemainingDays(Asset asset) => (GetEndOfLife(asset) - Today).Days;

    public LifecycleStatus GetStatus(Asset asset)
    {
        var endOfLife = GetEndOfLife(asset);
        var today = Today;

        // Lifetime is over.
        if (endOfLife <= today)
            return LifecycleStatus.Expired;

        // Check the smaller threshold first so the most specific rule wins.
        // We put the two rules in an array of "tuples" (small groups of values,
        // here a number of months and a status), then sort them by months.
        // With the default settings the order is: 3 months -> YELLOW, then 6 months -> RED.
        var thresholds = new[]
            {
                (Months: options.YellowThresholdMonths, Status: LifecycleStatus.Yellow),
                (Months: options.RedThresholdMonths, Status: LifecycleStatus.Red)
            }
            .OrderBy(t => t.Months);

        // "foreach" loops over every item. "var (months, status)" splits each tuple
        // into two variables (called "deconstruction").
        foreach (var (months, status) in thresholds)
        {
            // Does the end of life come before (today + N months)? Then less than N months are left.
            if (endOfLife < today.AddMonths(months))
                return status;
        }

        // More life left than both thresholds.
        return LifecycleStatus.Normal;
    }

    public bool IsExpired(Asset asset) => GetStatus(asset) == LifecycleStatus.Expired;

    // "is A or B" is a pattern that is true if the status is either Yellow or Red.
    public bool IsExpiringSoon(Asset asset) => GetStatus(asset) is LifecycleStatus.Yellow or LifecycleStatus.Red;

    // Turns the remaining time into short text such as "1y 4m", "2m 5d", "12d" or "expired 30d ago".
    public string DescribeRemaining(Asset asset)
    {
        var endOfLife = GetEndOfLife(asset);
        var today = Today;
        if (endOfLife <= today)
            return $"expired {(today - endOfLife).Days}d ago";

        // Count the whole months between today and the end of life...
        var months = (endOfLife.Year - today.Year) * 12 + endOfLife.Month - today.Month;
        if (today.AddMonths(months) > endOfLife)
            months--; // ...and step back one if we went past the end date
        // ...then the leftover days.
        var days = (endOfLife - today.AddMonths(months)).Days;

        var years = months / 12;  // integer division: 17 / 12 = 1
        months %= 12;             // remainder ("modulo"): 17 % 12 = 5
        return years > 0 ? $"{years}y {months}m" : months > 0 ? $"{months}m {days}d" : $"{days}d";
    }
}
