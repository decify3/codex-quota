namespace CodexGuardian.Core;

public sealed record WeeklyQuotaReport(
    DateTimeOffset PeriodStart,
    DateTimeOffset PeriodEnd,
    int SampleCount,
    int WeeklyConsumedPercent,
    int FiveHourWindowCount,
    double PeakFiveHourBurnPercentPerHour,
    string Summary);

public static class WeeklyQuotaReporter
{
    public static WeeklyQuotaReport Generate(
        IReadOnlyList<QuotaHistoryEntry> history,
        DateTimeOffset periodEnd)
    {
        var periodStart = periodEnd.AddDays(-7);
        var ordered = history
            .Where(entry => entry.CapturedAt >= periodStart && entry.CapturedAt <= periodEnd)
            .OrderBy(entry => entry.CapturedAt)
            .ToArray();
        if (ordered.Length == 0)
            return new(periodStart, periodEnd, 0, 0, 0, 0, "No local quota samples were recorded during this period.");

        var weeklyConsumed = 0;
        var peakRate = 0d;
        foreach (var group in ordered
                     .Where(entry => entry.WeeklyRemainingPercent is not null && entry.WeeklyResetsAt is not null)
                     .GroupBy(entry => entry.WeeklyResetsAt))
        {
            var points = group.OrderBy(entry => entry.WeeklyObservedAt).ToArray();
            if (points.Length >= 2)
                weeklyConsumed += Math.Max(0, points[0].WeeklyRemainingPercent!.Value - points[^1].WeeklyRemainingPercent!.Value);
        }

        var fiveHourGroups = ordered
            .Where(entry => entry.FiveHourRemainingPercent is not null && entry.FiveHourResetsAt is not null)
            .GroupBy(entry => entry.FiveHourResetsAt)
            .ToArray();
        foreach (var group in fiveHourGroups)
        {
            var points = group.OrderBy(entry => entry.FiveHourObservedAt).ToArray();
            for (var index = 1; index < points.Length; index++)
            {
                var hours = (points[index].FiveHourObservedAt!.Value - points[index - 1].FiveHourObservedAt!.Value).TotalHours;
                var consumed = points[index - 1].FiveHourRemainingPercent!.Value - points[index].FiveHourRemainingPercent!.Value;
                if (hours >= 1d / 6d && consumed > 0)
                    peakRate = Math.Max(peakRate, consumed / hours);
            }
        }

        return new(periodStart, periodEnd, ordered.Length, weeklyConsumed, fiveHourGroups.Length, peakRate,
            $"{weeklyConsumed}% weekly quota used across {fiveHourGroups.Length} observed five-hour windows; peak observed pace {peakRate:F1}% per hour.");
    }
}
