using CodexGuardian.Core;
namespace CodexGuardian.Tests;
public sealed class DemoSnapshotsTests
{
    [Fact] public void ScenariosExerciseRealWindowRules()
    {
        var now = DateTimeOffset.UtcNow;
        Assert.True(OrbSelection.Select(DemoSnapshots.Create("weekly",now),now).IsWeekly);
        Assert.False(OrbSelection.Select(DemoSnapshots.Create("dual",now),now).IsWeekly);
        Assert.Equal(DataFreshness.Expired,OrbSelection.Select(DemoSnapshots.Create("stale",now),now).Freshness);
        Assert.Null(WeeklyBudget.Calculate(DemoSnapshots.Create("reset",now),1,0,now));
        Assert.Null(DemoSnapshots.Create("missing",now));
        Assert.Throws<ArgumentException>(()=>DemoSnapshots.Create("bad",now));
    }
}
