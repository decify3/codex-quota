using CodexGuardian.Core;
using CodexGuardian.Providers;
namespace CodexGuardian.Tests;
public class UnknownWindowTests
{
    [Fact]
    public void RetainsUnknownLiveDurationAndDoesNotInventFiveHourWindow()
    {
        const string json = """{"id":2,"result":{"rateLimits":{"primary":{"usedPercent":12,"windowDurationMins":480,"resetsAt":2000000000},"secondary":null,"planType":"pro"}}}""";
        var now = DateTimeOffset.FromUnixTimeSeconds(1999990000);
        Assert.True(AppServerRateLimitParser.TryParseResponse(json, now, out var snapshot));
        Assert.Null(snapshot!.FiveHour);
        Assert.Null(snapshot.Weekly);
        Assert.Equal(480, Assert.Single(snapshot.OtherWindows!).WindowMinutes);
        var orb = OrbSelection.Select(snapshot, now);
        Assert.Equal(88, orb.Window!.RemainingPercent);
        Assert.Equal(DataFreshness.Fresh, orb.Freshness);
        Assert.Equal(PlanRisk.Unknown, PlanRiskEvaluator.Evaluate(snapshot, new(10, 2, "test"), null, 0, now, TimeZoneInfo.Utc));
    }
}
