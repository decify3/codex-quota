using CodexGuardian.Core;
using CodexGuardian.Storage;
using System.Text.Json;

namespace CodexGuardian.Tests;

public class HistoryStoreTests
{
    [Fact]
    public void ParsesSanitizedHistoryEntry()
    {
        var line = """
            {"CapturedAt":"2026-07-12T10:00:00+00:00","FiveHourRemainingPercent":70,"WeeklyRemainingPercent":80,"FiveHourResetsAt":"2026-07-12T14:00:00+00:00","WeeklyResetsAt":"2026-07-18T08:00:00+00:00","Source":"Codex local session metadata"}
            """;

        Assert.True(JsonlQuotaHistoryStore.TryParse(line, out var entry));
        Assert.Equal(70, entry!.FiveHourRemainingPercent);
        Assert.Equal(80, entry.WeeklyRemainingPercent);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-json")]
    [InlineData("{\"FiveHourRemainingPercent\":120}")]
    public void RejectsInvalidHistoryLine(string line)
    {
        Assert.False(JsonlQuotaHistoryStore.TryParse(line, out _));
    }

    [Fact]
    public async Task ExportsOnlySanitizedQuotaFields()
    {
        var folder = TestPaths.Root;
        var historyPath = Path.Combine(folder, "export-source.jsonl");
        var exportPath = Path.Combine(folder, "sanitized-export.json");
        Directory.CreateDirectory(folder);
        await File.WriteAllTextAsync(historyPath,
            "{\"CapturedAt\":\"2026-07-12T10:00:00+00:00\",\"FiveHourRemainingPercent\":70,\"WeeklyRemainingPercent\":80,\"FiveHourResetsAt\":\"2026-07-12T14:00:00+00:00\",\"WeeklyResetsAt\":\"2026-07-18T08:00:00+00:00\",\"Source\":\"Codex local session metadata\"}\n");

        using var store = new JsonlQuotaHistoryStore(historyPath);
        await store.ExportSanitizedAsync(exportPath);
        using var document = JsonDocument.Parse(await File.ReadAllTextAsync(exportPath));

        var root = document.RootElement;
        Assert.Equal(1, root.GetProperty("schema").GetInt32());
        var serialized = root.GetProperty("entries").GetRawText();
        Assert.DoesNotContain("prompt", serialized, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("token", serialized, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(70, root.GetProperty("entries")[0].GetProperty("FiveHourRemainingPercent").GetInt32());
    }

    [Fact]
    public async Task ClearKeepsHistoryFileButRemovesItsEntries()
    {
        var folder = TestPaths.Root;
        var historyPath = Path.Combine(folder, "clear-source.jsonl");
        Directory.CreateDirectory(folder);
        await File.WriteAllTextAsync(historyPath, "test fixture");

        using var store = new JsonlQuotaHistoryStore(historyPath);
        await store.ClearAsync();

        Assert.True(File.Exists(historyPath));
        Assert.Empty(await File.ReadAllTextAsync(historyPath));
    }

    [Fact]
    public async Task CompactionKeepsOnlyConfiguredRetentionWindow()
    {
        var folder = TestPaths.Root;
        var historyPath = Path.Combine(folder, "retention-source.jsonl");
        var now = new DateTimeOffset(2026, 7, 13, 12, 0, 0, TimeSpan.Zero);
        Directory.CreateDirectory(folder);
        await File.WriteAllLinesAsync(historyPath,
        [
            HistoryLine(now.AddDays(-31), 80),
            HistoryLine(now.AddDays(-29), 70),
            HistoryLine(now, 60),
        ]);

        using var store = new JsonlQuotaHistoryStore(historyPath, TimeSpan.FromDays(30));
        await store.CompactAsync(now);
        var entries = await store.ReadSinceAsync(DateTimeOffset.MinValue);

        Assert.Equal(2, entries.Count);
        Assert.DoesNotContain(entries, entry => entry.CapturedAt == now.AddDays(-31));
    }

    [Fact]
    public async Task HistoryNeverExceedsConfiguredByteBudgetUnderHighFrequencyWrites()
    {
        var historyPath = Path.Combine(TestPaths.Root, "bounded-history.jsonl");
        var start = new DateTimeOffset(2026, 7, 14, 12, 0, 0, TimeSpan.Zero);
        const long maximumBytes = 2048;
        await File.WriteAllTextAsync(historyPath, string.Empty);
        using var store = new JsonlQuotaHistoryStore(
            historyPath,
            TimeSpan.FromDays(365),
            maximumBytes);

        for (var index = 0; index < 100; index++)
        {
            var capturedAt = start.AddSeconds(index);
            await store.AppendAsync(new(
                capturedAt,
                "test",
                new(index % 100, 300, start.AddHours(5)),
                new(20, 10080, start.AddDays(7)),
                DataFreshness.Fresh));
        }

        var entries = await store.ReadSinceAsync(DateTimeOffset.MinValue);
        Assert.InRange(new FileInfo(historyPath).Length, 1, maximumBytes);
        Assert.NotEmpty(entries);
        Assert.Equal(start.AddSeconds(99), entries[^1].CapturedAt);
        Assert.True(entries.Count < 100);
    }

    private static string HistoryLine(DateTimeOffset capturedAt, int remaining) =>
        $$"""
        {"CapturedAt":"{{capturedAt:O}}","FiveHourRemainingPercent":{{remaining}},"WeeklyRemainingPercent":80,"FiveHourResetsAt":"{{capturedAt.AddHours(5):O}}","WeeklyResetsAt":"{{capturedAt.AddDays(7):O}}","Source":"test"}
        """;

    [Fact]
    public async Task RestartDoesNotAppendSameCapturedSnapshotTwice()
    {
        var folder = TestPaths.Root;
        var historyPath = Path.Combine(folder, "restart-dedupe.jsonl");
        Directory.CreateDirectory(folder);
        await File.WriteAllTextAsync(historyPath, string.Empty);
        var capturedAt = new DateTimeOffset(2026, 7, 13, 12, 0, 0, TimeSpan.Zero);
        var snapshot = new CodexGuardian.Core.QuotaSnapshot(capturedAt, "test", new(20, 300, capturedAt.AddHours(5)), new(30, 10080, capturedAt.AddDays(7)), CodexGuardian.Core.DataFreshness.Fresh);

        using (var first = new JsonlQuotaHistoryStore(historyPath)) await first.AppendAsync(snapshot);
        using (var restarted = new JsonlQuotaHistoryStore(historyPath)) await restarted.AppendAsync(snapshot);
        using var reader = new JsonlQuotaHistoryStore(historyPath);

        Assert.Single(await reader.ReadSinceAsync(DateTimeOffset.MinValue));
    }

    [Fact]
    public async Task CorruptLinesAreSkippedWithoutLosingValidEntries()
    {
        var folder = TestPaths.Root;
        var historyPath = Path.Combine(folder, "partially-corrupt.jsonl");
        var capturedAt = new DateTimeOffset(2026, 7, 13, 12, 0, 0, TimeSpan.Zero);
        Directory.CreateDirectory(folder);
        await File.WriteAllLinesAsync(historyPath, ["not-json", HistoryLine(capturedAt, 60), "{partial"]);

        using var store = new JsonlQuotaHistoryStore(historyPath);
        var entries = await store.ReadSinceAsync(DateTimeOffset.MinValue);

        Assert.Single(entries);
        Assert.Equal(60, entries[0].FiveHourRemainingPercent);
    }

    [Fact]
    public async Task ConcurrentAppendsProduceReadableHistory()
    {
        var folder = TestPaths.Root;
        var historyPath = Path.Combine(folder, "concurrent-history.jsonl");
        var start = new DateTimeOffset(2026, 7, 13, 12, 0, 0, TimeSpan.Zero);
        Directory.CreateDirectory(folder);
        await File.WriteAllTextAsync(historyPath, string.Empty);
        using var store = new JsonlQuotaHistoryStore(historyPath);

        await Task.WhenAll(Enumerable.Range(0, 100).Select(index =>
        {
            var captured = start.AddSeconds(index);
            return store.AppendAsync(new(captured, "test", new(index % 100, 300, start.AddHours(5)), new(30, 10080, start.AddDays(7)), CodexGuardian.Core.DataFreshness.Fresh));
        }));
        var entries = await store.ReadSinceAsync(DateTimeOffset.MinValue);

        Assert.Equal(100, entries.Count);
        Assert.Equal(entries.OrderBy(entry => entry.CapturedAt), entries);
    }

    [Fact]
    public async Task PersistsWeeklyWindowWhenFiveHourWindowIsMissing()
    {
        var path = Path.Combine(TestPaths.Root, "partial-window-history.jsonl");
        await File.WriteAllTextAsync(path, string.Empty);
        var capturedAt = new DateTimeOffset(2026, 7, 14, 10, 0, 0, TimeSpan.Zero);
        using var store = new JsonlQuotaHistoryStore(path);

        await store.AppendAsync(new(
            capturedAt,
            "test",
            null,
            new(41, 10080, capturedAt.AddDays(7)),
            DataFreshness.Fresh,
            "plus",
            null,
            capturedAt));
        var entries = await store.ReadSinceAsync(DateTimeOffset.MinValue);

        var entry = Assert.Single(entries);
        Assert.Null(entry.FiveHourRemainingPercent);
        Assert.Equal(59, entry.WeeklyRemainingPercent);
    }
}
