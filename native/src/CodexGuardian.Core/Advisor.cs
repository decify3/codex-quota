namespace CodexGuardian.Core;

public sealed record AdvisorContext(
    bool IsFullScreen,
    bool IsPresenting,
    bool IsDoNotDisturb,
    bool AlreadyNotified,
    bool IsCoding = false);

public sealed record Advice(
    NotificationLevel Level,
    string Reason,
    string? Message = null);

public static class QuotaAdvisor
{
    public static Advice Evaluate(
        QuotaSnapshot snapshot,
        BurnRatePrediction prediction,
        AdvisorContext context,
        DateTimeOffset now)
    {
        if (snapshot.FiveHour is not { } fiveHour ||
            snapshot.GetFiveHourFreshness(now) is DataFreshness.Expired or DataFreshness.Unavailable)
            return new(NotificationLevel.Silent, "The five-hour window is unavailable or too old for a decision.");

        if (context.IsDoNotDisturb || context.IsPresenting || context.IsFullScreen || context.AlreadyNotified)
            return new(NotificationLevel.Silent, "The current context suppresses interruptions.");

        var untilReset = fiveHour.ResetsAt - now;
        if (untilReset <= TimeSpan.FromMinutes(1))
            return new(NotificationLevel.Silent, "The quota resets before a reminder could help.");

        if (fiveHour.RemainingPercent <= 10 && untilReset > TimeSpan.FromMinutes(30))
        {
            var level = context.IsCoding ? NotificationLevel.Passive : NotificationLevel.Active;
            return new(level, "The five-hour quota is critically low with time left to act.",
                $"{fiveHour.RemainingPercent}% remaining; resets in {FormatDuration(untilReset)}.");
        }

        if (prediction.EstimatedExhaustionAt is { } exhaustion &&
            exhaustion < fiveHour.ResetsAt &&
            prediction.Confidence >= PredictionConfidence.Medium)
        {
            return new(NotificationLevel.Passive, "A trusted trend predicts exhaustion before reset.",
                $"At the current pace, quota may run out around {exhaustion:HH:mm}.");
        }

        if (fiveHour.RemainingPercent <= 25)
            return new(NotificationLevel.Ambient, "Quota is low, but no immediate decision is required.");

        return new(NotificationLevel.Silent, "Quota is healthy.");
    }

    private static string FormatDuration(TimeSpan duration) =>
        duration.TotalHours >= 1
            ? $"{(int)duration.TotalHours}h {duration.Minutes}m"
            : $"{Math.Max(1, duration.Minutes)}m";
}
