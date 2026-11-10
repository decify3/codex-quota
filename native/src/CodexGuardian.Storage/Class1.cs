using System.Text.Json;
using System.Text;
using CodexGuardian.Core;

namespace CodexGuardian.Storage;

public sealed class JsonlQuotaHistoryStore : IQuotaHistoryStore
{
    public const long DefaultMaximumBytes = 16 * 1024 * 1024;
    private static readonly JsonSerializerOptions IndentedJsonOptions = new() { WriteIndented = true };
    private readonly string _path;
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private readonly long _maximumBytes;
    private TimeSpan _retention;
    private DateTimeOffset? _lastWrittenAt;
    private DateTimeOffset? _lastCompactedAt;

    public JsonlQuotaHistoryStore(
        string? path = null,
        TimeSpan? retention = null,
        long maximumBytes = DefaultMaximumBytes)
    {
        _path = path ?? GuardianDataPaths.GetFilePath("quota-history.jsonl");
        _retention = retention ?? TimeSpan.FromDays(90);
        _maximumBytes = maximumBytes;
        ArgumentOutOfRangeException.ThrowIfLessThan(_retention, TimeSpan.FromDays(7));
        ArgumentOutOfRangeException.ThrowIfLessThan(_maximumBytes, 1024);
    }

    public void SetRetention(TimeSpan retention)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(retention, TimeSpan.FromDays(7));
        _retention = retention;
    }

    public async Task AppendAsync(QuotaSnapshot snapshot, CancellationToken cancellationToken = default)
    {
        var entry = FromSnapshot(snapshot);
        await _writeLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_lastWrittenAt is null && File.Exists(_path))
            {
                var existing = await ReadSinceAsync(DateTimeOffset.MinValue, cancellationToken).ConfigureAwait(false);
                _lastWrittenAt = existing.Count == 0 ? null : existing[^1].CapturedAt;
            }
            if (_lastWrittenAt == entry.CapturedAt) return;
            var directory = Path.GetDirectoryName(_path);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

            var line = JsonSerializer.Serialize(entry) + Environment.NewLine;
            await File.AppendAllTextAsync(_path, line, cancellationToken).ConfigureAwait(false);
            _lastWrittenAt = entry.CapturedAt;
            var exceedsSizeBudget = new FileInfo(_path).Length > _maximumBytes;
            if (exceedsSizeBudget ||
                _lastCompactedAt is null ||
                entry.CapturedAt - _lastCompactedAt >= TimeSpan.FromDays(1))
            {
                await CompactCoreAsync(entry.CapturedAt - _retention, cancellationToken).ConfigureAwait(false);
                _lastCompactedAt = entry.CapturedAt;
            }
        }
        finally
        {
            _writeLock.Release();
        }
    }

    public async Task CompactAsync(DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        await _writeLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await CompactCoreAsync(now - _retention, cancellationToken).ConfigureAwait(false);
            _lastCompactedAt = now;
        }
        finally
        {
            _writeLock.Release();
        }
    }

    public async Task<IReadOnlyList<QuotaHistoryEntry>> ReadSinceAsync(
        DateTimeOffset since,
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_path)) return [];

        var entries = new List<QuotaHistoryEntry>();
        await using var stream = new FileStream(
            _path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete,
            bufferSize: 4096,
            useAsync: true);
        using var reader = new StreamReader(stream);

        while (await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false) is { } line)
        {
            if (TryParse(line, out var entry) && entry!.CapturedAt >= since)
                entries.Add(entry);
        }

        return entries.OrderBy(entry => entry.CapturedAt).ToArray();
    }

    public static bool TryParse(string line, out QuotaHistoryEntry? entry)
    {
        entry = null;
        try
        {
            entry = JsonSerializer.Deserialize<QuotaHistoryEntry>(line);
            if (entry is null || string.IsNullOrWhiteSpace(entry.Source)) return false;
            var fiveHourValid = entry.FiveHourRemainingPercent is >= 0 and <= 100 && entry.FiveHourResetsAt is not null;
            var fiveHourAbsent = entry.FiveHourRemainingPercent is null && entry.FiveHourResetsAt is null;
            var weeklyValid = entry.WeeklyRemainingPercent is >= 0 and <= 100 && entry.WeeklyResetsAt is not null;
            var weeklyAbsent = entry.WeeklyRemainingPercent is null && entry.WeeklyResetsAt is null;
            return (fiveHourValid || weeklyValid) && (fiveHourValid || fiveHourAbsent) && (weeklyValid || weeklyAbsent);
        }
        catch (JsonException)
        {
            return false;
        }
    }

    public async Task ExportSanitizedAsync(string destinationPath, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationPath);
        var entries = await ReadSinceAsync(DateTimeOffset.MinValue, cancellationToken).ConfigureAwait(false);
        var summary = new
        {
            schema = 1,
            generated_at = DateTimeOffset.UtcNow,
            privacy = "Quota percentages, reset times and provider labels only. No prompts, responses, code or credentials.",
            entries,
        };
        var directory = Path.GetDirectoryName(destinationPath);
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
        await File.WriteAllTextAsync(
            destinationPath,
            JsonSerializer.Serialize(summary, IndentedJsonOptions),
            cancellationToken).ConfigureAwait(false);
    }

    public async Task ClearAsync(CancellationToken cancellationToken = default)
    {
        await _writeLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var directory = Path.GetDirectoryName(_path);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
            await File.WriteAllTextAsync(_path, string.Empty, cancellationToken).ConfigureAwait(false);
            _lastWrittenAt = null;
            _lastCompactedAt = null;
        }
        finally
        {
            _writeLock.Release();
        }
    }

    private static QuotaHistoryEntry FromSnapshot(QuotaSnapshot snapshot) => new(
        snapshot.CapturedAt,
        snapshot.FiveHour?.RemainingPercent,
        snapshot.Weekly?.RemainingPercent,
        snapshot.FiveHour?.ResetsAt,
        snapshot.Weekly?.ResetsAt,
        snapshot.Source,
        snapshot.FiveHourObservedAt,
        snapshot.WeeklyObservedAt,
        snapshot.ScopeKey);

    private async Task CompactCoreAsync(DateTimeOffset cutoff, CancellationToken cancellationToken)
    {
        if (!File.Exists(_path)) return;
        var entries = await ReadSinceAsync(cutoff, cancellationToken).ConfigureAwait(false);
        var lines = entries.Select(entry => JsonSerializer.Serialize(entry)).ToArray();
        var retainedLines = new List<string>();
        var retainedBytes = 0L;
        var newlineBytes = Encoding.UTF8.GetByteCount(Environment.NewLine);
        for (var index = lines.Length - 1; index >= 0; index--)
        {
            var lineBytes = Encoding.UTF8.GetByteCount(lines[index]) + newlineBytes;
            if (retainedLines.Count > 0 && retainedBytes + lineBytes > _maximumBytes) break;
            if (lineBytes > _maximumBytes) continue;
            retainedLines.Add(lines[index]);
            retainedBytes += lineBytes;
        }
        retainedLines.Reverse();
        var content = string.Join(Environment.NewLine, retainedLines);
        if (content.Length > 0) content += Environment.NewLine;
        await File.WriteAllTextAsync(_path, content, cancellationToken).ConfigureAwait(false);
    }

    public void Dispose() => _writeLock.Dispose();
}
