namespace CodexGuardian.Core;

public static class WeeklyBudget
{
    public static double? Calculate(QuotaSnapshot? snapshot, int workDays, int reserve, DateTimeOffset now)
    {
        if (workDays is < 1 or > 31 || reserve is < 0 or > 100) return null;
        if (snapshot?.Weekly is not { } weekly || weekly.UsedPercent is < 0 or > 100 ||
            weekly.ResetsAt <= now || snapshot.WeeklyObservedAt > now ||
            snapshot.GetWeeklyFreshness(now) != DataFreshness.Fresh) return null;
        // Inclusive dates: a user may plan work today and before reset on the final day.
        var availableDates = (weekly.ResetsAt.LocalDateTime.Date - now.LocalDateTime.Date).Days + 1;
        if (workDays > availableDates) return null;
        return Math.Max(0, weekly.RemainingPercent - reserve) / (double)workDays;
    }
}
