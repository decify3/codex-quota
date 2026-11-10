using CodexGuardian.Core;
using System.Text.Json;

namespace CodexGuardian.Storage;

public sealed record SavedDailyPlan(string ScopeKey, DateTimeOffset ResetsAt, int ReservePercent, PlannedWorkDay[] Days,
    PlannedResetEvent? ResetEvent = null);

public sealed class DailyPlanStore(string? path = null)
{
    private readonly string _path = path ?? GuardianDataPaths.GetFilePath("daily-plan.json");

    public SavedDailyPlan? Load(string? scopeKey, DateTimeOffset resetsAt)
    {
        if (string.IsNullOrWhiteSpace(scopeKey) || !File.Exists(_path)) return null;
        try
        {
            var plan = JsonSerializer.Deserialize<SavedDailyPlan>(File.ReadAllText(_path));
            return plan?.ScopeKey == scopeKey && plan.ResetsAt == resetsAt &&
                plan.ReservePercent is >= 0 and <= 100 &&
                plan.Days is { Length: > 0 and <= 31 } &&
                plan.Days.All(day => day is not null && double.IsFinite(day.Weight) &&
                    day.Weight > 0 && day.Weight <= 10 && day.MinimumPercent is >= 0 and <= 100) &&
                plan.Days.Select(day => day.Date).Distinct().Count() == plan.Days.Length ? plan : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException) { return null; }
    }

    public void Save(SavedDailyPlan plan)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(plan.ScopeKey);
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(_path))!);
        var temporary = _path + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(plan));
        File.Move(temporary, _path, true);
    }
}
