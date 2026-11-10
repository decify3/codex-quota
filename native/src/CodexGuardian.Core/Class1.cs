namespace CodexGuardian.Core;

public enum DataFreshness
{
    Fresh,
    Stale,
    Expired,
    Unavailable,
}

public enum PredictionConfidence
{
    None,
    Low,
    Medium,
    High,
}

public enum NotificationLevel
{
    Silent,
    Ambient,
    Passive,
    Active,
}

public sealed record QuotaWindow(
    int UsedPercent,
    int WindowMinutes,
    DateTimeOffset ResetsAt)
{
    public int RemainingPercent => Math.Clamp(100 - UsedPercent, 0, 100);
}

public sealed record QuotaSnapshot(
    DateTimeOffset CapturedAt,
    string Source,
    QuotaWindow? FiveHour,
    QuotaWindow? Weekly,
    DataFreshness Freshness,
    string? PlanType = null,
    DateTimeOffset? FiveHourCapturedAt = null,
    DateTimeOffset? WeeklyCapturedAt = null,
    string? ScopeKey = null,
    IReadOnlyList<QuotaWindow>? OtherWindows = null)
{
    public DateTimeOffset? FiveHourObservedAt => FiveHour is null ? null : FiveHourCapturedAt ?? CapturedAt;
    public DateTimeOffset? WeeklyObservedAt => Weekly is null ? null : WeeklyCapturedAt ?? CapturedAt;

    public DataFreshness GetFiveHourFreshness(DateTimeOffset now) =>
        FiveHourObservedAt is { } observedAt ? GetFreshness(observedAt, now) : DataFreshness.Unavailable;

    public DataFreshness GetWeeklyFreshness(DateTimeOffset now) =>
        WeeklyObservedAt is { } observedAt ? GetFreshness(observedAt, now) : DataFreshness.Unavailable;

    public static DataFreshness GetFreshness(DateTimeOffset capturedAt, DateTimeOffset now)
    {
        var age = now - capturedAt;
        if (age < TimeSpan.Zero) return DataFreshness.Unavailable;
        if (age <= TimeSpan.FromMinutes(5)) return DataFreshness.Fresh;
        if (age <= TimeSpan.FromHours(1)) return DataFreshness.Stale;
        return DataFreshness.Expired;
    }
}

public sealed record QuotaReadResult(QuotaSnapshot? Snapshot, string? ErrorCode, string? Message)
{
    public bool IsSuccess => Snapshot is not null;

    public static QuotaReadResult Success(QuotaSnapshot snapshot) => new(snapshot, null, null);

    public static QuotaReadResult Failure(string errorCode, string message) =>
        new(null, errorCode, message);
}

public interface IQuotaProvider
{
    string Name { get; }

    Task<QuotaReadResult> ReadAsync(CancellationToken cancellationToken = default);
}
