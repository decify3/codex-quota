using CodexGuardian.Core;
using CodexGuardian.Storage;

namespace CodexGuardian.Tests;

public class DailyPlanStoreTests
{
    [Theory]
    [InlineData("[null]")]
    [InlineData("[]")]
    [InlineData("[{\"Date\":\"2026-11-10\",\"Weight\":-1,\"MinimumPercent\":0}]")]
    public void InvalidStoredDaysAreIgnoredInsteadOfCrashingEditor(string daysJson)
    {
        var path = Path.Combine(TestPaths.Root, Guid.NewGuid().ToString("N"), "plan.json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var reset = new DateTimeOffset(2026, 11, 12, 0, 0, 0, TimeSpan.Zero);
        File.WriteAllText(path, "{\"ScopeKey\":\"a\",\"ResetsAt\":\"2026-11-12T00:00:00Z\",\"ReservePercent\":10,\"Days\":" + daysJson + "}");
        Assert.Null(new DailyPlanStore(path).Load("a", reset));
    }

    [Fact]
    public void RestoresOnlySameAccountAndReset()
    {
        var path = Path.Combine(TestPaths.Root, Guid.NewGuid().ToString("N"), "plan.json");
        var store = new DailyPlanStore(path);
        var reset = DateTimeOffset.UtcNow.AddDays(2);
        store.Save(new("scope-a", reset, 10, [new(new(2026, 11, 3), 2, 5)]));
        var restored = store.Load("scope-a", reset);
        Assert.NotNull(restored);
        Assert.Equal(2, restored.Days[0].Weight);
        Assert.Equal(5, restored.Days[0].MinimumPercent);
        Assert.Null(store.Load("scope-b", reset));
        Assert.Null(store.Load("scope-a", reset.AddDays(7)));
        Assert.Null(store.Load(null, reset));
        File.WriteAllText(path, "{broken");
        Assert.Null(store.Load("scope-a", reset));
    }

    [Fact]
    public void MidnightResetDoesNotMakeResetDateAvailable()
    {
        var now = new DateTimeOffset(2026, 11, 3, 22, 0, 0, TimeSpan.Zero);
        var snapshot = new QuotaSnapshot(now, "test", null,
            new(10, 10080, new(2026, 11, 4, 0, 0, 0, TimeSpan.Zero)), DataFreshness.Fresh);
        var plan = DailyBudgetPlanner.Calculate(snapshot, [new(new(2026, 11, 4), 1)], 0, now, TimeZoneInfo.Utc);
        Assert.Equal("invalid-days", plan.Error);
    }
}
