using CodexGuardian.Core;
namespace CodexGuardian.Tests;

public class WeeklyUsageForecasterTests
{
    private static readonly DateTimeOffset Start = new(2026, 11, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Now = Start.AddDays(2).AddHours(10);
    private static readonly DateTimeOffset Reset = Start.AddDays(7);
    private static QuotaSnapshot Snapshot => new(Now, "test", null, new(30, 10080, Reset), DataFreshness.Fresh, ScopeKey: "a");
    private static QuotaHistoryEntry Point(DateTimeOffset time, int remaining, string scope = "a") =>
        new(time, null, remaining, null, Reset, "test", ScopeKey: scope);

    [Fact]
    public void MeasuresCompleteDaysAndProjectsSelectedEffort()
    {
        var baseline = WeeklyUsageForecaster.Measure(
            [Point(Start, 100), Point(Start.AddDays(1), 90), Point(Start.AddDays(2), 70)], Snapshot, Now, TimeZoneInfo.Utc);
        Assert.Equal(2, baseline.CompleteDays);
        Assert.Equal(15, baseline.PercentPerDay);
        var forecast = WeeklyUsageForecaster.Project(baseline,
            [new(new(2026, 11, 3), 1), new(new(2026, 11, 4), 2)], 40);
        Assert.Equal(45, forecast.ExpectedConsumption);
        Assert.Equal(new DateOnly(2026, 11, 4), forecast.ReserveCrossingDate);
    }

    [Fact]
    public void DoesNotInterpretMissingDaysOrOtherAccountsAsZeroConsumption()
    {
        var baseline = WeeklyUsageForecaster.Measure(
            [Point(Start, 100), Point(Start.AddDays(2), 70), Point(Start.AddDays(1), 90, "other")], Snapshot, Now, TimeZoneInfo.Utc);
        Assert.Null(baseline.PercentPerDay);
        Assert.Equal(0, baseline.CompleteDays);
    }

    [Fact]
    public void ExcludesDaysWithBalanceIncreaseAndConflictingObservations()
    {
        var baseline = WeeklyUsageForecaster.Measure(
            [Point(Start, 100), Point(Start.AddHours(10), 80), Point(Start.AddHours(11), 95),
             Point(Start.AddDays(1), 90), Point(Start.AddDays(2), 70)], Snapshot, Now, TimeZoneInfo.Utc);
        Assert.Equal(1, baseline.CompleteDays);
        Assert.Null(baseline.PercentPerDay);
        baseline = WeeklyUsageForecaster.Measure(
            [Point(Start, 100), Point(Start, 99), Point(Start.AddDays(1), 90), Point(Start.AddDays(2), 70)], Snapshot, Now, TimeZoneInfo.Utc);
        Assert.Null(baseline.PercentPerDay);
    }

    [Fact]
    public void StaleSnapshotCannotGenerateForecastBaseline()
    {
        var baseline = WeeklyUsageForecaster.Measure([], Snapshot, Now.AddMinutes(6), TimeZoneInfo.Utc);
        Assert.Null(baseline.PercentPerDay);
        Assert.Null(WeeklyUsageForecaster.Project(baseline, [new(new(2026, 11, 3), 1)], 70).ExpectedConsumption);
    }

    [Fact]
    public void DaytimeSegmentsAreLabeledAndNeverProveWholeDaySufficiency()
    {
        var baseline = WeeklyUsageForecaster.Measure(
            [Point(Start.AddHours(9), 100), Point(Start.AddHours(17), 90),
             Point(Start.AddDays(1).AddHours(9), 85), Point(Start.AddDays(1).AddHours(17), 65)],
            Snapshot, Now, TimeZoneInfo.Utc);
        Assert.True(baseline.IsPartialDay);
        Assert.Equal(15, baseline.PercentPerDay);
        Assert.Equal(8, baseline.AverageObservedHours);
        Assert.Equal(PlanRisk.Unknown, PlanRiskEvaluator.Evaluate(Snapshot, baseline,
            [new(new(2026, 11, 3), 1)], 0, Now, TimeZoneInfo.Utc));
        Assert.Equal(PlanRisk.ReserveAtRisk, PlanRiskEvaluator.Evaluate(Snapshot, baseline,
            [new(new(2026, 11, 3), 5)], 0, Now, TimeZoneInfo.Utc));
    }

    [Fact]
    public void ShortOrSingleDaySegmentsDoNotGenerateBaseline()
    {
        var baseline = WeeklyUsageForecaster.Measure(
            [Point(Start.AddHours(9), 100), Point(Start.AddHours(11), 90),
             Point(Start.AddDays(1).AddHours(9), 85), Point(Start.AddDays(1).AddHours(17), 65)],
            Snapshot, Now, TimeZoneInfo.Utc);
        Assert.Null(baseline.PercentPerDay);
    }
}
