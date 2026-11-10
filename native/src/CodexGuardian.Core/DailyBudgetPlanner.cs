namespace CodexGuardian.Core;

/// <summary>Relative planned effort, not a measured usage multiplier.</summary>
public sealed record PlannedWorkDay(DateOnly Date, double Weight, int MinimumPercent = 0);
public sealed record DailyQuotaAllocation(DateOnly Date, double Percent, double Weight);
public sealed record DailyQuotaPlan(IReadOnlyList<DailyQuotaAllocation> Days, double AvailablePercent, string? Error)
{
    public bool IsValid => Error is null;
}

public static class DailyBudgetPlanner
{
    public static DailyQuotaPlan Calculate(QuotaSnapshot? snapshot, IReadOnlyList<PlannedWorkDay> days,
        int reservePercent, DateTimeOffset now, TimeZoneInfo timeZone)
    {
        if (reservePercent is < 0 or > 100 || days.Count is < 1 or > 31)
            return new([], 0, "invalid-plan");
        if (snapshot?.Weekly is not { } weekly || weekly.UsedPercent is < 0 or > 100 ||
            snapshot.GetWeeklyFreshness(now) != DataFreshness.Fresh || weekly.ResetsAt <= now)
            return new([], 0, "quota-unavailable");
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(now, timeZone).DateTime);
        var resetDay = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(weekly.ResetsAt.AddTicks(-1), timeZone).DateTime);
        if (days.Select(day => day.Date).Distinct().Count() != days.Count ||
            days.Any(day => day.Date < today || day.Date > resetDay ||
                !double.IsFinite(day.Weight) || day.Weight <= 0 || day.Weight > 10 ||
                day.MinimumPercent is < 0 or > 100))
            return new([], 0, "invalid-days");
        var available = Math.Max(0, weekly.RemainingPercent - reservePercent);
        var minimum = days.Sum(day => day.MinimumPercent);
        if (minimum > available) return new([], available, "minimum-exceeds-quota");
        var weight = days.Sum(day => day.Weight);
        var flexible = available - minimum;
        return new(days.OrderBy(day => day.Date).Select(day => new DailyQuotaAllocation(
            day.Date, day.MinimumPercent + flexible * day.Weight / weight, day.Weight)).ToArray(), available, null);
    }
}
