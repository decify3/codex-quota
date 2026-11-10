using CodexGuardian.Core;
namespace CodexGuardian.Tests;
public sealed class OrbSelectionTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void CurrentWeeklyWinsOverExpiredOrStaleShortWindow(bool resetPassed)
    {
        var now = DateTimeOffset.UtcNow;
        var snapshot = new QuotaSnapshot(now, "test",
            new(39, 300, resetPassed ? now.AddMinutes(-1) : now.AddHours(1)),
            new(0, 10080, now.AddDays(7)), DataFreshness.Fresh,
            FiveHourCapturedAt: resetPassed ? now : now.AddHours(-2));
        var selected = OrbSelection.Select(snapshot, now);
        Assert.True(selected.IsWeekly);
        Assert.Equal(100, selected.Window!.RemainingPercent);
    }
    [Fact] public void WeeklyOnlyAccountUsesWeeklyWindow()
    {
        var now = DateTimeOffset.UtcNow;
        var weekly = new QuotaWindow(1,10080,now.AddDays(2));
        var result = OrbSelection.Select(new(now,"test",null,weekly,DataFreshness.Fresh),now);
        Assert.True(result.IsWeekly);
        Assert.Equal(99,result.Window!.RemainingPercent);
    }
    [Fact] public void DualWindowUsesShortAndMissingNeverFabricatesBalance()
    {
        var now=DateTimeOffset.UtcNow;
        Assert.Null(OrbSelection.Select(null,now).Window);
        var result=OrbSelection.Select(new(now,"test",new(50,300,now.AddHours(1)),new(10,10080,now.AddDays(2)),DataFreshness.Fresh),now);
        Assert.False(result.IsWeekly);
        Assert.Equal(50,result.Window!.RemainingPercent);
    }
}
