namespace CodexGuardian.Core;

public sealed record OrbSelection(QuotaWindow? Window, bool IsWeekly, DataFreshness Freshness)
{
    public static OrbSelection Select(QuotaSnapshot? snapshot, DateTimeOffset now)
    {
        if (snapshot?.OtherWindows is { Count: > 0 } other)
            return new(other[0], false, QuotaSnapshot.GetFreshness(snapshot.CapturedAt, now));
        if (snapshot?.Weekly is { } currentWeekly && currentWeekly.ResetsAt > now &&
            snapshot.GetWeeklyFreshness(now) == DataFreshness.Fresh &&
            (snapshot.FiveHour is null || snapshot.FiveHour.ResetsAt <= now ||
             snapshot.GetFiveHourFreshness(now) != DataFreshness.Fresh))
            return new(currentWeekly, true, DataFreshness.Fresh);
        if (snapshot?.FiveHour is { } shortWindow)
            return new(shortWindow, false, snapshot.GetFiveHourFreshness(now));
        if (snapshot?.Weekly is { } weekly)
            return new(weekly, true, snapshot.GetWeeklyFreshness(now));
        return new(null, false, DataFreshness.Unavailable);
    }
}
