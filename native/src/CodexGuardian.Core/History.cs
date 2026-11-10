namespace CodexGuardian.Core;

public sealed record QuotaHistoryEntry(
    DateTimeOffset CapturedAt,
    int? FiveHourRemainingPercent,
    int? WeeklyRemainingPercent,
    DateTimeOffset? FiveHourResetsAt,
    DateTimeOffset? WeeklyResetsAt,
    string Source,
    DateTimeOffset? FiveHourCapturedAt = null,
    DateTimeOffset? WeeklyCapturedAt = null,
    string? ScopeKey = null)
{
    public DateTimeOffset? FiveHourObservedAt => FiveHourRemainingPercent is null ? null : FiveHourCapturedAt ?? CapturedAt;
    public DateTimeOffset? WeeklyObservedAt => WeeklyRemainingPercent is null ? null : WeeklyCapturedAt ?? CapturedAt;
}

public interface IQuotaHistoryStore : IDisposable
{
    Task AppendAsync(QuotaSnapshot snapshot, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<QuotaHistoryEntry>> ReadSinceAsync(
        DateTimeOffset since,
        CancellationToken cancellationToken = default);
}
