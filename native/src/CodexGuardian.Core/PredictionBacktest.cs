namespace CodexGuardian.Core;

public sealed record PredictionBacktestResult(
    int EvaluationCount,
    double MedianAbsoluteErrorPercent,
    double MeanAbsoluteErrorPercent,
    double TargetErrorPercent,
    bool TargetMet)
{
    public static PredictionBacktestResult Insufficient(double targetErrorPercent) =>
        new(0, 0, 0, targetErrorPercent, false);
}

public static class PredictionBacktester
{
    public const double DefaultTargetErrorPercent = 15;

    public static PredictionBacktestResult Evaluate(
        IReadOnlyList<QuotaHistoryPoint> history,
        DateTimeOffset resetsAt,
        double targetErrorPercent = DefaultTargetErrorPercent)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(targetErrorPercent);

        var ordered = history
            .Where(point => point.CapturedAt <= resetsAt)
            .OrderBy(point => point.CapturedAt)
            .GroupBy(point => point.CapturedAt)
            .Select(group => group.Last())
            .ToArray();
        var errors = new List<double>();

        for (var index = 2; index < ordered.Length - 1; index++)
        {
            var prediction = QuotaPredictor.Predict(ordered[..(index + 1)], resetsAt);
            if (prediction.Confidence == PredictionConfidence.None || prediction.PercentPerHour <= 0) continue;

            var current = ordered[index];
            var target = ordered
                .Skip(index + 1)
                .TakeWhile(point => point.CapturedAt - current.CapturedAt <= TimeSpan.FromMinutes(30))
                .LastOrDefault();
            if (target is null) continue;

            var forecastSpan = target.CapturedAt - current.CapturedAt;
            if (forecastSpan <= TimeSpan.Zero || forecastSpan > TimeSpan.FromMinutes(30)) continue;

            var predictedRemaining = Math.Clamp(
                current.RemainingPercent - prediction.PercentPerHour * forecastSpan.TotalHours,
                0,
                100);
            errors.Add(Math.Abs(predictedRemaining - target.RemainingPercent));
        }

        if (errors.Count < 3) return PredictionBacktestResult.Insufficient(targetErrorPercent);

        errors.Sort();
        var middle = errors.Count / 2;
        var median = errors.Count % 2 == 0
            ? (errors[middle - 1] + errors[middle]) / 2
            : errors[middle];
        var mean = errors.Average();
        return new(errors.Count, median, mean, targetErrorPercent, median <= targetErrorPercent);
    }
}
