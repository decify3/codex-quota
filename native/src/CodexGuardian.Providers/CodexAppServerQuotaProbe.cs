using System.ComponentModel;
using System.Diagnostics;
using System.Text.Json;
using CodexGuardian.Core;

namespace CodexGuardian.Providers;

internal interface ICodexQuotaProbe
{
    Task<QuotaSnapshot?> ReadAsync(CancellationToken cancellationToken = default);
}

public sealed class CodexHybridQuotaProvider : IQuotaProvider
{
    private const int MaximumStartupAttempts = 3;
    private static readonly TimeSpan StartupRetryDelay = TimeSpan.FromSeconds(10);
    private readonly CodexSessionQuotaProvider _sessionProvider;
    private readonly ICodexQuotaProbe _activeProbe;
    private readonly TimeProvider _timeProvider;
    private QuotaSnapshot? _lastActiveSnapshot;
    private DateTimeOffset _nextStartupAttemptAt = DateTimeOffset.MinValue;
    private int _startupAttempts;
    private bool _startupProbeComplete;
    private bool _forceActiveRead;
    private DateTimeOffset _lastProbeAt = DateTimeOffset.MinValue;
    private bool _personalAccountMode;
    private int _identityModeVersion;

    public void SetPersonalAccountMode(bool enabled)
    {
        if (_personalAccountMode == enabled) return;
        _personalAccountMode = enabled;
        _identityModeVersion++;
        if (_activeProbe is CodexAppServerQuotaProbe probe) probe.AllowPersonalAccountPartition = enabled;
        _lastActiveSnapshot = null;
        _forceActiveRead = true;
    }

    public CodexHybridQuotaProvider()
        : this(new CodexSessionQuotaProvider(), new CodexAppServerQuotaProbe(), TimeProvider.System)
    {
    }

    internal CodexHybridQuotaProvider(
        CodexSessionQuotaProvider sessionProvider,
        ICodexQuotaProbe activeProbe,
        TimeProvider timeProvider)
    {
        _sessionProvider = sessionProvider;
        _activeProbe = activeProbe;
        _timeProvider = timeProvider;
    }

    public string Name => "Codex app-server with local session fallback";

    public void RequestActiveRefresh() => _forceActiveRead = true;

    public async Task<QuotaReadResult> ReadAsync(CancellationToken cancellationToken = default)
    {
        var identityVersion = _identityModeVersion;
        var localResult = await _sessionProvider.ReadAsync(cancellationToken).ConfigureAwait(false);
        var now = _timeProvider.GetUtcNow();
        if (ShouldRunActiveProbe(now))
        {
            _forceActiveRead = false;
            _lastProbeAt = now;
            var activeSnapshot = await _activeProbe.ReadAsync(cancellationToken).ConfigureAwait(false);
            if (identityVersion != _identityModeVersion)
                return QuotaReadResult.Failure("identity_mode_changed", "Refresh after identity mode changed.");
            if (activeSnapshot is not null)
            {
                _lastActiveSnapshot = activeSnapshot;
                _startupProbeComplete = true;
            }
            else
            {
                _startupAttempts++;
                _nextStartupAttemptAt = now + StartupRetryDelay;
            }
        }

        // Session events carry no verified current account context. Never merge
        // them into a live account snapshot, even when their timestamps are newer.
        var merged = _lastActiveSnapshot is { } live
            ? live with { Freshness = QuotaSnapshot.GetFreshness(live.CapturedAt, _timeProvider.GetUtcNow()) }
            : localResult.Snapshot;
        return merged is null ? localResult : QuotaReadResult.Success(merged);
    }

    private bool ShouldRunActiveProbe(DateTimeOffset now)
    {
        if (_forceActiveRead) return true;
        if (_startupProbeComplete) return now - _lastProbeAt >= TimeSpan.FromMinutes(2);
        // Login and network failures are recoverable. After the initial burst,
        // keep retrying slowly instead of permanently falling back to old logs.
        if (_startupAttempts >= MaximumStartupAttempts)
            return now - _lastProbeAt >= TimeSpan.FromMinutes(2);
        if (now < _nextStartupAttemptAt)
            return false;
        // Even fresh local data does not establish the currently signed-in account.
        return true;
    }
}

internal sealed class CodexAppServerQuotaProbe(TimeSpan? timeout = null) : ICodexQuotaProbe
{
    public bool AllowPersonalAccountPartition { get; set; }
    private readonly TimeSpan _timeout = timeout ?? TimeSpan.FromSeconds(8);

    public async Task<QuotaSnapshot?> ReadAsync(CancellationToken cancellationToken = default)
    {
        var executable = FindRunningCodexExecutable();
        if (executable is null) return null;

        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(_timeout);
        using var process = new Process
        {
            StartInfo = new()
            {
                FileName = executable,
                WorkingDirectory = Path.GetDirectoryName(executable)!,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            },
        };
        process.StartInfo.ArgumentList.Add("app-server");
        process.StartInfo.ArgumentList.Add("--stdio");

        Task<string>? stderrDrain = null;
        var started = false;
        try
        {
            if (!process.Start()) return null;
            started = true;
            stderrDrain = process.StandardError.ReadToEndAsync(timeoutSource.Token);
            await SendAsync(process, new
            {
                id = 1,
                method = "initialize",
                @params = new
                {
                    clientInfo = new { name = "codex-guardian", version = "1.0" },
                    capabilities = new { experimentalApi = true },
                },
            }, timeoutSource.Token).ConfigureAwait(false);

            var initialized = false;
            string? accountPlan = null;
            string? scopeKey = null;
            while (await process.StandardOutput.ReadLineAsync(timeoutSource.Token).ConfigureAwait(false) is { } line)
            {
                using var document = JsonDocument.Parse(line);
                var root = document.RootElement;
                if (!root.TryGetProperty("id", out var id) || !id.TryGetInt32(out var requestId)) continue;
                if (root.TryGetProperty("error", out _)) return null;

                if (requestId == 1 && !initialized)
                {
                    await SendAsync(process, new { method = "initialized" }, timeoutSource.Token).ConfigureAwait(false);
                    await SendAsync(process, new { id = 3, method = "account/read", @params = new { refreshToken = false } }, timeoutSource.Token).ConfigureAwait(false);
                    initialized = true;
                }
                else if (requestId == 3)
                {
                    var account = AccountMetadataParser.Parse(line, AllowPersonalAccountPartition);
                    accountPlan = account.PlanType;
                    scopeKey = account.ScopeKey;
                    await SendAsync(process, new { id = 2, method = "account/rateLimits/read", @params = (object?)null }, timeoutSource.Token).ConfigureAwait(false);
                }
                else if (requestId == 2)
                {
                    return AppServerRateLimitParser.TryParseResponse(line, DateTimeOffset.UtcNow, out var snapshot)
                        ? snapshot! with { PlanType = snapshot.PlanType ?? accountPlan, ScopeKey = scopeKey }
                        : null;
                }
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return null;
        }
        catch (Exception exception) when (exception is Win32Exception or IOException or InvalidOperationException or JsonException)
        {
            return null;
        }
        finally
        {
            if (started) await StopProcessAsync(process).ConfigureAwait(false);
            if (stderrDrain is not null)
            {
                try { await stderrDrain.WaitAsync(TimeSpan.FromSeconds(1), CancellationToken.None).ConfigureAwait(false); }
                catch (Exception exception) when (exception is TimeoutException or OperationCanceledException) { }
            }
        }
        return null;
    }

    private static async Task SendAsync(Process process, object message, CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize(message);
        await process.StandardInput.WriteLineAsync(json.AsMemory(), cancellationToken).ConfigureAwait(false);
        await process.StandardInput.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task StopProcessAsync(Process process)
    {
        if (!process.HasExited)
        {
            process.StandardInput.Close();
            try
            {
                await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(1)).ConfigureAwait(false);
            }
            catch (TimeoutException)
            {
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync().ConfigureAwait(false);
            }
        }
    }

    private static string? FindRunningCodexExecutable()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        Process[] processes;
        try { processes = Process.GetProcessesByName("codex"); }
        catch (InvalidOperationException) { return null; }

        try
        {
            foreach (var process in processes)
            {
                try
                {
                    var path = process.MainModule?.FileName;
                    if (path is null) continue;
                    var fullPath = Path.GetFullPath(path);
                    if (IsTrustedCodexExecutablePath(fullPath, localAppData))
                        return fullPath;
                }
                catch (Exception exception) when (exception is Win32Exception or InvalidOperationException or NotSupportedException)
                {
                    // Ignore inaccessible or exiting processes; never fall back to an untrusted path.
                }
            }
        }
        finally
        {
            foreach (var process in processes) process.Dispose();
        }
        return null;
    }

    internal static bool IsTrustedCodexExecutablePath(string? executablePath, string localAppData)
    {
        if (string.IsNullOrWhiteSpace(executablePath) || string.IsNullOrWhiteSpace(localAppData)) return false;
        try
        {
            var trustedRoot = Path.GetFullPath(Path.Combine(localAppData, "OpenAI", "Codex", "bin")) + Path.DirectorySeparatorChar;
            var fullPath = Path.GetFullPath(executablePath);
            if (!string.Equals(Path.GetFileName(fullPath), "codex.exe", StringComparison.OrdinalIgnoreCase)) return false;
            if (fullPath.StartsWith(trustedRoot, StringComparison.OrdinalIgnoreCase)) return true;

            // Store installations use the protected WindowsApps root rather than LocalAppData.
            // Require the exact package publisher family and resource path; never search PATH.
            var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            if (string.IsNullOrWhiteSpace(programFiles)) return false;
            var storeRoot = Path.GetFullPath(Path.Combine(programFiles, "WindowsApps")) + Path.DirectorySeparatorChar;
            if (!fullPath.StartsWith(storeRoot, StringComparison.OrdinalIgnoreCase)) return false;
            var relative = fullPath[storeRoot.Length..];
            return System.Text.RegularExpressions.Regex.IsMatch(relative,
                @"\AOpenAI\.Codex_\d+\.\d+\.\d+\.\d+_(?:x64|arm64)__2p2nqsd0c76g0\\app\\resources\\codex\.exe\z",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.CultureInvariant);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }
    }
}

internal static class AccountMetadataParser
{
    public static (string? PlanType, string? ScopeKey) Parse(string response, bool allowPersonalAccountPartition = false)
    {
        using var document = JsonDocument.Parse(response);
        if (!document.RootElement.TryGetProperty("result", out var result) ||
            !result.TryGetProperty("account", out var account) || account.ValueKind != JsonValueKind.Object)
            return (null, null);
        var plan = account.TryGetProperty("planType", out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
        // Prefer an explicit ID. Name-based context is a conservative local partition,
        // not a claim that the service supplied a stable workspace ID.
        var id = account.TryGetProperty("chatgptAccountId", out var identity) && identity.ValueKind == JsonValueKind.String ? identity.GetString() : null;
        var email = account.TryGetProperty("email", out var emailValue) && emailValue.ValueKind == JsonValueKind.String ? emailValue.GetString() : null;
        var workspace = account.TryGetProperty("workspaceName", out var nameValue) && nameValue.ValueKind == JsonValueKind.String ? nameValue.GetString() : null;
        var identityParts = !string.IsNullOrWhiteSpace(id) ? new[] { "id", id, plan }
            : !string.IsNullOrWhiteSpace(email) && !string.IsNullOrWhiteSpace(workspace)
                ? new[] { "name-context", email.Trim().ToLowerInvariant(), workspace, plan }
                : allowPersonalAccountPartition && !string.IsNullOrWhiteSpace(email) && !string.IsNullOrWhiteSpace(plan)
                    ? new[] { "user-confirmed-personal", email.Trim().ToLowerInvariant(), plan } : null;
        var key = identityParts is null ? null : Convert.ToHexString(
            System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(JsonSerializer.Serialize(identityParts))));
        return (plan, key);
    }
}

public static class AppServerRateLimitParser
{
    public static bool TryParseResponse(string response, DateTimeOffset now, out QuotaSnapshot? snapshot)
    {
        snapshot = null;
        try
        {
            using var document = JsonDocument.Parse(response);
            var root = document.RootElement;
            if (!root.TryGetProperty("result", out var result) || result.ValueKind != JsonValueKind.Object)
                return false;

            JsonElement limits;
            if (result.TryGetProperty("rateLimitsByLimitId", out var byId) &&
                byId.ValueKind == JsonValueKind.Object &&
                byId.TryGetProperty("codex", out var codexLimits) &&
                codexLimits.ValueKind == JsonValueKind.Object)
                limits = codexLimits;
            else if (result.TryGetProperty("rateLimits", out var legacyLimits) && legacyLimits.ValueKind == JsonValueKind.Object)
                limits = legacyLimits;
            else
                return false;

            QuotaWindow? fiveHour = null;
            QuotaWindow? weekly = null;
            var otherWindows = new List<QuotaWindow>();
            foreach (var propertyName in new[] { "primary", "secondary" })
            {
                if (!limits.TryGetProperty(propertyName, out var element) || !TryReadWindow(element, out var window))
                    continue;
                if (window!.WindowMinutes == 300) fiveHour = window;
                else if (window.WindowMinutes == 10080) weekly = window;
                else otherWindows.Add(window);
            }
            if (fiveHour is null && weekly is null && otherWindows.Count == 0) return false;

            var planType = limits.TryGetProperty("planType", out var plan) && plan.ValueKind == JsonValueKind.String
                ? plan.GetString()
                : null;
            snapshot = new(
                now,
                "Codex app-server rate limits",
                fiveHour,
                weekly,
                DataFreshness.Fresh,
                planType,
                fiveHour is null ? null : now,
                weekly is null ? null : now,
                OtherWindows: otherWindows.Count == 0 ? null : otherWindows.ToArray());
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
            !element.TryGetProperty("usedPercent", out var usedElement) ||
            !element.TryGetProperty("windowDurationMins", out var minutesElement) ||
            !element.TryGetProperty("resetsAt", out var resetElement) ||
            !usedElement.TryGetInt32(out var used) ||
            !minutesElement.TryGetInt32(out var minutes) ||
            !resetElement.TryGetInt64(out var resetUnix) ||
            used is < 0 or > 100 ||
            minutes <= 0)
            return false;

        window = new(used, minutes, DateTimeOffset.FromUnixTimeSeconds(resetUnix));
        return true;
    }
}
