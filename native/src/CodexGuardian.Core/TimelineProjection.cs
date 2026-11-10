namespace CodexGuardian.Core;

public enum UsageScenario
{
    Paused,
    Light,
    Current,
    High,
}

public sealed record TimelinePoint(
    DateTimeOffset Time,
    double RemainingPercent,
    bool IsForecast,
    double UncertaintyPercent = 0);

public static class QuotaTimeline
{
    public static IReadOnlyList<TimelinePoint> Project(
        QuotaSnapshot snapshot,
        BurnRatePrediction prediction,
        UsageScenario scenario,
        DateTimeOffset now,
        TimeSpan? step = null)
    {
        var interval = step ?? TimeSpan.FromMinutes(30);
        if (interval <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(step));
        if (snapshot.FiveHour is not { } fiveHour) return [];

        var multiplier = scenario switch
        {
            UsageScenario.Paused => 0,
            UsageScenario.Light => 0.5,
            UsageScenario.Current => 1,
            UsageScenario.High => 1.75,
            _ => 1,
        };
        var rate = prediction.PercentPerHour * multiplier;
        var points = new List<TimelinePoint>
        {
            new(now, fiveHour.RemainingPercent, true),
        };
        if (prediction.Confidence == PredictionConfidence.None) return points;

        for (var time = now + interval; time < fiveHour.ResetsAt; time += interval)
        {
            var hours = (time - now).TotalHours;
            var consumed = rate * hours;
            points.Add(new(
                time,
                Math.Max(0, fiveHour.RemainingPercent - consumed),
                true,
                CalculateUncertainty(consumed, prediction.Confidence)));
        }

        if (fiveHour.ResetsAt > now)
        {
            var hours = (fiveHour.ResetsAt - now).TotalHours;
            points.Add(new(
                fiveHour.ResetsAt,
                Math.Max(0, fiveHour.RemainingPercent - rate * hours),
                true,
                CalculateUncertainty(rate * hours, prediction.Confidence)));
        }

        return points;
    }

    private static double CalculateUncertainty(double consumedPercent, PredictionConfidence confidence)
    {
        var factor = confidence switch
        {
            PredictionConfidence.High => 0.15,
            PredictionConfidence.Medium => 0.3,
            _ => 0.55,
        };
        return Math.Min(35, 2 + consumedPercent * factor);
    }
}
