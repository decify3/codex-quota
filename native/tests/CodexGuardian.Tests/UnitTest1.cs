using CodexGuardian.Core;
using CodexGuardian.Providers;

namespace CodexGuardian.Tests;

public class SessionRateLimitParserTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 12, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public void ParsesValidRateLimitEvent()
    {
        var line = """
            {"timestamp":"2026-07-12T09:59:00Z","payload":{"rate_limits":{"primary":{"used_percent":24,"window_minutes":300,"resets_at":1783868165},"secondary":{"used_percent":17,"window_minutes":10080,"resets_at":1784358087},"plan_type":"plus"}}}
            """;

        var parsed = SessionRateLimitParser.TryParse(line, Now, out var snapshot);

        Assert.True(parsed);
        Assert.NotNull(snapshot);
        Assert.Equal(76, snapshot.FiveHour!.RemainingPercent);
        Assert.Equal(83, snapshot.Weekly!.RemainingPercent);
        Assert.Equal(DataFreshness.Fresh, snapshot.Freshness);
        Assert.Equal("plus", snapshot.PlanType);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(101)]
    public void KeepsValidSiblingWhenOneWindowHasInvalidPercentage(int usedPercent)
    {
        var line = """
            {"timestamp":"2026-07-12T09:59:00Z","payload":{"rate_limits":{"primary":{"used_percent":USED,"window_minutes":300,"resets_at":1783868165},"secondary":{"used_percent":17,"window_minutes":10080,"resets_at":1784358087}}}}
            """.Replace("USED", usedPercent.ToString(System.Globalization.CultureInfo.InvariantCulture));

        Assert.True(SessionRateLimitParser.TryParse(line, Now, out var snapshot));
        Assert.Null(snapshot!.FiveHour);
        Assert.Equal(83, snapshot.Weekly!.RemainingPercent);
    }

    [Fact]
    public void MarksOldMetadataAsExpired()
    {
        var line = """
            {"timestamp":"2026-07-12T07:00:00Z","payload":{"rate_limits":{"primary":{"used_percent":24,"window_minutes":300,"resets_at":1783868165},"secondary":{"used_percent":17,"window_minutes":10080,"resets_at":1784358087}}}}
            """;

        Assert.True(SessionRateLimitParser.TryParse(line, Now, out var snapshot));
        Assert.Equal(DataFreshness.Expired, snapshot!.Freshness);
    }

    [Fact]
    public void SkipsPartialEventWithNullWindow()
    {
        var line = """
            {"timestamp":"2026-07-12T09:59:00Z","payload":{"rate_limits":{"primary":null,"secondary":null}}}
            """;

        Assert.False(SessionRateLimitParser.TryParse(line, Now, out _));
    }

    [Fact]
    public void AcceptsDecimalPercentageFromLiveSchema()
    {
        var line = """
            {"timestamp":"2026-07-12T09:59:00Z","payload":{"rate_limits":{"primary":{"used_percent":24.0,"window_minutes":300,"resets_at":1783868165},"secondary":{"used_percent":17.0,"window_minutes":10080,"resets_at":1784358087}}}}
            """;

        Assert.True(SessionRateLimitParser.TryParse(line, Now, out var snapshot));
        Assert.Equal(76, snapshot!.FiveHour!.RemainingPercent);
    }

    [Fact]
    public void ParsesCurrentWeeklyOnlySchemaFromPrimarySlot()
    {
        var line = """
            {"timestamp":"2026-07-12T09:59:00Z","payload":{"rate_limits":{"primary":{"used_percent":41.0,"window_minutes":10080,"resets_at":1784358087},"secondary":null,"plan_type":"plus"}}}
            """;

        Assert.True(SessionRateLimitParser.TryParse(line, Now, out var snapshot));
        Assert.Null(snapshot!.FiveHour);
        Assert.Equal(59, snapshot.Weekly!.RemainingPercent);
        Assert.Equal(Now.AddMinutes(-1), snapshot.WeeklyObservedAt);
    }

    [Fact]
    public void ClassifiesWindowsByDurationInsteadOfSlotName()
    {
        var line = """
            {"timestamp":"2026-07-12T09:59:00Z","payload":{"rate_limits":{"primary":{"used_percent":17,"window_minutes":10080,"resets_at":1784358087},"secondary":{"used_percent":24,"window_minutes":300,"resets_at":1783868165}}}}
            """;

        Assert.True(SessionRateLimitParser.TryParse(line, Now, out var snapshot));
        Assert.Equal(76, snapshot!.FiveHour!.RemainingPercent);
        Assert.Equal(83, snapshot.Weekly!.RemainingPercent);
    }
}

public class QuotaPredictorTests
{
    [Fact]
    public void PredictsExhaustionWhenTrendPrecedesReset()
    {
        var start = new DateTimeOffset(2026, 7, 12, 8, 0, 0, TimeSpan.Zero);
        var history = Enumerable.Range(0, 8)
            .Select(index => new QuotaHistoryPoint(start.AddMinutes(index * 20), 90 - index * 5))
            .ToArray();

        var prediction = QuotaPredictor.Predict(history, start.AddHours(8));

        Assert.True(prediction.PercentPerHour > 0);
        Assert.NotNull(prediction.EstimatedExhaustionAt);
        Assert.NotNull(prediction.EstimatedExhaustionEarliest);
        Assert.NotNull(prediction.EstimatedExhaustionLatest);
        Assert.True(prediction.EstimatedExhaustionEarliest < prediction.EstimatedExhaustionAt);
        Assert.True(prediction.EstimatedExhaustionLatest > prediction.EstimatedExhaustionAt);
        Assert.Equal(PredictionConfidence.High, prediction.Confidence);
    }

    [Fact]
    public void DoesNotClaimPrecisionDuringColdStart()
    {
        var prediction = QuotaPredictor.Predict(
            [new(DateTimeOffset.UtcNow, 50)],
            DateTimeOffset.UtcNow.AddHours(5));

        Assert.Equal(PredictionConfidence.None, prediction.Confidence);
        Assert.Null(prediction.EstimatedExhaustionAt);
    }

    [Fact]
    public void LongGapStartsANewColdSegment()
    {
        var start = new DateTimeOffset(2026, 7, 12, 8, 0, 0, TimeSpan.Zero);
        QuotaHistoryPoint[] history =
        [
            new(start, 90),
            new(start.AddMinutes(15), 85),
            new(start.AddMinutes(30), 80),
            new(start.AddHours(3), 60),
            new(start.AddHours(3).AddMinutes(10), 58),
        ];

        var prediction = QuotaPredictor.Predict(history, start.AddHours(5));

        Assert.Equal(PredictionConfidence.None, prediction.Confidence);
        Assert.Equal(2, prediction.SampleCount);
    }

    [Fact]
    public void DuplicateTimestampsDoNotCreateFalseSamples()
    {
        var start = new DateTimeOffset(2026, 7, 12, 8, 0, 0, TimeSpan.Zero);
        QuotaHistoryPoint[] history =
        [
            new(start, 90),
            new(start, 85),
            new(start.AddMinutes(15), 82),
            new(start.AddMinutes(30), 79),
        ];

        var prediction = QuotaPredictor.Predict(history, start.AddHours(5));

        Assert.Equal(3, prediction.SampleCount);
    }

    [Fact]
    public void ResetBeforeExhaustionRemovesExhaustionClaim()
    {
        var start = new DateTimeOffset(2026, 7, 12, 8, 0, 0, TimeSpan.Zero);
        var history = Enumerable.Range(0, 5)
            .Select(index => new QuotaHistoryPoint(start.AddMinutes(index * 15), 90 - index * 2))
            .ToArray();

        var prediction = QuotaPredictor.Predict(history, start.AddHours(2));

        Assert.Null(prediction.EstimatedExhaustionAt);
        Assert.Null(prediction.EstimatedExhaustionEarliest);
        Assert.Null(prediction.EstimatedExhaustionLatest);
    }
}

public class QuotaAdvisorTests
{
    [Fact]
    public void LowQuotaWithoutUrgencyIsAmbientOnly()
    {
        var now = DateTimeOffset.UtcNow;
        var advice = QuotaAdvisor.Evaluate(
            Snapshot(now, 20),
            new(0, null, PredictionConfidence.None, 0),
            new(false, false, false, false),
            now);

        Assert.Equal(NotificationLevel.Ambient, advice.Level);
        Assert.Null(advice.Message);
    }

    [Fact]
    public void OneMinuteRuleStaysSilent()
    {
        var now = DateTimeOffset.UtcNow;
        var snapshot = new QuotaSnapshot(
            now,
            "test",
            new QuotaWindow(99, 300, now.AddSeconds(45)),
            new QuotaWindow(20, 10080, now.AddDays(5)),
            DataFreshness.Fresh);

        var advice = QuotaAdvisor.Evaluate(
            snapshot,
            new(0, null, PredictionConfidence.None, 0),
            new(false, false, false, false),
            now);

        Assert.Equal(NotificationLevel.Silent, advice.Level);
    }

    [Fact]
    public void CriticalQuotaWithTimeToActIsActive()
    {
        var now = DateTimeOffset.UtcNow;
        var snapshot = new QuotaSnapshot(
            now,
            "test",
            new QuotaWindow(94, 300, now.AddHours(3)),
            new QuotaWindow(20, 10080, now.AddDays(5)),
            DataFreshness.Fresh);

        var advice = QuotaAdvisor.Evaluate(
            snapshot,
            new(0, null, PredictionConfidence.None, 0),
            new(false, false, false, false),
            now);

        Assert.Equal(NotificationLevel.Active, advice.Level);
    }

    [Fact]
    public void MissingFiveHourWindowNeverTriggersAdvice()
    {
        var now = DateTimeOffset.UtcNow;
        var snapshot = new QuotaSnapshot(
            now,
            "test",
            null,
            new QuotaWindow(41, 10080, now.AddDays(5)),
            DataFreshness.Fresh);

        var advice = QuotaAdvisor.Evaluate(
            snapshot,
            new(0, null, PredictionConfidence.None, 0),
            new(false, false, false, false),
            now);

        Assert.Equal(NotificationLevel.Silent, advice.Level);
    }

    [Fact]
    public void FullScreenSuppressesCriticalAdvice()
    {
        var now = DateTimeOffset.UtcNow;
        var advice = QuotaAdvisor.Evaluate(
            Snapshot(now, 6),
            new(0, null, PredictionConfidence.None, 0),
            new(true, false, false, false),
            now);

        Assert.Equal(NotificationLevel.Silent, advice.Level);
    }

    [Fact]
    public void CodingDowngradesCriticalAdviceToPassive()
    {
        var now = DateTimeOffset.UtcNow;
        var advice = QuotaAdvisor.Evaluate(
            Snapshot(now, 6),
            new(0, null, PredictionConfidence.None, 0),
            new(false, false, false, false, true),
            now);

        Assert.Equal(NotificationLevel.Passive, advice.Level);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void PresentingOrDoNotDisturbStaysSilent(bool presenting, bool doNotDisturb)
    {
        var now = DateTimeOffset.UtcNow;
        var advice = QuotaAdvisor.Evaluate(
            Snapshot(now, 6),
            new(0, null, PredictionConfidence.None, 0),
            new(false, presenting, doNotDisturb, false),
            now);

        Assert.Equal(NotificationLevel.Silent, advice.Level);
    }

    [Fact]
    public void LowConfidencePredictionCannotProduceActiveAdvice()
    {
        var now = DateTimeOffset.UtcNow;
        var advice = QuotaAdvisor.Evaluate(
            Snapshot(now, 40),
            new(20, now.AddHours(2), PredictionConfidence.Low, 3),
            new(false, false, false, false),
            now);

        Assert.NotEqual(NotificationLevel.Active, advice.Level);
    }

    private static QuotaSnapshot Snapshot(DateTimeOffset now, int remaining) => new(
        now,
        "test",
        new(100 - remaining, 300, now.AddHours(3)),
        new(20, 10080, now.AddDays(5)),
        DataFreshness.Fresh);
}
