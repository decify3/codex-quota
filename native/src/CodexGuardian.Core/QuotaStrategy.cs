namespace CodexGuardian.Core;

public enum QuotaStrategyMode
{
    Sprint,
    Balanced,
    Conservative,
}

public sealed record QuotaStrategyPolicy(
    QuotaStrategyMode Mode,
    int FiveHourReservePercent,
    int WeeklyReservePercent)
{
    public static QuotaStrategyPolicy For(QuotaStrategyMode mode) => mode switch
    {
        QuotaStrategyMode.Sprint => new(mode, 5, 10),
        QuotaStrategyMode.Balanced => new(mode, 15, 25),
        QuotaStrategyMode.Conservative => new(mode, 30, 40),
        _ => throw new ArgumentOutOfRangeException(nameof(mode)),
    };
}

public sealed record QuotaStrategyAssessment(
    bool IsBelowReserve,
    bool IsProjectedBelowReserve,
    string Explanation);

public static class QuotaStrategyAdvisor
{
    public static QuotaStrategyAssessment Evaluate(
        QuotaSnapshot snapshot,
        BurnRatePrediction prediction,
        QuotaStrategyPolicy policy,
        DateTimeOffset now)
    {
        var fiveHour = snapshot.FiveHour;
        var weekly = snapshot.Weekly;
        var belowNow = fiveHour is not null && fiveHour.RemainingPercent < policy.FiveHourReservePercent ||
                       weekly is not null && weekly.RemainingPercent < policy.WeeklyReservePercent;
        if (belowNow)
        {
            return new(true, false,
                $"Below the {policy.Mode.ToString().ToLowerInvariant()} reserve: keep at least {policy.FiveHourReservePercent}% (5h) and {policy.WeeklyReservePercent}% (weekly).");
        }

        if (fiveHour is null)
            return new(false, false, "The five-hour window is unavailable; weekly reserve remains visible without a forward five-hour assessment.");

        var hoursUntilReset = Math.Max(0, (fiveHour.ResetsAt - now).TotalHours);
        var projected = fiveHour.RemainingPercent - prediction.PercentPerHour * hoursUntilReset;
        var trustedProjection = prediction.Confidence >= PredictionConfidence.Medium;
        if (trustedProjection && projected < policy.FiveHourReservePercent)
        {
            return new(false, true,
                $"Current pace is projected to cross the {policy.FiveHourReservePercent}% reserve before reset; estimate is based on {prediction.SampleCount} local samples.");
        }

        return new(false, false,
            trustedProjection
                ? $"Current pace stays above the {policy.Mode.ToString().ToLowerInvariant()} reserve before reset."
                : "Reserve is healthy now; more local history is needed for a forward assessment.");
    }
}
