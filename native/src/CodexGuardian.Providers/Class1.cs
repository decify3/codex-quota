using System.Text.Json;
using CodexGuardian.Core;

namespace CodexGuardian.Providers;

public sealed class CodexSessionQuotaProvider : IQuotaProvider
{
    private const int TailBytes = 4 * 1024 * 1024;
    private readonly string _sessionsRoot;
    private readonly TimeProvider _timeProvider;
    private readonly Dictionary<string, CachedFileSnapshot> _fileCache = new(StringComparer.OrdinalIgnoreCase);

    public CodexSessionQuotaProvider(string? sessionsRoot = null, TimeProvider? timeProvider = null)
    {
        _sessionsRoot = sessionsRoot ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".codex",
            "sessions");
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public string Name => "Codex local session metadata";

    public async Task<QuotaReadResult> ReadAsync(CancellationToken cancellationToken = default)
    {
        if (!Directory.Exists(_sessionsRoot))
            return QuotaReadResult.Failure("sessions_not_found", "Codex session directory was not found.");

        string[] files;
        try
        {
            files = Directory.GetFiles(_sessionsRoot, "rollout-*.jsonl", SearchOption.AllDirectories)
                .OrderByDescending(File.GetLastWriteTimeUtc)
                .Take(12)
                .ToArray();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return QuotaReadResult.Failure("sessions_unreadable", exception.Message);
        }

        var activeFiles = files.ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var cachedFile in _fileCache.Keys.Where(file => !activeFiles.Contains(file)).ToArray())
            _fileCache.Remove(cachedFile);

        QuotaSnapshot? aggregate = null;
        foreach (var file in files)
        {
            var snapshot = await ReadLatestFromFileAsync(file, cancellationToken).ConfigureAwait(false);
            aggregate = Merge(aggregate, snapshot, _timeProvider.GetUtcNow());
        }

        if (aggregate is not null) return QuotaReadResult.Success(aggregate);

        return QuotaReadResult.Failure(
            "quota_metadata_not_found",
            "No Codex rate-limit metadata was found. Run a Codex task, then refresh.");
    }

    private async Task<QuotaSnapshot?> ReadLatestFromFileAsync(
        string file,
        CancellationToken cancellationToken)
    {
        var fileInfo = new FileInfo(file);
        if (_fileCache.TryGetValue(file, out var cached) &&
            cached.Length == fileInfo.Length &&
            cached.LastWriteTimeUtc == fileInfo.LastWriteTimeUtc)
            return cached.Snapshot;

        QuotaSnapshot? latest = null;
        try
        {
            await using var stream = new FileStream(
                file,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete,
                bufferSize: 4096,
                options: FileOptions.Asynchronous | FileOptions.SequentialScan);
            var start = Math.Max(0, stream.Length - TailBytes);
            stream.Seek(start, SeekOrigin.Begin);
            using var reader = new StreamReader(stream);

            if (start > 0)
                await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);

            while (await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false) is { } line)
            {
                if (!line.Contains("\"rate_limits\"", StringComparison.Ordinal)) continue;
                if (SessionRateLimitParser.TryParse(line, _timeProvider.GetUtcNow(), out var parsed))
                    latest = Merge(latest, parsed, _timeProvider.GetUtcNow());
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return null;
        }

        if (latest is not null)
            _fileCache[file] = new(fileInfo.Length, fileInfo.LastWriteTimeUtc, latest);
        return latest;
    }

    internal static QuotaSnapshot? Merge(QuotaSnapshot? current, QuotaSnapshot? candidate, DateTimeOffset now)
    {
        if (candidate is null) return current;
        if (current is null) return candidate with { Freshness = QuotaSnapshot.GetFreshness(candidate.CapturedAt, now) };
        if (current.ScopeKey is not null || candidate.ScopeKey is not null)
        {
            if (current.ScopeKey != candidate.ScopeKey)
            {
                var latest = candidate.CapturedAt >= current.CapturedAt ? candidate : current;
                return latest with { Freshness = QuotaSnapshot.GetFreshness(latest.CapturedAt, now) };
            }
        }
        if (candidate.Source == "Codex app-server rate limits" && candidate.CapturedAt >= current.CapturedAt)
            return candidate with { Freshness = QuotaSnapshot.GetFreshness(candidate.CapturedAt, now) };

        var useCandidateFiveHour = candidate.FiveHour is not null &&
            (current.FiveHour is null || candidate.FiveHourObservedAt >= current.FiveHourObservedAt);
        var useCandidateWeekly = candidate.Weekly is not null &&
            (current.Weekly is null || candidate.WeeklyObservedAt >= current.WeeklyObservedAt);
        var capturedAt = current.CapturedAt >= candidate.CapturedAt ? current.CapturedAt : candidate.CapturedAt;
        var newer = candidate.CapturedAt >= current.CapturedAt ? candidate : current;

        return new(
            capturedAt,
            newer.Source,
            useCandidateFiveHour ? candidate.FiveHour : current.FiveHour,
            useCandidateWeekly ? candidate.Weekly : current.Weekly,
            QuotaSnapshot.GetFreshness(capturedAt, now),
            newer.PlanType ?? current.PlanType ?? candidate.PlanType,
            useCandidateFiveHour ? candidate.FiveHourObservedAt : current.FiveHourObservedAt,
            useCandidateWeekly ? candidate.WeeklyObservedAt : current.WeeklyObservedAt,
            newer.ScopeKey);
    }

    private sealed record CachedFileSnapshot(long Length, DateTime LastWriteTimeUtc, QuotaSnapshot Snapshot);
}

public static class SessionRateLimitParser
{
    public static bool TryParse(string line, DateTimeOffset now, out QuotaSnapshot? snapshot)
    {
        snapshot = null;
        try
        {
            using var document = JsonDocument.Parse(line);
            var root = document.RootElement;
            if (!root.TryGetProperty("payload", out var payload) ||
                !payload.TryGetProperty("rate_limits", out var limits) ||
                limits.ValueKind != JsonValueKind.Object)
                return false;

            QuotaWindow? fiveHour = null;
            QuotaWindow? weekly = null;
            foreach (var propertyName in new[] { "primary", "secondary" })
            {
                if (!limits.TryGetProperty(propertyName, out var element) ||
                    !TryReadWindow(element, out var window)) continue;
                if (window!.WindowMinutes == 300) fiveHour = window;
                else if (window.WindowMinutes == 10080) weekly = window;
            }
            if (fiveHour is null && weekly is null) return false;

            var capturedAt = ReadTimestamp(root, now);
            var planType = limits.TryGetProperty("plan_type", out var plan) && plan.ValueKind == JsonValueKind.String
                ? plan.GetString()
                : null;

            snapshot = new(
                capturedAt,
                "Codex local session metadata",
                fiveHour!,
                weekly!,
                QuotaSnapshot.GetFreshness(capturedAt, now),
                planType,
                fiveHour is null ? null : capturedAt,
                weekly is null ? null : capturedAt);
            return true;
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException or ArgumentOutOfRangeException)
        {
            return false;
        }
    }

    private static bool TryReadWindow(JsonElement element, out QuotaWindow? window)
    {
        window = null;
        if (element.ValueKind != JsonValueKind.Object ||
            !element.TryGetProperty("used_percent", out var usedElement) ||
            !element.TryGetProperty("window_minutes", out var minutesElement) ||
            !element.TryGetProperty("resets_at", out var resetElement) ||
            !minutesElement.TryGetInt32(out var minutes) ||
            !resetElement.TryGetInt64(out var resetUnix) ||
            !usedElement.TryGetDouble(out var usedValue) ||
            usedValue is < 0 or > 100 ||
            minutes <= 0)
            return false;

        var used = (int)Math.Round(usedValue, MidpointRounding.AwayFromZero);
        window = new(used, minutes, DateTimeOffset.FromUnixTimeSeconds(resetUnix));
        return true;
    }

    private static DateTimeOffset ReadTimestamp(JsonElement root, DateTimeOffset fallback)
    {
        if (root.TryGetProperty("timestamp", out var timestamp) &&
            timestamp.ValueKind == JsonValueKind.String &&
            DateTimeOffset.TryParse(timestamp.GetString(), out var parsed))
            return parsed;

        return fallback;
    }
}
