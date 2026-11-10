namespace CodexGuardian.Core;

public enum PlanRisk { Unknown, WithinPlan, ReserveAtRisk, MinimumUnavailable, Exhausted }

public static class PlanRiskEvaluator
{
    public static PlanRisk Evaluate(QuotaSnapshot? snapshot, WeeklyUsageBaseline baseline,
        IReadOnlyList<PlannedWorkDay>? days, int reserve, DateTimeOffset now, TimeZoneInfo timeZone,
        PlannedResetEvent? resetEvent = null)
    {
        var selected = OrbSelection.Select(snapshot, now);
        if (selected.Window is null || selected.Freshness != DataFreshness.Fresh || selected.Window.ResetsAt <= now)
            return PlanRisk.Unknown;
        if (selected.Window.RemainingPercent == 0) return PlanRisk.Exhausted;
        if (snapshot?.OtherWindows is { Count: > 0 }) return PlanRisk.Unknown;
        if (snapshot?.Weekly is { RemainingPercent: 0 } weekly && weekly.ResetsAt > now &&
            snapshot.GetWeeklyFreshness(now) == DataFreshness.Fresh) return PlanRisk.Exhausted;
        if (days is null || snapshot?.ScopeKey is null) return PlanRisk.Unknown;
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(now, timeZone).DateTime);
        var future = days.Where(day => day.Date >= today).ToArray();
        var budget = resetEvent is null ? DailyBudgetPlanner.Calculate(snapshot, future, reserve, now, timeZone)
            : ResetEventBudget.Calculate(snapshot, future, reserve, resetEvent, now, timeZone);
        if (budget.Error == "minimum-exceeds-quota") return PlanRisk.MinimumUnavailable;
        if (!budget.IsValid) return PlanRisk.Unknown;
        // A breached reserve is observable even without enough history to forecast usage.
        if (snapshot.Weekly is { } currentWeek && currentWeek.RemainingPercent < reserve)
            return PlanRisk.ReserveAtRisk;
        var forecast = WeeklyUsageForecaster.Project(baseline,
            future.Where(day => budget.Days.Any(allocation => allocation.Date == day.Date)).ToArray(), budget.AvailablePercent);
        if (forecast.ExpectedConsumption is null) return PlanRisk.Unknown;
        if (forecast.ReserveCrossingDate is not null) return PlanRisk.ReserveAtRisk;
        return baseline.IsPartialDay ? PlanRisk.Unknown : PlanRisk.WithinPlan;
    }
}
