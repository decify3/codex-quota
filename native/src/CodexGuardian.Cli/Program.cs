using System.Text.Json;
using CodexGuardian.Providers;
using CodexGuardian.Core;

IQuotaProvider provider = args.Contains("--live", StringComparer.Ordinal)
    ? new CodexHybridQuotaProvider()
    : new CodexSessionQuotaProvider();
var result = await provider.ReadAsync();

if (result.Snapshot is not { } snapshot)
{
    Console.Error.WriteLine($"{result.ErrorCode}: {result.Message}");
    return 1;
}

var output = new
{
    source = snapshot.Source,
    captured_at = snapshot.CapturedAt,
    freshness = snapshot.Freshness.ToString().ToLowerInvariant(),
    plan = snapshot.PlanType,
    has_account_partition = snapshot.ScopeKey is not null,
    other_windows = snapshot.OtherWindows?.Select(window => new
    {
        window_minutes = window.WindowMinutes,
        remaining_percent = window.RemainingPercent,
        resets_at = window.ResetsAt,
    }),
    five_hour = snapshot.FiveHour is { } fiveHour ? new
    {
        remaining_percent = fiveHour.RemainingPercent,
        resets_at = fiveHour.ResetsAt,
        observed_at = snapshot.FiveHourObservedAt,
        freshness = snapshot.GetFiveHourFreshness(DateTimeOffset.UtcNow).ToString().ToLowerInvariant(),
    } : null,
    weekly = snapshot.Weekly is { } weekly ? new
    {
        remaining_percent = weekly.RemainingPercent,
        resets_at = weekly.ResetsAt,
        observed_at = snapshot.WeeklyObservedAt,
        freshness = snapshot.GetWeeklyFreshness(DateTimeOffset.UtcNow).ToString().ToLowerInvariant(),
    } : null,
};

Console.WriteLine(JsonSerializer.Serialize(output, JsonDefaults.Indented));
return 0;

internal static class JsonDefaults
{
    public static JsonSerializerOptions Indented { get; } = new() { WriteIndented = true };
}
