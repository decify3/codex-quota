namespace CodexGuardian.Core;

public sealed record PlannedResetEvent(string Kind, DateTimeOffset PlannedAt, DateTimeOffset? ExpiresAt);

public static class ResetEventBudget
{
    public static DailyQuotaPlan Calculate(QuotaSnapshot? snapshot, IReadOnlyList<PlannedWorkDay> days,
        int reserve, PlannedResetEvent resetEvent, DateTimeOffset now, TimeZoneInfo zone)
    {
        if (resetEvent.Kind is not ("announcement" or "card") || resetEvent.PlannedAt <= now)
            return new([], 0, "event-invalid");
        if (resetEvent.Kind == "card" && (resetEvent.ExpiresAt is not { } expiry || expiry <= now || resetEvent.PlannedAt >= expiry))
            return new([], 0, "event-expiry");
        if (snapshot?.Weekly is not { } weekly || resetEvent.PlannedAt > weekly.ResetsAt)
            return new([], 0, "event-after-reset");
        var last = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(resetEvent.PlannedAt.AddTicks(-1), zone).DateTime);
        return DailyBudgetPlanner.Calculate(snapshot, days.Where(day => day.Date <= last).ToArray(), reserve, now, zone);
    }
}
