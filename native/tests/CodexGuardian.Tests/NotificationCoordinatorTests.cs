using CodexGuardian.Core;

namespace CodexGuardian.Tests;

public class NotificationCoordinatorTests
{
    [Fact]
    public void SuppressesAmbientAdvice()
    {
        var coordinator = new NotificationCoordinator();
        var now = DateTimeOffset.UtcNow;

        Assert.False(coordinator.ShouldDeliver(
            new(NotificationLevel.Ambient, "low"),
            CreateSnapshot(now, 20),
            now));
    }

    [Fact]
    public void MergesRepeatedAdviceDuringCooldown()
    {
        var coordinator = new NotificationCoordinator(TimeSpan.FromMinutes(30));
        var now = DateTimeOffset.UtcNow;
        var advice = new Advice(NotificationLevel.Passive, "trend");

        Assert.True(coordinator.ShouldDeliver(advice, CreateSnapshot(now, 20), now));
        Assert.False(coordinator.ShouldDeliver(advice, CreateSnapshot(now.AddMinutes(5), 18), now.AddMinutes(5)));
    }

    [Fact]
    public void AllowsMaterialWorseningDuringCooldown()
    {
        var coordinator = new NotificationCoordinator(TimeSpan.FromMinutes(30));
        var now = DateTimeOffset.UtcNow;
        var advice = new Advice(NotificationLevel.Passive, "trend");

        Assert.True(coordinator.ShouldDeliver(advice, CreateSnapshot(now, 20), now));
        Assert.True(coordinator.ShouldDeliver(advice, CreateSnapshot(now.AddMinutes(5), 14), now.AddMinutes(5)));
    }

    [Fact]
    public void HonorsSilenceUntilReset()
    {
        var coordinator = new NotificationCoordinator();
        var now = DateTimeOffset.UtcNow;
        coordinator.SilenceUntil(now.AddHours(2));

        Assert.False(coordinator.ShouldDeliver(
            new(NotificationLevel.Active, "critical"),
            CreateSnapshot(now, 5),
            now));
    }

    private static QuotaSnapshot CreateSnapshot(DateTimeOffset now, int remaining) => new(
        now,
        "test",
        new(100 - remaining, 300, now.AddHours(3)),
        new(10, 10080, now.AddDays(5)),
        DataFreshness.Fresh);
}
