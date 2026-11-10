using CodexGuardian.Providers;

namespace CodexGuardian.Tests;

public class ProviderTailTests
{
    [Fact]
    public async Task ReadsValidatedEventFromTailOfLargeFile()
    {
        var root = Path.Combine(TestPaths.Root, "large-session");
        var day = Path.Combine(root, "2026", "07", "13");
        var file = Path.Combine(day, "rollout-tail-test.jsonl");
        Directory.CreateDirectory(day);
        var prefix = new string('x', 5 * 1024 * 1024) + Environment.NewLine;
        var line = "{\"timestamp\":\"2026-07-13T00:00:00Z\",\"payload\":{\"rate_limits\":{\"primary\":{\"used_percent\":25,\"window_minutes\":300,\"resets_at\":1783908000},\"secondary\":{\"used_percent\":40,\"window_minutes\":10080,\"resets_at\":1784512800},\"plan_type\":\"plus\"}}}";
        await File.WriteAllTextAsync(file, prefix + line + Environment.NewLine);
        var now = new DateTimeOffset(2026, 7, 13, 0, 1, 0, TimeSpan.Zero);
        var provider = new CodexSessionQuotaProvider(root, new FixedTimeProvider(now));

        var result = await provider.ReadAsync();

        Assert.True(result.IsSuccess);
        Assert.Equal(75, result.Snapshot!.FiveHour!.RemainingPercent);
        Assert.Equal(60, result.Snapshot.Weekly!.RemainingPercent);
    }

    [Fact]
    public async Task CachedSnapshotStillUpdatesFreshness()
    {
        var root = Path.Combine(TestPaths.Root, "cached-session");
        var file = Path.Combine(root, "rollout-cache-test.jsonl");
        Directory.CreateDirectory(root);
        var line = "{\"timestamp\":\"2026-07-13T00:00:00Z\",\"payload\":{\"rate_limits\":{\"primary\":{\"used_percent\":25,\"window_minutes\":300,\"resets_at\":1783908000},\"secondary\":{\"used_percent\":40,\"window_minutes\":10080,\"resets_at\":1784512800}}}}";
        await File.WriteAllTextAsync(file, line + Environment.NewLine);
        var clock = new MutableTimeProvider(new(2026, 7, 13, 0, 1, 0, TimeSpan.Zero));
        var provider = new CodexSessionQuotaProvider(root, clock);

        Assert.Equal(CodexGuardian.Core.DataFreshness.Fresh, (await provider.ReadAsync()).Snapshot!.Freshness);
        clock.Now = clock.Now.AddHours(2);
        Assert.Equal(CodexGuardian.Core.DataFreshness.Expired, (await provider.ReadAsync()).Snapshot!.Freshness);
    }

    [Fact]
    public async Task MergesFiveHourAndWeeklyWindowsAcrossRecentFiles()
    {
        var root = Path.Combine(TestPaths.Root, "merged-window-sessions", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var fiveHourFile = Path.Combine(root, "rollout-five-hour.jsonl");
        var weeklyFile = Path.Combine(root, "rollout-weekly.jsonl");
        await File.WriteAllTextAsync(fiveHourFile,
            "{\"timestamp\":\"2026-07-14T09:55:00Z\",\"payload\":{\"rate_limits\":{\"primary\":{\"used_percent\":25,\"window_minutes\":300,\"resets_at\":1784026500},\"secondary\":null}}}" + Environment.NewLine);
        await File.WriteAllTextAsync(weeklyFile,
            "{\"timestamp\":\"2026-07-14T09:59:00Z\",\"payload\":{\"rate_limits\":{\"primary\":{\"used_percent\":41,\"window_minutes\":10080,\"resets_at\":1784554114},\"secondary\":null,\"plan_type\":\"plus\"}}}" + Environment.NewLine);
        File.SetLastWriteTimeUtc(fiveHourFile, new(2026, 7, 14, 9, 55, 0, DateTimeKind.Utc));
        File.SetLastWriteTimeUtc(weeklyFile, new(2026, 7, 14, 9, 59, 0, DateTimeKind.Utc));
        var provider = new CodexSessionQuotaProvider(root, new FixedTimeProvider(new(2026, 7, 14, 10, 0, 0, TimeSpan.Zero)));

        var result = await provider.ReadAsync();

        Assert.True(result.IsSuccess);
        Assert.Equal(75, result.Snapshot!.FiveHour!.RemainingPercent);
        Assert.Equal(59, result.Snapshot.Weekly!.RemainingPercent);
        Assert.Equal(new DateTimeOffset(2026, 7, 14, 9, 55, 0, TimeSpan.Zero), result.Snapshot.FiveHourObservedAt);
        Assert.Equal(new DateTimeOffset(2026, 7, 14, 9, 59, 0, TimeSpan.Zero), result.Snapshot.WeeklyObservedAt);
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class MutableTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = now;
        public override DateTimeOffset GetUtcNow() => Now;
    }
}
