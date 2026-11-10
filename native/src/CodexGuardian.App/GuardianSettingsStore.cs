using System.IO;
using System.ComponentModel;
using System.Diagnostics;
using System.Security;
using System.Text.Json;
using CodexGuardian.Storage;
using Microsoft.Win32;

namespace CodexGuardian.App;

public sealed record GuardianSettings(
    int RefreshSeconds = 15,
    bool AlwaysOnTop = true,
    bool StartWithWindows = false,
    bool EnableActiveNotifications = false,
    bool DoNotDisturb = false,
    bool ReduceMotion = false,
    string StrategyMode = "Balanced",
    int HistoryRetentionDays = 90,
    string ThemeMode = "MinimalDark",
    string LanguageCode = "zh-CN",
    bool EnablePersonalizedAnalysis = true,
    bool LowPresenceMode = false,
    string AmbientEffect = "None",
    bool FollowCodex = false,
    bool PersonalAccountOnly = false)
{
    public GuardianSettings Normalize() => this with
    {
        RefreshSeconds = Math.Clamp(RefreshSeconds, 10, 300),
        StrategyMode = Enum.TryParse<CodexGuardian.Core.QuotaStrategyMode>(StrategyMode, true, out var mode)
            ? mode.ToString()
            : CodexGuardian.Core.QuotaStrategyMode.Balanced.ToString(),
        HistoryRetentionDays = Math.Clamp(HistoryRetentionDays, 30, 365),
        ThemeMode = ThemeMode is "MinimalDark" or "Glass" or "Cyber" or "Ember" or "Aurora" or "Moss" or
            "Sakura" or "Frost" or "Tidal" or "Solar"
            ? ThemeMode
            : "MinimalDark",
        AmbientEffect = AmbientEffect is "None" or "Petals" or "Snow" or "Fireflies" or "Starfall"
            ? AmbientEffect
            : "None",
        FollowCodex = FollowCodex && !StartWithWindows,
        LanguageCode = LanguageCode is "zh-CN" or "en-US"
            ? LanguageCode
            : "en-US",
    };
}

public sealed class GuardianSettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly string _path = GuardianDataPaths.GetFilePath("settings.json");

    public GuardianSettings Load()
    {
        if (!File.Exists(_path)) return new();
        try
        {
            var settings = JsonSerializer.Deserialize<GuardianSettings>(File.ReadAllText(_path)) ?? new();
            if (settings.ThemeMode == "Quiet")
                settings = settings with { ThemeMode = "MinimalDark", LowPresenceMode = true };
            return settings.Normalize();
        }
        catch (Exception exception) when (exception is IOException or JsonException or UnauthorizedAccessException)
        {
            return new();
        }
    }

    public async Task SaveAsync(GuardianSettings settings)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        var temporary = _path + ".tmp";
        await File.WriteAllTextAsync(temporary, JsonSerializer.Serialize(settings.Normalize(), JsonOptions));
        File.Move(temporary, _path, true);
    }
}

public static class StartupRegistration
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "CodexQuota";

    public static bool TryApply(bool enabled)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
            if (key is null) return false;

            if (!enabled)
            {
                key.DeleteValue(ValueName, throwOnMissingValue: false);
                return true;
            }

            var executable = Environment.ProcessPath;
            if (string.IsNullOrWhiteSpace(executable)) return false;
            // A development run hosted by dotnet.exe is not a usable startup entry.
            if (string.Equals(Path.GetFileName(executable), "dotnet.exe", StringComparison.OrdinalIgnoreCase)) return false;
            var command = $"\"{executable}\"";
            if (command.Length > 260) return false;
            key.SetValue(ValueName, command, RegistryValueKind.String);
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or SecurityException)
        {
            return false;
        }
    }
}

public static class FollowCodexRegistration
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "CodexQuotaFollowCodex";

    public static bool IsWatcherAvailable
    {
        get
        {
            var directory = Path.GetDirectoryName(Environment.ProcessPath);
            return !string.IsNullOrWhiteSpace(directory) &&
                   File.Exists(Path.Combine(directory, CodexGuardian.Core.CodexFollowProtocol.WatcherFileName));
        }
    }

    public static bool TryApply(bool enabled)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
            if (key is null) return false;

            if (!enabled)
            {
                key.DeleteValue(ValueName, throwOnMissingValue: false);
                SignalStop();
                return true;
            }

            var directory = Path.GetDirectoryName(Environment.ProcessPath);
            if (string.IsNullOrWhiteSpace(directory)) return false;
            var watcher = Path.Combine(directory, CodexGuardian.Core.CodexFollowProtocol.WatcherFileName);
            if (!File.Exists(watcher)) return false;

            var command = $"\"{watcher}\"";
            if (command.Length > 260) return false;
            key.SetValue(ValueName, command, RegistryValueKind.String);
            StartWatcher(watcher, directory);
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or SecurityException)
        {
            return false;
        }
    }

    private static void StartWatcher(string watcher, string directory)
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = watcher,
                WorkingDirectory = directory,
                UseShellExecute = false,
                CreateNoWindow = true,
            });
        }
        catch (Exception exception) when (exception is Win32Exception or InvalidOperationException)
        {
            // The registry entry is still valid and Windows can retry it at the next sign-in.
        }
    }

    private static void SignalStop()
    {
        try
        {
            using var stopEvent = EventWaitHandle.OpenExisting(CodexGuardian.Core.CodexFollowProtocol.StopEventName);
            stopEvent.Set();
        }
        catch (WaitHandleCannotBeOpenedException)
        {
            // No watcher is running.
        }
        catch (UnauthorizedAccessException)
        {
            // Do not escalate privileges merely to stop an optional user process.
        }
    }
}


