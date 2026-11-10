using CodexGuardian.Core;

namespace CodexGuardian.Tests;

public class ResetEventBudgetTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 6, 12, 0, 0, TimeSpan.Zero);
    private static QuotaSnapshot Snapshot => new(Now, "test", null, new(30, 10080, Now.AddDays(5)), DataFreshness.Fresh);
    private static PlannedWorkDay[] Days => [new(new(2026, 11, 6), 1), new(new(2026, 11, 7), 1), new(new(2026, 11, 8), 1)];

    [Fact]
    public void MidnightEventExcludesFollowingDayAndDoesNotAddQuota()
    {
        var plan = ResetEventBudget.Calculate(Snapshot, Days, 10, new("announcement", new(2026, 11, 7, 0, 0, 0, TimeSpan.Zero), null), Now, TimeZoneInfo.Utc);
        Assert.True(plan.IsValid);
        Assert.Single(plan.Days);
        Assert.Equal(60, plan.Days[0].Percent);
        Assert.Equal(70, Snapshot.Weekly!.RemainingPercent);
    }

    [Fact]
    public void CardUseAtExpiryIsRejected()
    {
        var at = Now.AddHours(3);
        Assert.Equal("event-expiry", ResetEventBudget.Calculate(Snapshot, Days, 0, new("card", at, at), Now, TimeZoneInfo.Utc).Error);
        Assert.True(ResetEventBudget.Calculate(Snapshot, Days, 0, new("card", at, at.AddMinutes(1)), Now, TimeZoneInfo.Utc).IsValid);
    }

    [Fact]
    public void PastEventAndEventBeyondNaturalResetAreRejected()
    {
        Assert.Equal("event-invalid", ResetEventBudget.Calculate(Snapshot, Days, 0, new("announcement", Now, null), Now, TimeZoneInfo.Utc).Error);
        Assert.Equal("event-after-reset", ResetEventBudget.Calculate(Snapshot, Days, 0, new("announcement", Now.AddDays(6), null), Now, TimeZoneInfo.Utc).Error);
    }

    [Fact]
    public void StaleQuotaCannotProduceAnEventBudget()
    {
        Assert.Equal("quota-unavailable", ResetEventBudget.Calculate(Snapshot, Days, 0, new("announcement", Now.AddHours(2), null), Now.AddMinutes(6), TimeZoneInfo.Utc).Error);
    }

    [Fact]
    public void LocalMidnightBoundaryUsesSpecifiedTimeZone()
    {
        var zone = TimeZoneInfo.CreateCustomTimeZone("test-plus-eight", TimeSpan.FromHours(8), "Test", "Test");
        var midnight = new DateTimeOffset(2026, 11, 7, 0, 0, 0, TimeSpan.FromHours(8));
        var plan = ResetEventBudget.Calculate(Snapshot, Days, 10, new("announcement", midnight, null), Now, zone);
        Assert.True(plan.IsValid);
        Assert.Single(plan.Days);
        Assert.Equal(new DateOnly(2026, 11, 6), plan.Days[0].Date);
    }

    [Fact]
    public void EventStoreRoundTripDoesNotOverwriteWeeklyPlan()
    {
        var folder = Path.Combine(TestPaths.Root, "reset-event", Guid.NewGuid().ToString("N"));
        var weeklyStore = new CodexGuardian.Storage.DailyPlanStore(Path.Combine(folder, "weekly.json"));
        var eventStore = new CodexGuardian.Storage.DailyPlanStore(Path.Combine(folder, "event.json"));
        var reset = Now.AddDays(5);
        weeklyStore.Save(new("scope", reset, 25, Days));
        var planned = new PlannedResetEvent("card", Now.AddHours(1), Now.AddHours(2));
        eventStore.Save(new("scope", reset, 5, Days, planned));
        Assert.Equal(25, weeklyStore.Load("scope", reset)!.ReservePercent);
        Assert.Null(weeklyStore.Load("scope", reset)!.ResetEvent);
        Assert.Equal(planned, eventStore.Load("scope", reset)!.ResetEvent);
        Assert.Null(eventStore.Load("other", reset));
        Assert.Null(eventStore.Load("scope", reset.AddDays(1)));
    }
}
