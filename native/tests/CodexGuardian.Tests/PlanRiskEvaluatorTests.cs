using CodexGuardian.Core;
namespace CodexGuardian.Tests;
public class PlanRiskEvaluatorTests
{
    [Fact]
    public void BreachedReserveWarnsWithoutUsageHistory()
    {
        var now = new DateTimeOffset(2026, 11, 10, 10, 0, 0, TimeSpan.Zero);
        var snapshot = new QuotaSnapshot(now, "test", null,
            new(95, 10080, now.AddDays(2)), DataFreshness.Fresh, ScopeKey: "a");
        PlannedWorkDay[] days = [new(DateOnly.FromDateTime(now.UtcDateTime), 1)];
        Assert.Equal(PlanRisk.ReserveAtRisk, PlanRiskEvaluator.Evaluate(snapshot,
            new(null, 0, "unknown"), days, 10, now, TimeZoneInfo.Utc));
        Assert.Equal(PlanRisk.Unknown, PlanRiskEvaluator.Evaluate(snapshot,
            new(null, 0, "unknown"), days, 10, now.AddMinutes(6), TimeZoneInfo.Utc));
    }

    [Fact]
    public void EventRiskUsesOnlyPreResetDaysAndExpiresWithoutRefilling()
    {
        var now = new DateTimeOffset(2026, 11, 6, 10, 0, 0, TimeSpan.Zero);
        var day = DateOnly.FromDateTime(now.UtcDateTime);
        var snapshot = new QuotaSnapshot(now, "test", null, new(90, 10080, now.AddDays(3)), DataFreshness.Fresh, ScopeKey: "a");
        PlannedWorkDay[] days = [new(day, 1), new(day.AddDays(1), 1)];
        var resetEvent = new PlannedResetEvent("card", now.AddHours(1), now.AddHours(2));
        Assert.Equal(PlanRisk.ReserveAtRisk, PlanRiskEvaluator.Evaluate(snapshot, new(6, 2, "test"), days, 0, now, TimeZoneInfo.Utc));
        Assert.Equal(PlanRisk.WithinPlan, PlanRiskEvaluator.Evaluate(snapshot, new(6, 2, "test"), days, 0, now, TimeZoneInfo.Utc, resetEvent));
        Assert.Equal(PlanRisk.Unknown, PlanRiskEvaluator.Evaluate(snapshot, new(6, 2, "test"), days, 0, now, TimeZoneInfo.Utc, resetEvent with { PlannedAt = now }));
        Assert.Equal(10, snapshot.Weekly!.RemainingPercent);
    }
    [Fact]
    public void ColorsFollowPlanEvidenceRatherThanRemainingThreshold()
    {
        var now = DateTimeOffset.UtcNow;
        var day = DateOnly.FromDateTime(now.UtcDateTime);
        var snapshot = new QuotaSnapshot(now, "test", null, new(90, 10080, now.AddDays(2)), DataFreshness.Fresh, ScopeKey: "a");
        Assert.Equal(PlanRisk.WithinPlan, PlanRiskEvaluator.Evaluate(snapshot, new(2, 2, "test"), [new(day, 1)], 1, now, TimeZoneInfo.Utc));
        Assert.Equal(PlanRisk.ReserveAtRisk, PlanRiskEvaluator.Evaluate(snapshot, new(12, 2, "test"), [new(day, 1)], 1, now, TimeZoneInfo.Utc));
        Assert.Equal(PlanRisk.Unknown, PlanRiskEvaluator.Evaluate(snapshot, new(null, 0, "test"), [new(day, 1)], 1, now, TimeZoneInfo.Utc));
        Assert.Equal(PlanRisk.MinimumUnavailable, PlanRiskEvaluator.Evaluate(snapshot, new(null, 0, "test"), [new(day, 1, 20)], 1, now, TimeZoneInfo.Utc));
        Assert.Equal(PlanRisk.Unknown, PlanRiskEvaluator.Evaluate(snapshot, new(2, 2, "test"), [new(day, 1)], 1, now.AddMinutes(6), TimeZoneInfo.Utc));
    }
}
