using CodexGuardian.Core;
namespace CodexGuardian.Tests;

public sealed class WeeklyBudgetTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 3, 12, 0, 0, TimeSpan.Zero);
    private static QuotaSnapshot Snapshot => new(Now, "test", null, new(19,10080,Now.AddDays(5)), DataFreshness.Fresh);
    [Fact] public void DistributesAfterReserve() => Assert.Equal(14.2, WeeklyBudget.Calculate(Snapshot,5,10,Now));
    [Fact] public void ReserveCanExceedBalance() => Assert.Equal(0, WeeklyBudget.Calculate(Snapshot,5,90,Now));
    [Fact] public void RejectsUnknownStaleAndReset()
    {
        Assert.Null(WeeklyBudget.Calculate(null,5,10,Now));
        Assert.Null(WeeklyBudget.Calculate(Snapshot,5,10,Now.AddMinutes(6)));
        Assert.Null(WeeklyBudget.Calculate(Snapshot with { Weekly = new(19,10080,Now) },1,10,Now));
        Assert.Null(WeeklyBudget.Calculate(Snapshot with { CapturedAt = Now.AddMinutes(1) },1,10,Now));
    }
    [Fact] public void RejectsImpossiblePlan()
    {
        Assert.Null(WeeklyBudget.Calculate(Snapshot,0,10,Now));
        Assert.Null(WeeklyBudget.Calculate(Snapshot,30,10,Now));
        Assert.Null(WeeklyBudget.Calculate(Snapshot,1,-1,Now));
    }
}
