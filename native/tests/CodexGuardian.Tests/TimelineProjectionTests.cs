using CodexGuardian.Core;

namespace CodexGuardian.Tests;

public class TimelineProjectionTests
{
    [Fact]
    public void PausedScenarioKeepsQuotaFlat()
    {
        var now = DateTimeOffset.UtcNow;
        var snapshot = CreateSnapshot(now, 60);
        var points = QuotaTimeline.Project(
            snapshot,
            new(20, now.AddHours(3), PredictionConfidence.Medium, 6),
            UsageScenario.Paused,
            now);

        Assert.All(points, point => Assert.Equal(60, point.RemainingPercent));
    }

    [Fact]
    public void HighScenarioConsumesFasterThanCurrent()
    {
        var now = DateTimeOffset.UtcNow;
        var snapshot = CreateSnapshot(now, 80);
        var prediction = new BurnRatePrediction(10, null, PredictionConfidence.Medium, 6);

        var current = QuotaTimeline.Project(snapshot, prediction, UsageScenario.Current, now);
        var high = QuotaTimeline.Project(snapshot, prediction, UsageScenario.High, now);

        Assert.True(high[^1].RemainingPercent < current[^1].RemainingPercent);
    }

    [Fact]
    public void ProjectionEndsAtReset()
    {
        var now = DateTimeOffset.UtcNow;
        var snapshot = CreateSnapshot(now, 80);
        var points = QuotaTimeline.Project(
            snapshot,
            new(10, null, PredictionConfidence.Low, 3),
            UsageScenario.Current,
            now,
            TimeSpan.FromMinutes(20));

        Assert.Equal(snapshot.FiveHour!.ResetsAt, points[^1].Time);
    }

    [Fact]
    public void NoConfidenceDoesNotDrawAFutureCurve()
    {
        var now = DateTimeOffset.UtcNow;
        var snapshot = CreateSnapshot(now, 70);

        var points = QuotaTimeline.Project(
            snapshot,
            new(0, null, PredictionConfidence.None, 1),
            UsageScenario.Current,
            now);

        Assert.Single(points);
    }

    [Fact]
    public void LowerConfidenceProducesAWiderUncertaintyBand()
    {
        var now = DateTimeOffset.UtcNow;
        var snapshot = CreateSnapshot(now, 70);
        var low = QuotaTimeline.Project(snapshot, new(10, null, PredictionConfidence.Low, 3), UsageScenario.Current, now);
        var high = QuotaTimeline.Project(snapshot, new(10, null, PredictionConfidence.High, 10), UsageScenario.Current, now);

        Assert.True(low[^1].UncertaintyPercent > high[^1].UncertaintyPercent);
    }

    private static QuotaSnapshot CreateSnapshot(DateTimeOffset now, int remaining) => new(
        now,
        "test",
        new(100 - remaining, 300, now.AddHours(4)),
        new(20, 10080, now.AddDays(5)),
        DataFreshness.Fresh);
}
