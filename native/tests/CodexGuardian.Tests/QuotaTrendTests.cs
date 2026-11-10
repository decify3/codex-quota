using CodexGuardian.Core;

namespace CodexGuardian.Tests;

public class QuotaTrendTests
{
    [Fact]
    public void ReportsAllRequiredWindows()
    {
        var now = new DateTimeOffset(2026, 7, 13, 0, 0, 0, TimeSpan.Zero);
        var entries = Enumerable.Range(0, 9)
            .Select(index => Entry(now.AddMinutes(-index * 10), 80 + index, 70 + index, now.AddHours(4), now.AddDays(5)))
            .ToArray();

        var trends = QuotaTrendAnalyzer.Analyze(entries, now);

        Assert.Equal(["30m", "2h", "5h", "7d"], trends.Select(trend => trend.Label));
        Assert.Equal(8, trends[1].ConsumedPercent);
        Assert.True(trends[1].PercentPerHour > 0);
    }

    [Fact]
    public void DoesNotBridgeDataGapsOrResetBoundaries()
    {
        var now = new DateTimeOffset(2026, 7, 13, 0, 0, 0, TimeSpan.Zero);
        QuotaHistoryEntry[] entries =
        [
            Entry(now.AddHours(-4), 80, 80, now.AddHours(-1), now.AddDays(2)),
            Entry(now.AddHours(-3), 20, 70, now.AddHours(-1), now.AddDays(2)),
            Entry(now.AddMinutes(-20), 95, 69, now.AddHours(4), now.AddDays(2)),
            Entry(now.AddMinutes(-10), 90, 68, now.AddHours(4), now.AddDays(2)),
            Entry(now, 85, 67, now.AddHours(4), now.AddDays(2)),
        ];

        var fiveHour = QuotaTrendAnalyzer.Analyze(entries, now).Single(trend => trend.Label == "5h");

        Assert.Equal(10, fiveHour.ConsumedPercent);
        Assert.Equal(3, fiveHour.SampleCount);
        Assert.Equal(TimeSpan.FromMinutes(20), fiveHour.ObservedDuration);
    }

    private static QuotaHistoryEntry Entry(
        DateTimeOffset capturedAt,
        int fiveHourRemaining,
        int weeklyRemaining,
        DateTimeOffset fiveHourReset,
        DateTimeOffset weeklyReset) =>
        new(capturedAt, fiveHourRemaining, weeklyRemaining, fiveHourReset, weeklyReset, "test");
}

public class PredictionBacktesterTests
{
    [Fact]
    public void StableHistoryMeetsThePublishedMedianErrorTarget()
    {
        var start = new DateTimeOffset(2026, 7, 12, 8, 0, 0, TimeSpan.Zero);
        var history = Enumerable.Range(0, 12)
            .Select(index => new QuotaHistoryPoint(start.AddMinutes(index * 15), 95 - index * 3))
            .ToArray();

        var result = PredictionBacktester.Evaluate(history, start.AddHours(5));

        Assert.True(result.EvaluationCount >= 3);
        Assert.True(result.TargetMet);
        Assert.InRange(result.MedianAbsoluteErrorPercent, 0, 0.01);
    }

    [Fact]
    public void InsufficientHistoryDoesNotClaimTheTargetWasMet()
    {
        var now = DateTimeOffset.UtcNow;
        var result = PredictionBacktester.Evaluate(
            [new(now, 80), new(now.AddMinutes(5), 79)],
            now.AddHours(5));

        Assert.Equal(0, result.EvaluationCount);
        Assert.False(result.TargetMet);
    }
}
