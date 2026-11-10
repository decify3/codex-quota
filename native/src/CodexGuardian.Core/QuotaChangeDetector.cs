namespace CodexGuardian.Core;

public enum QuotaWindowKind
{
    FiveHour,
    Weekly,
}

public sealed record QuotaChange(
    QuotaWindowKind Window,
    int DeltaPercentagePoints,
    bool ResetObserved);

public static class QuotaChangeDetector
{
    public static IReadOnlyList<QuotaChange> Detect(QuotaSnapshot? previous, QuotaSnapshot current)
    {
        if (previous is null) return [];

        var changes = new List<QuotaChange>(2);
        AddChange(
            changes,
            QuotaWindowKind.FiveHour,
            previous.FiveHour,
            previous.FiveHourObservedAt,
            current.FiveHour,
            current.FiveHourObservedAt);
        AddChange(
            changes,
            QuotaWindowKind.Weekly,
            previous.Weekly,
            previous.WeeklyObservedAt,
            current.Weekly,
            current.WeeklyObservedAt);
        return changes;
    }

    private static void AddChange(
        List<QuotaChange> changes,
        QuotaWindowKind kind,
        QuotaWindow? previous,
        DateTimeOffset? previousObservedAt,
        QuotaWindow? current,
        DateTimeOffset? currentObservedAt)
    {
        if (previous is null || current is null ||
            previousObservedAt is null || currentObservedAt <= previousObservedAt)
            return;

        var resetObserved = current.ResetsAt != previous.ResetsAt;
        var delta = current.RemainingPercent - previous.RemainingPercent;
        if (delta == 0 && !resetObserved) return;
        changes.Add(new(kind, delta, resetObserved));
    }
}
