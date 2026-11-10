using CodexGuardian.Core;

namespace CodexGuardian.Tests;

public class TaskBudgetTests
{
    [Fact]
    public void EstimateIncludesRangeConfidenceAndBasis()
    {
        var result = TaskBudgetAdvisor.Estimate(
            new(10, null, PredictionConfidence.Medium, 8),
            TimeSpan.FromHours(1),
            QuotaStrategyPolicy.For(QuotaStrategyMode.Balanced));

        Assert.Equal(6, result.MinimumPercent);
        Assert.Equal(14, result.MaximumPercent);
        Assert.Equal(PredictionConfidence.Medium, result.Confidence);
        Assert.Contains("8 local samples", result.Basis, StringComparison.Ordinal);
    }

    [Fact]
    public void ColdStartDoesNotInventBudget()
    {
        var result = TaskBudgetAdvisor.Estimate(
            new(0, null, PredictionConfidence.None, 0),
            TimeSpan.FromHours(1),
            QuotaStrategyPolicy.For(QuotaStrategyMode.Balanced));

        Assert.Equal(0, result.MaximumPercent);
        Assert.Equal(PredictionConfidence.None, result.Confidence);
    }

    [Fact]
    public void SummaryDoesNotAttributeConsumptionAcrossReset()
    {
        var now = DateTimeOffset.UtcNow;
        var before = CreateSnapshot(now, 20, now.AddMinutes(5));
        var after = CreateSnapshot(now.AddMinutes(20), 98, now.AddHours(5));

        var result = TaskBudgetAdvisor.Summarize(before, after);

        Assert.Null(result.FiveHourConsumedPercent);
    }

    private static QuotaSnapshot CreateSnapshot(DateTimeOffset capturedAt, int remaining, DateTimeOffset resetAt) => new(
        capturedAt,
        "test",
        new(100 - remaining, 300, resetAt),
        new(30, 10080, capturedAt.AddDays(5)),
        DataFreshness.Fresh, ScopeKey: "account-a");

    [Fact]
    public void MeasuresSameWindowAndScopeInPercentagePoints()
    {
        var now = DateTimeOffset.UtcNow;
        var before = CreateSnapshot(now, 80, now.AddHours(2));
        var after = before with { CapturedAt = now.AddMinutes(10), FiveHour = before.FiveHour! with { UsedPercent = 25 } };
        var summary = TaskBudgetAdvisor.Summarize(before, after);
        Assert.Equal(5, summary.FiveHourConsumedPercent);
        Assert.Equal(0, summary.WeeklyConsumedPercent);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("account-b")]
    public void DoesNotCompareUnknownOrChangedAccount(string? scope)
    {
        var now = DateTimeOffset.UtcNow;
        var before = CreateSnapshot(now, 80, now.AddHours(2));
        var after = before with { CapturedAt = now.AddMinutes(10), ScopeKey = scope };
        var summary = TaskBudgetAdvisor.Summarize(before, after);
        Assert.Null(summary.FiveHourConsumedPercent);
        Assert.Null(summary.WeeklyConsumedPercent);
    }

    [Fact]
    public void ReusedStaleOrIncreasedBalancesAreUnknownInsteadOfZero()
    {
        var now = DateTimeOffset.UtcNow;
        var before = CreateSnapshot(now, 80, now.AddHours(2));
        Assert.Null(TaskBudgetAdvisor.Summarize(before, before).FiveHourConsumedPercent);
        var after = before with { CapturedAt = now.AddMinutes(10), FiveHourCapturedAt = now };
        Assert.Null(TaskBudgetAdvisor.Summarize(before, after).FiveHourConsumedPercent);
        after = before with { CapturedAt = now.AddMinutes(10), FiveHour = before.FiveHour! with { UsedPercent = 10 } };
        Assert.Null(TaskBudgetAdvisor.Summarize(before, after).FiveHourConsumedPercent);
    }
}
