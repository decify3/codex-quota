using CodexGuardian.Core;

namespace CodexGuardian.Tests;

public class DailyBudgetPlannerTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 3, 10, 0, 0, TimeSpan.Zero);
    private static QuotaSnapshot Snapshot => new(Now, "test", null, new(20, 10080, Now.AddDays(3)), DataFreshness.Fresh);
    private static readonly DateOnly Today = new(2026, 11, 3);

    [Fact]
    public void AllocatesImportantDayMinimumThenWeightedRemainingBudget()
    {
        var result = DailyBudgetPlanner.Calculate(Snapshot,
            [new(Today, 1), new(Today.AddDays(1), 2, 10)], 10, Now, TimeZoneInfo.Utc);
        Assert.True(result.IsValid);
        Assert.Equal(70, result.AvailablePercent);
        Assert.Equal(20, result.Days[0].Percent);
        Assert.Equal(50, result.Days[1].Percent);
        Assert.Equal(result.AvailablePercent, result.Days.Sum(day => day.Percent), 8);
    }

    [Fact]
    public void RefusesImpossibleMinimumOrStaleQuota()
    {
        Assert.Equal("minimum-exceeds-quota", DailyBudgetPlanner.Calculate(Snapshot,
            [new(Today, 1, 80)], 10, Now, TimeZoneInfo.Utc).Error);
        Assert.Equal("quota-unavailable", DailyBudgetPlanner.Calculate(Snapshot,
            [new(Today, 1)], 10, Now.AddMinutes(6), TimeZoneInfo.Utc).Error);
    }

    [Fact]
    public void RefusesDuplicatePastAndPostResetDates()
    {
        Assert.False(DailyBudgetPlanner.Calculate(Snapshot,
            [new(Today, 1), new(Today, 2)], 0, Now, TimeZoneInfo.Utc).IsValid);
        Assert.False(DailyBudgetPlanner.Calculate(Snapshot,
            [new(Today.AddDays(-1), 1)], 0, Now, TimeZoneInfo.Utc).IsValid);
        Assert.False(DailyBudgetPlanner.Calculate(Snapshot,
            [new(Today.AddDays(4), 1)], 0, Now, TimeZoneInfo.Utc).IsValid);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void RefusesInvalidEffort(double effort)
    {
        Assert.False(DailyBudgetPlanner.Calculate(Snapshot,
            [new(Today, effort)], 0, Now, TimeZoneInfo.Utc).IsValid);
    }
}
