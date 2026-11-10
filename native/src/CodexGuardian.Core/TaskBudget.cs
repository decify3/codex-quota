namespace CodexGuardian.Core;

public sealed record TaskBudgetEstimate(
    int MinimumPercent,
    int MaximumPercent,
    PredictionConfidence Confidence,
    string Basis);

public sealed record TaskQuotaSummary(
    DateTimeOffset StartedAt,
    DateTimeOffset FinishedAt,
    int? FiveHourConsumedPercent,
    int? WeeklyConsumedPercent,
    string Explanation);

public static class TaskBudgetAdvisor
{
    public static TaskBudgetEstimate Estimate(
        BurnRatePrediction prediction,
        TimeSpan plannedDuration,
        QuotaStrategyPolicy policy)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(plannedDuration, TimeSpan.Zero);
        if (prediction.Confidence == PredictionConfidence.None || prediction.PercentPerHour <= 0)
            return new(0, 0, PredictionConfidence.None, "Not enough local history to estimate a task budget.");

        var expected = prediction.PercentPerHour * plannedDuration.TotalHours;
        var spread = prediction.Confidence switch
        {
            PredictionConfidence.High => 0.2,
            PredictionConfidence.Medium => 0.4,
            _ => 0.75,
        };
        var minimum = Math.Max(1, (int)Math.Floor(expected * (1 - spread)));
        var maximum = Math.Max(minimum, (int)Math.Ceiling(expected * (1 + spread)));
        return new(minimum, maximum, prediction.Confidence,
            $"Based on {prediction.SampleCount} local samples at {prediction.PercentPerHour:F1}% per hour; keep the {policy.FiveHourReservePercent}% reserve intact.");
    }

    public static TaskQuotaSummary Summarize(QuotaSnapshot before, QuotaSnapshot after)
    {
        if (after.CapturedAt < before.CapturedAt) throw new ArgumentException("The final snapshot must not predate the initial snapshot.", nameof(after));
        var sameScope = !string.IsNullOrWhiteSpace(before.ScopeKey) && before.ScopeKey == after.ScopeKey;
        var shortDelta = sameScope ? Delta(before.FiveHour, after.FiveHour,
            before.FiveHourObservedAt, after.FiveHourObservedAt, before.CapturedAt, after.CapturedAt) : null;
        var weeklyDelta = sameScope ? Delta(before.Weekly, after.Weekly,
            before.WeeklyObservedAt, after.WeeklyObservedAt, before.CapturedAt, after.CapturedAt) : null;
        return new(before.CapturedAt, after.CapturedAt, shortDelta, weeklyDelta,
            "Account-wide change between observations; concurrent work is included. Unknown deltas indicate missing identity, unavailable data, a reset, or a balance increase.");
    }

    private static int? Delta(QuotaWindow? before, QuotaWindow? after,
        DateTimeOffset? beforeObserved, DateTimeOffset? afterObserved,
        DateTimeOffset start, DateTimeOffset end)
    {
        if (before is null || after is null || beforeObserved is not { } first || afterObserved is not { } last ||
            last <= first || QuotaSnapshot.GetFreshness(first, start) != DataFreshness.Fresh ||
            QuotaSnapshot.GetFreshness(last, end) != DataFreshness.Fresh ||
            before.ResetsAt != after.ResetsAt || before.WindowMinutes != after.WindowMinutes ||
            before.ResetsAt <= end || before.UsedPercent is < 0 or > 100 || after.UsedPercent is < 0 or > 100 ||
            after.UsedPercent < before.UsedPercent) return null;
        return after.UsedPercent - before.UsedPercent;
    }
}
