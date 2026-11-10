using CodexGuardian.Core;
namespace CodexGuardian.Tests;
public class PlanNotificationGateTests
{
    [Fact]
    public void DeliversOncePerContextAndSeverity()
    {
        var gate = new PlanNotificationGate();
        var now = DateTimeOffset.UtcNow;
        var reset = now.AddDays(2);
        var context = new AdvisorContext(false, false, false, false);
        Assert.True(gate.ShouldDeliver(PlanRisk.ReserveAtRisk, "a", reset, now, true, context));
        Assert.False(gate.ShouldDeliver(PlanRisk.ReserveAtRisk, "a", reset, now.AddHours(1), true, context));
        Assert.True(gate.ShouldDeliver(PlanRisk.MinimumUnavailable, "a", reset, now, true, context));
        Assert.True(gate.ShouldDeliver(PlanRisk.ReserveAtRisk, "b", reset, now, true, context));
        Assert.False(gate.ShouldDeliver(PlanRisk.Unknown, "a", reset, now, true, context));
    }
    [Fact]
    public void SuppressionDoesNotConsumeNotificationAndSilenceIsRespected()
    {
        var gate = new PlanNotificationGate();
        var now = DateTimeOffset.UtcNow;
        var reset = now.AddDays(1);
        var context = new AdvisorContext(false, false, false, false);
        Assert.False(gate.ShouldDeliver(PlanRisk.Exhausted, "a", reset, now, false, context));
        Assert.False(gate.ShouldDeliver(PlanRisk.Exhausted, "a", reset, now, true, context with { IsDoNotDisturb = true }));
        Assert.False(gate.ShouldDeliver(PlanRisk.Exhausted, "a", reset, now, true, context with { IsFullScreen = true }));
        Assert.True(gate.ShouldDeliver(PlanRisk.Exhausted, "a", reset, now, true, context));
        gate.SilenceUntil(reset);
        Assert.False(gate.ShouldDeliver(PlanRisk.MinimumUnavailable, "a", reset, now, true, context));
    }
}
