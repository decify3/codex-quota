using CodexGuardian.Core;
using CodexGuardian.Storage;

namespace CodexGuardian.Tests;

public class NotificationDecisionStoreTests
{
    [Fact]
    public async Task WritesSafeReasonAndDeduplicatesUnchangedDecision()
    {
        var path = Path.Combine(TestPaths.Root, "notification-decisions.jsonl");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, string.Empty);
        var now = new DateTimeOffset(2026, 7, 13, 12, 0, 0, TimeSpan.Zero);
        var snapshot = new QuotaSnapshot(
            now,
            "test",
            new(94, 300, now.AddHours(3)),
            new(20, 10080, now.AddDays(5)),
            DataFreshness.Fresh);
        using var store = new NotificationDecisionStore(path);

        Assert.True(await store.TryAppendIfChangedAsync(
            new(NotificationLevel.Active, "Critical quota with time to act."),
            snapshot,
            true,
            now));
        Assert.False(await store.TryAppendIfChangedAsync(
            new(NotificationLevel.Active, "Critical quota with time to act."),
            snapshot,
            true,
            now.AddSeconds(15)));

        var content = await File.ReadAllTextAsync(path);
        Assert.Single(content.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries));
        Assert.Contains("Critical quota with time to act.", content, StringComparison.Ordinal);
        Assert.DoesNotContain("prompt", content, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("code", content, StringComparison.OrdinalIgnoreCase);
    }
}
