namespace CodexGuardian.Core;

public sealed record QuotaHistoryPoint(DateTimeOffset CapturedAt, int RemainingPercent);

public sealed record BurnRatePrediction(
    double PercentPerHour,
    DateTimeOffset? EstimatedExhaustionAt,
    PredictionConfidence Confidence,
    int SampleCount,
    DateTimeOffset? EstimatedExhaustionEarliest = null,
    DateTimeOffset? EstimatedExhaustionLatest = null);

public static class QuotaPredictor
{
    public static BurnRatePrediction Predict(
        IReadOnlyList<QuotaHistoryPoint> history,
        DateTimeOffset resetsAt)
    {
        if (history.Count < 3)
        {
            return new(0, null, PredictionConfidence.None, history.Count);
        }

        var ordered = history
            .OrderBy(point => point.CapturedAt)
            .GroupBy(point => point.CapturedAt)
            .Select(group => group.Last())
            .ToArray();
        var segmentStart = 0;
        for (var index = 1; index < ordered.Length; index++)
        {
            if (ordered[index].CapturedAt - ordered[index - 1].CapturedAt > TimeSpan.FromMinutes(30))
                segmentStart = index;
        }
        if (segmentStart > 0) ordered = ordered[segmentStart..];
        if (ordered.Length < 3)
            return new(0, null, PredictionConfidence.None, ordered.Length);
        var elapsed = ordered[^1].CapturedAt - ordered[0].CapturedAt;
        var consumed = ordered[0].RemainingPercent - ordered[^1].RemainingPercent;
        if (elapsed < TimeSpan.FromMinutes(10) || consumed <= 0)
        {
            return new(0, null, PredictionConfidence.Low, ordered.Length);
        }

        var rate = consumed / elapsed.TotalHours;
        var hoursRemaining = ordered[^1].RemainingPercent / rate;
        var confidence = elapsed >= TimeSpan.FromHours(2) && ordered.Length >= 8
            ? PredictionConfidence.High
            : elapsed >= TimeSpan.FromMinutes(30) && ordered.Length >= 5
                ? PredictionConfidence.Medium
                : PredictionConfidence.Low;
        DateTimeOffset? exhaustion = ordered[^1].CapturedAt.AddHours(hoursRemaining);
        if (exhaustion >= resetsAt) exhaustion = null;
        if (exhaustion is null) return new(rate, null, confidence, ordered.Length);

        var spread = confidence switch
        {
            PredictionConfidence.High => 0.15,
            PredictionConfidence.Medium => 0.3,
            _ => 0.5,
        };
        var earliest = ordered[^1].CapturedAt.AddHours(hoursRemaining * (1 - spread));
        var latest = ordered[^1].CapturedAt.AddHours(hoursRemaining * (1 + spread));
        if (latest > resetsAt) latest = resetsAt;
        return new(rate, exhaustion, confidence, ordered.Length, earliest, latest);
    }
}
