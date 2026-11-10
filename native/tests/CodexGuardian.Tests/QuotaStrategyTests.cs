using CodexGuardian.Core;

namespace CodexGuardian.Tests;

public class QuotaStrategyTests
{
    [Fact]
    public void FlagsCurrentQuotaBelowReserve()
    {
        var now = DateTimeOffset.UtcNow;
        var snapshot = CreateSnapshot(now, fiveHourRemaining: 12, weeklyRemaining: 60);

        var result = QuotaStrategyAdvisor.Evaluate(snapshot, new(0, null, PredictionConfidence.None, 0),
            QuotaStrategyPolicy.For(QuotaStrategyMode.Balanced), now);

        Assert.True(result.IsBelowReserve);
        Assert.False(result.IsProjectedBelowReserve);
    }

    [Fact]
    public void FlagsTrustedProjectionCrossingReserve()
    {
        var now = DateTimeOffset.UtcNow;
        var snapshot = CreateSnapshot(now, fiveHourRemaining: 50, weeklyRemaining: 60);

        var result = QuotaStrategyAdvisor.Evaluate(snapshot, new(12, now.AddHours(3), PredictionConfidence.Medium, 8),
            QuotaStrategyPolicy.For(QuotaStrategyMode.Balanced), now);

        Assert.False(result.IsBelowReserve);
        Assert.True(result.IsProjectedBelowReserve);
        Assert.Contains("8 local samples", result.Explanation, StringComparison.Ordinal);
    }

    [Fact]
    public void LowConfidenceDoesNotClaimFutureReserveRisk()
    {
        var now = DateTimeOffset.UtcNow;
        var snapshot = CreateSnapshot(now, fiveHourRemaining: 50, weeklyRemaining: 60);

        var result = QuotaStrategyAdvisor.Evaluate(snapshot, new(20, now.AddHours(2), PredictionConfidence.Low, 3),
            QuotaStrategyPolicy.For(QuotaStrategyMode.Balanced), now);

        Assert.False(result.IsProjectedBelowReserve);
        Assert.Contains("more local history", result.Explanation, StringComparison.OrdinalIgnoreCase);
    }

    private static QuotaSnapshot CreateSnapshot(DateTimeOffset now, int fiveHourRemaining, int weeklyRemaining) => new(
        now,
        "test",
        new(100 - fiveHourRemaining, 300, now.AddHours(4)),
        new(100 - weeklyRemaining, 10080, now.AddDays(5)),
        DataFreshness.Fresh);
}
