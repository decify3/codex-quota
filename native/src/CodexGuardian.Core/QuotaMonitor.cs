namespace CodexGuardian.Core;

public sealed class QuotaMonitor(IQuotaProvider provider) : IDisposable
{
    private readonly SemaphoreSlim _refreshLock = new(1, 1);

    public QuotaReadResult? Latest { get; private set; }

    public async Task<QuotaReadResult> RefreshAsync(CancellationToken cancellationToken = default)
    {
        await _refreshLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            try
            {
                Latest = await provider.ReadAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or System.Text.Json.JsonException or FormatException)
            {
                Latest = QuotaReadResult.Failure("provider_failure", $"The quota provider could not read a trusted snapshot: {exception.GetType().Name}.");
            }
            return Latest;
        }
        finally
        {
            _refreshLock.Release();
        }
    }

    public void Dispose() => _refreshLock.Dispose();
}
