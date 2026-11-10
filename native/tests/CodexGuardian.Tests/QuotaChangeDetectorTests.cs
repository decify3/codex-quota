using CodexGuardian.Core;

namespace CodexGuardian.Tests;

public sealed class QuotaChangeDetectorTests
{
    [Fact]
    public void DetectsOnlyWindowsWithNewerObservations()
    {
        var now = new DateTimeOffset(2026, 7, 14, 12, 0, 0, TimeSpan.Zero);
        var previous = Snapshot(now, fiveHourUsed: 20, weeklyUsed: 30);
        var current = Snapshot(now.AddMinutes(1), fiveHourUsed: 22, weeklyUsed: 30) with
        {
            FiveHour = previous.FiveHour! with { UsedPercent = 22 },
            Weekly = previous.Weekly,
            WeeklyCapturedAt = now,
        };

        var change = Assert.Single(QuotaChangeDetector.Detect(previous, current));

        Assert.Equal(QuotaWindowKind.FiveHour, change.Window);
        Assert.Equal(-2, change.DeltaPercentagePoints);
        Assert.False(change.ResetObserved);
    }

    [Fact]
    public void ReportsAResetEvenWhenRemainingPercentIsUnchanged()
    {
        var now = new DateTimeOffset(2026, 7, 14, 12, 0, 0, TimeSpan.Zero);
        var previous = Snapshot(now, fiveHourUsed: 20, weeklyUsed: 30);
        var current = previous with
        {
            CapturedAt = now.AddMinutes(1),
            FiveHourCapturedAt = now.AddMinutes(1),
            FiveHour = previous.FiveHour! with { ResetsAt = now.AddHours(10) },
        };

        var change = Assert.Single(QuotaChangeDetector.Detect(previous, current));

        Assert.True(change.ResetObserved);
        Assert.Equal(0, change.DeltaPercentagePoints);
    }

    [Fact]
    public void IgnoresTheInitialSnapshotAndRepeatedObservations()
    {
        var now = new DateTimeOffset(2026, 7, 14, 12, 0, 0, TimeSpan.Zero);
        var snapshot = Snapshot(now, fiveHourUsed: 20, weeklyUsed: 30);

        Assert.Empty(QuotaChangeDetector.Detect(null, snapshot));
        Assert.Empty(QuotaChangeDetector.Detect(snapshot, snapshot));
    }

    private static QuotaSnapshot Snapshot(DateTimeOffset capturedAt, int fiveHourUsed, int weeklyUsed) => new(
        capturedAt,
        "test",
        new(fiveHourUsed, 300, capturedAt.AddHours(5)),
        new(weeklyUsed, 10080, capturedAt.AddDays(7)),
        DataFreshness.Fresh,
        FiveHourCapturedAt: capturedAt,
        WeeklyCapturedAt: capturedAt);
}
