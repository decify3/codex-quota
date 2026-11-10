namespace CodexGuardian.Core;

public static class DemoSnapshots
{
    public static QuotaSnapshot? Create(string scenario, DateTimeOffset now) => scenario switch
    {
        "weekly" => new(now,"DEMO",null,new(1,10080,now.AddHours(40)),DataFreshness.Fresh,"pro"),
        "plan-save" => new(now,"DEMO",null,new(1,10080,new DateTimeOffset(now.UtcDateTime.Date.AddDays(2),TimeSpan.Zero)),DataFreshness.Fresh,"pro",ScopeKey:"DEMO-PLAN-ONLY"),
        "dual" => new(now,"DEMO",new(67,300,now.AddHours(2)),new(19,10080,now.AddDays(3)),DataFreshness.Fresh,"plus"),
        "stale" => new(now.AddHours(-2),"DEMO",null,new(50,10080,now.AddDays(1)),DataFreshness.Expired,"pro"),
        "reset" => new(now,"DEMO",null,new(100,10080,now.AddSeconds(-1)),DataFreshness.Fresh,"pro"),
        "missing" => null,
        _ => throw new ArgumentException("Unknown demo scenario", nameof(scenario)),
    };
}
