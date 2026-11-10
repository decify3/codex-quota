namespace CodexGuardian.Core;

public sealed class NotificationCoordinator(TimeSpan? cooldown = null)
{
    private readonly TimeSpan _cooldown = cooldown ?? TimeSpan.FromMinutes(30);
    private DateTimeOffset? _lastDeliveredAt;
    private int? _lastRemainingPercent;
    private NotificationLevel _lastLevel = NotificationLevel.Silent;
    private DateTimeOffset? _silencedUntil;

    public bool ShouldDeliver(Advice advice, QuotaSnapshot snapshot, DateTimeOffset now)
    {
        if (advice.Level < NotificationLevel.Passive) return false;
        if (snapshot.FiveHour is not { } fiveHour) return false;
        if (_silencedUntil is { } silencedUntil && now < silencedUntil) return false;

        if (_lastDeliveredAt is { } lastDelivered && now - lastDelivered < _cooldown)
        {
            var severityIncreased = advice.Level > _lastLevel;
            var quotaDroppedMaterially = _lastRemainingPercent is { } previous &&
                                         previous - fiveHour.RemainingPercent >= 5;
            if (!severityIncreased && !quotaDroppedMaterially) return false;
        }

        _lastDeliveredAt = now;
        _lastRemainingPercent = fiveHour.RemainingPercent;
        _lastLevel = advice.Level;
        return true;
    }

    public void SilenceUntil(DateTimeOffset resetAt) => _silencedUntil = resetAt;

    public void ResetSilence(DateTimeOffset now)
    {
        if (_silencedUntil is { } silencedUntil && now >= silencedUntil)
            _silencedUntil = null;
    }
}
