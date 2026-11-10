using CodexGuardian.Core;

namespace CodexGuardian.Tests;

public class QuotaMonitorTests
{
    [Fact]
    public async Task ConvertsExpectedProviderExceptionToFailure()
    {
        using var monitor = new QuotaMonitor(new ThrowingProvider(new IOException("fixture")));

        var result = await monitor.RefreshAsync();

        Assert.False(result.IsSuccess);
        Assert.Equal("provider_failure", result.ErrorCode);
        Assert.Contains("IOException", result.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DoesNotSwallowCancellation()
    {
        using var monitor = new QuotaMonitor(new ThrowingProvider(new OperationCanceledException()));

        await Assert.ThrowsAsync<OperationCanceledException>(() => monitor.RefreshAsync());
    }

    [Fact]
    public async Task ConcurrentRefreshesAreSerialized()
    {
        var provider = new CountingProvider();
        using var monitor = new QuotaMonitor(provider);

        await Task.WhenAll(Enumerable.Range(0, 20).Select(_ => monitor.RefreshAsync()));

        Assert.Equal(1, provider.MaximumConcurrency);
        Assert.Equal(20, provider.CallCount);
    }

    [Fact]
    public async Task RecoversOnTheRefreshAfterAProviderFailure()
    {
        var provider = new RecoveringProvider();
        using var monitor = new QuotaMonitor(provider);

        Assert.False((await monitor.RefreshAsync()).IsSuccess);
        Assert.True((await monitor.RefreshAsync()).IsSuccess);
        Assert.NotNull(monitor.Latest?.Snapshot);
    }

    private sealed class ThrowingProvider(Exception exception) : IQuotaProvider
    {
        public string Name => "throwing fixture";

        public Task<QuotaReadResult> ReadAsync(CancellationToken cancellationToken = default) =>
            Task.FromException<QuotaReadResult>(exception);
    }

    private sealed class CountingProvider : IQuotaProvider
    {
        private int _active;
        private int _maximumConcurrency;
        private int _callCount;

        public string Name => "counting fixture";
        public int MaximumConcurrency => _maximumConcurrency;
        public int CallCount => _callCount;

        public async Task<QuotaReadResult> ReadAsync(CancellationToken cancellationToken = default)
        {
            var active = Interlocked.Increment(ref _active);
            Interlocked.Increment(ref _callCount);
            UpdateMaximum(active);
            try
            {
                await Task.Delay(2, cancellationToken);
                var now = DateTimeOffset.UtcNow;
                return QuotaReadResult.Success(new(now, Name, new(20, 300, now.AddHours(5)), new(30, 10080, now.AddDays(7)), DataFreshness.Fresh));
            }
            finally
            {
                Interlocked.Decrement(ref _active);
            }
        }

        private void UpdateMaximum(int value)
        {
            int observed;
            do
            {
                observed = _maximumConcurrency;
                if (value <= observed) return;
            } while (Interlocked.CompareExchange(ref _maximumConcurrency, value, observed) != observed);
        }
    }

    private sealed class RecoveringProvider : IQuotaProvider
    {
        private int _calls;
        public string Name => "recovering fixture";

        public Task<QuotaReadResult> ReadAsync(CancellationToken cancellationToken = default)
        {
            if (Interlocked.Increment(ref _calls) == 1)
                throw new IOException("fixture");
            var now = DateTimeOffset.UtcNow;
            return Task.FromResult(QuotaReadResult.Success(new(
                now,
                Name,
                new(20, 300, now.AddHours(5)),
                new(30, 10080, now.AddDays(7)),
                DataFreshness.Fresh)));
        }
    }
}
