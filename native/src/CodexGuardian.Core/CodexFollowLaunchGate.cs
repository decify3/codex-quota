namespace CodexGuardian.Core;

public sealed class CodexFollowLaunchGate
{
    private bool _armed = true;

    public bool ShouldLaunch(bool codexRunning, bool guardianRunning)
    {
        if (!codexRunning)
        {
            _armed = true;
            return false;
        }

        if (!_armed) return false;
        _armed = false;
        return !guardianRunning;
    }
}

public static class CodexDesktopProcessMatcher
{
    public static bool IsCodexDesktopExecutable(string? executablePath)
    {
        if (string.IsNullOrWhiteSpace(executablePath)) return false;
        var normalized = executablePath.Replace('/', '\\');
        return normalized.Contains("\\WindowsApps\\OpenAI.Codex_", StringComparison.OrdinalIgnoreCase) &&
               normalized.EndsWith("\\app\\ChatGPT.exe", StringComparison.OrdinalIgnoreCase);
    }
}

public static class CodexFollowProtocol
{
    public const string WatcherFileName = "CodexGuardian.Watcher.exe";
    public const string GuardianFileName = "CodexGuardian.App.exe";
    public const string WatcherMutexName = "Local\\CodexQuota.Watcher";
    public const string StopEventName = "Local\\CodexQuota.Watcher.Stop";
}

