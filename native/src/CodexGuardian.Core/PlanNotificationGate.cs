namespace CodexGuardian.Core;

/// <summary>One interruption per risk level and account/window in this app session.</summary>
public sealed class PlanNotificationGate
{
    private readonly HashSet<(string Scope, DateTimeOffset Reset, PlanRisk Risk)> _delivered = [];
    private DateTimeOffset _silencedUntil;

    public bool ShouldDeliver(PlanRisk risk, string? scope, DateTimeOffset reset,
        DateTimeOffset now, bool enabled, AdvisorContext context)
    {
        if (!enabled || string.IsNullOrWhiteSpace(scope) || reset <= now || now < _silencedUntil ||
            context.IsDoNotDisturb || context.IsFullScreen || context.IsPresenting || context.IsCoding ||
            risk is not (PlanRisk.ReserveAtRisk or PlanRisk.MinimumUnavailable or PlanRisk.Exhausted)) return false;
        _delivered.RemoveWhere(item => item.Reset <= now);
        return _delivered.Add((scope, reset, risk));
    }

    public void SilenceUntil(DateTimeOffset reset) => _silencedUntil = reset;
}
