using CodexGuardian.Core;

namespace CodexGuardian.Tests;

public class WeeklyReportTests
{
    [Fact]
    public void EmptyHistoryProducesHonestEmptyReport()
    {
        var report = WeeklyQuotaReporter.Generate([], DateTimeOffset.UtcNow);

        Assert.Equal(0, report.SampleCount);
        Assert.Contains("No local quota samples", report.Summary, StringComparison.Ordinal);
    }

    [Fact]
    public void ReportSeparatesResetWindowsAndIgnoresContent()
    {
        var end = new DateTimeOffset(2026, 7, 13, 12, 0, 0, TimeSpan.Zero);
        var weeklyReset = end.AddDays(3);
        var firstReset = end.AddDays(-4);
        var secondReset = end.AddHours(3);
        QuotaHistoryEntry[] entries =
        [
            Entry(end.AddDays(-2), 90, 70, firstReset, weeklyReset),
            Entry(end.AddDays(-2).AddHours(1), 75, 68, firstReset, weeklyReset),
            Entry(end.AddHours(-2), 85, 60, secondReset, weeklyReset),
            Entry(end.AddHours(-1), 65, 58, secondReset, weeklyReset),
        ];

        var report = WeeklyQuotaReporter.Generate(entries, end);

        Assert.Equal(12, report.WeeklyConsumedPercent);
        Assert.Equal(2, report.FiveHourWindowCount);
        Assert.Equal(20, report.PeakFiveHourBurnPercentPerHour);
        Assert.DoesNotContain("prompt", report.Summary, StringComparison.OrdinalIgnoreCase);
    }

    private static QuotaHistoryEntry Entry(
        DateTimeOffset capturedAt,
        int fiveHourRemaining,
        int weeklyRemaining,
        DateTimeOffset fiveHourReset,
        DateTimeOffset weeklyReset) => new(
            capturedAt, fiveHourRemaining, weeklyRemaining, fiveHourReset, weeklyReset, "test");
}
