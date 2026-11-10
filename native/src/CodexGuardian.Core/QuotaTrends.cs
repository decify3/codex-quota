namespace CodexGuardian.Core;

public sealed record QuotaTrendWindow(
    string Label,
    TimeSpan Window,
    int SampleCount,
    int ConsumedPercent,
    double PercentPerHour,
    TimeSpan ObservedDuration,
    PredictionConfidence Confidence);

public static class QuotaTrendAnalyzer
{
    private static readonly TimeSpan MaximumContinuousGap = TimeSpan.FromMinutes(30);

    public static IReadOnlyList<QuotaTrendWindow> Analyze(
        IReadOnlyList<QuotaHistoryEntry> history,
        DateTimeOffset now)
    {
        return
        [
            AnalyzeWindow(history, now, "30m", TimeSpan.FromMinutes(30), useWeeklyQuota: false),
            AnalyzeWindow(history, now, "2h", TimeSpan.FromHours(2), useWeeklyQuota: false),
            AnalyzeWindow(history, now, "5h", TimeSpan.FromHours(5), useWeeklyQuota: false),
            AnalyzeWindow(history, now, "7d", TimeSpan.FromDays(7), useWeeklyQuota: true),
        ];
    }

    private static QuotaTrendWindow AnalyzeWindow(
        IReadOnlyList<QuotaHistoryEntry> history,
        DateTimeOffset now,
        string label,
        TimeSpan window,
        bool useWeeklyQuota)
    {
        var points = history
            .Where(entry =>
            {
                var observedAt = useWeeklyQuota ? entry.WeeklyObservedAt : entry.FiveHourObservedAt;
                return observedAt >= now - window && observedAt <= now;
            })
            .Where(entry => useWeeklyQuota
                ? entry.WeeklyRemainingPercent is not null && entry.WeeklyResetsAt is not null
                : entry.FiveHourRemainingPercent is not null && entry.FiveHourResetsAt is not null)
            .OrderBy(entry => useWeeklyQuota ? entry.WeeklyObservedAt : entry.FiveHourObservedAt)
            .GroupBy(entry => useWeeklyQuota ? entry.WeeklyObservedAt : entry.FiveHourObservedAt)
            .Select(group => group.Last())
            .ToArray();

        var consumed = 0;
        var observed = TimeSpan.Zero;
        var contributingSamples = new HashSet<DateTimeOffset>();
        for (var index = 1; index < points.Length; index++)
        {
            var previous = points[index - 1];
            var current = points[index];
            var previousObservedAt = (useWeeklyQuota ? previous.WeeklyObservedAt : previous.FiveHourObservedAt)!.Value;
            var currentObservedAt = (useWeeklyQuota ? current.WeeklyObservedAt : current.FiveHourObservedAt)!.Value;
            var gap = currentObservedAt - previousObservedAt;
            if (gap <= TimeSpan.Zero || gap > MaximumContinuousGap) continue;

            var sameResetWindow = useWeeklyQuota
                ? previous.WeeklyResetsAt == current.WeeklyResetsAt
                : previous.FiveHourResetsAt == current.FiveHourResetsAt;
            if (!sameResetWindow) continue;

            var previousRemaining = useWeeklyQuota
                ? previous.WeeklyRemainingPercent!.Value
                : previous.FiveHourRemainingPercent!.Value;
            var currentRemaining = useWeeklyQuota
                ? current.WeeklyRemainingPercent!.Value
                : current.FiveHourRemainingPercent!.Value;
            if (currentRemaining > previousRemaining) continue;

            consumed += previousRemaining - currentRemaining;
            observed += gap;
            contributingSamples.Add(previousObservedAt);
            contributingSamples.Add(currentObservedAt);
        }

        var rate = observed > TimeSpan.Zero ? consumed / observed.TotalHours : 0;
        var confidence = observed >= TimeSpan.FromHours(2) && contributingSamples.Count >= 8
            ? PredictionConfidence.High
            : observed >= TimeSpan.FromMinutes(30) && contributingSamples.Count >= 5
                ? PredictionConfidence.Medium
                : observed >= TimeSpan.FromMinutes(10) && contributingSamples.Count >= 3
                    ? PredictionConfidence.Low
                    : PredictionConfidence.None;

        return new(label, window, contributingSamples.Count, consumed, rate, observed, confidence);
    }
}
