using System.Text.Json;
using CodexGuardian.Core;

namespace CodexGuardian.Storage;

public sealed record NotificationDecisionEntry(
    DateTimeOffset EvaluatedAt,
    NotificationLevel Level,
    string Reason,
    string Outcome,
    DateTimeOffset FiveHourResetsAt);

public sealed class NotificationDecisionStore : IDisposable
{
    private const long MaximumLogBytes = 512 * 1024;
    private const int CompactedEntryCount = 500;
    private readonly string _path;
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private string? _lastSignature;

    public NotificationDecisionStore(string? path = null)
    {
        _path = path ?? GuardianDataPaths.GetFilePath("notification-decisions.jsonl");
    }

    public async Task<bool> TryAppendIfChangedAsync(
        Advice advice,
        QuotaSnapshot snapshot,
        bool delivered,
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        if (snapshot.FiveHour is not { } fiveHour) return false;
        var outcome = delivered ? "delivered" : "suppressed";
        var signature = $"{advice.Level}|{advice.Reason}|{outcome}|{fiveHour.ResetsAt:O}";
        await _writeLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (signature == _lastSignature) return false;
            var directory = Path.GetDirectoryName(_path);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
            var entry = new NotificationDecisionEntry(
                now,
                advice.Level,
                advice.Reason,
                outcome,
                fiveHour.ResetsAt);
            await File.AppendAllTextAsync(
                _path,
                JsonSerializer.Serialize(entry) + Environment.NewLine,
                cancellationToken).ConfigureAwait(false);
            _lastSignature = signature;
            if (new FileInfo(_path).Length > MaximumLogBytes)
                await CompactAsync(cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return false;
        }
        finally
        {
            _writeLock.Release();
        }
    }

    private async Task CompactAsync(CancellationToken cancellationToken)
    {
        var lines = await File.ReadAllLinesAsync(_path, cancellationToken).ConfigureAwait(false);
        var retained = lines.TakeLast(CompactedEntryCount);
        await File.WriteAllLinesAsync(_path, retained, cancellationToken).ConfigureAwait(false);
    }

    public void Dispose() => _writeLock.Dispose();
}
