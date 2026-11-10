namespace CodexGuardian.Core;

public sealed record WeeklyUsageBaseline(double? PercentPerDay, int CompleteDays, string Reason,
    double AverageObservedHours = 24)
{
    public bool IsPartialDay => Reason == "observed-partial-days";
}
public sealed record PlannedUsageForecast(DateOnly? ReserveCrossingDate, double? ExpectedConsumption, int EvidenceDays);

public static class WeeklyUsageForecaster
{
    // A complete day needs observations near both midnights. Missing/offline days
    // are excluded, never counted as zero. Restrict evidence to one account/window.
    public static WeeklyUsageBaseline Measure(IReadOnlyList<QuotaHistoryEntry> history,
        QuotaSnapshot snapshot, DateTimeOffset now, TimeZoneInfo timeZone)
    {
        if (string.IsNullOrWhiteSpace(snapshot.ScopeKey) || snapshot.Weekly is not { } weekly ||
            snapshot.GetWeeklyFreshness(now) != DataFreshness.Fresh || weekly.ResetsAt <= now)
            return new(null, 0, "quota-unavailable");
        var points = history.Where(entry => entry.ScopeKey == snapshot.ScopeKey &&
                entry.WeeklyResetsAt == weekly.ResetsAt && entry.WeeklyRemainingPercent is >= 0 and <= 100 &&
                entry.WeeklyObservedAt <= now)
            .GroupBy(entry => entry.WeeklyObservedAt!.Value)
            .Where(group => group.Select(entry => entry.WeeklyRemainingPercent).Distinct().Count() == 1)
            .Select(group => group.First()).OrderBy(entry => entry.WeeklyObservedAt).ToArray();
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(now, timeZone).DateTime);
        var totals = new List<double>();
        for (var date = today.AddDays(-7); date < today; date = date.AddDays(1))
        {
            var startLocal = date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);
            var endLocal = date.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);
            if (timeZone.IsInvalidTime(startLocal) || timeZone.IsInvalidTime(endLocal) ||
                timeZone.IsAmbiguousTime(startLocal) || timeZone.IsAmbiguousTime(endLocal)) continue;
            var start = new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(startLocal, timeZone));
            var end = new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(endLocal, timeZone));
            var first = points.FirstOrDefault(point => point.WeeklyObservedAt >= start && point.WeeklyObservedAt <= start.AddMinutes(30));
            var last = points.LastOrDefault(point => point.WeeklyObservedAt >= end.AddMinutes(-30) && point.WeeklyObservedAt <= end);
            if (first is null || last is null) continue;
            var segment = points.Where(point => point.WeeklyObservedAt >= first.WeeklyObservedAt &&
                point.WeeklyObservedAt <= last.WeeklyObservedAt).ToArray();
            if (segment.Zip(segment.Skip(1)).Any(pair => pair.Second.WeeklyRemainingPercent > pair.First.WeeklyRemainingPercent)) continue;
            totals.Add(first.WeeklyRemainingPercent!.Value - last.WeeklyRemainingPercent!.Value);
        }
        if (totals.Count >= 2) return new(totals.Average(), totals.Count, "observed-calendar-days");

        // Daytime-only users can still inspect observed daily segments. Do not
        // extrapolate missing hours, or claim these segments cover full days.
        var segments = new List<(double Consumption, double Hours)>();
        foreach (var group in points.GroupBy(point => DateOnly.FromDateTime(
                     TimeZoneInfo.ConvertTime(point.WeeklyObservedAt!.Value, timeZone).DateTime)))
        {
            if (group.Key >= today) continue;
            var dayPoints = group.OrderBy(point => point.WeeklyObservedAt).ToArray();
            var first = dayPoints[0];
            var last = dayPoints[^1];
            var hours = (last.WeeklyObservedAt!.Value - first.WeeklyObservedAt!.Value).TotalHours;
            if (hours < 4 || dayPoints.Zip(dayPoints.Skip(1)).Any(pair =>
                    pair.Second.WeeklyRemainingPercent > pair.First.WeeklyRemainingPercent)) continue;
            segments.Add((first.WeeklyRemainingPercent!.Value - last.WeeklyRemainingPercent!.Value, hours));
        }
        return segments.Count >= 2
            ? new(segments.Average(segment => segment.Consumption), segments.Count, "observed-partial-days",
                segments.Average(segment => segment.Hours))
            : new(null, totals.Count, "insufficient-complete-days");
    }

    public static PlannedUsageForecast Project(WeeklyUsageBaseline baseline,
        IReadOnlyList<PlannedWorkDay> days, double availablePercent)
    {
        if (baseline.PercentPerDay is not { } daily || !double.IsFinite(daily) || daily < 0 ||
            !double.IsFinite(availablePercent) || availablePercent < 0 || days.Count == 0 ||
            days.Any(day => !double.IsFinite(day.Weight) || day.Weight <= 0 || day.Weight > 10))
            return new(null, null, baseline.CompleteDays);
        double total = 0;
        DateOnly? crossing = null;
        foreach (var day in days.OrderBy(day => day.Date))
        {
            total += daily * day.Weight;
            if (total > availablePercent && crossing is null) crossing = day.Date;
        }
        return new(crossing, total, baseline.CompleteDays);
    }
}
