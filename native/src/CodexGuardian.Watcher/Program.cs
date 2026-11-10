using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Threading;
using CodexGuardian.Core;

namespace CodexGuardian.Watcher;

internal static class Program
{
    private static readonly TimeSpan ProbeInterval = TimeSpan.FromSeconds(5);
    private static int? TrustedCodexProcessId;
    private static DateTime TrustedCodexProcessStart;

    private static void Main()
    {
        using var mutex = new Mutex(initiallyOwned: true, CodexFollowProtocol.WatcherMutexName, out var isFirstInstance);
        if (!isFirstInstance) return;

        using var stopEvent = new EventWaitHandle(false, EventResetMode.AutoReset, CodexFollowProtocol.StopEventName);
        var guardianPath = Path.Combine(AppContext.BaseDirectory, CodexFollowProtocol.GuardianFileName);
        var launchGate = new CodexFollowLaunchGate();

        while (true)
        {
            var codexRunning = IsCodexDesktopRunning();
            var guardianRunning = IsProcessRunning("CodexGuardian.App");
            if (launchGate.ShouldLaunch(codexRunning, guardianRunning))
                TryLaunchGuardian(guardianPath);

            if (stopEvent.WaitOne(ProbeInterval)) return;
        }
    }

    private static bool IsCodexDesktopRunning()
    {
        if (IsTrustedCodexProcessRunning()) return true;

        Process[] processes;
        try
        {
            processes = Process.GetProcessesByName("ChatGPT");
        }
        catch (InvalidOperationException)
        {
            return false;
        }

        try
        {
            foreach (var process in processes)
            {
                try
                {
                    var startedAt = process.StartTime;
                    if (!CodexDesktopProcessMatcher.IsCodexDesktopExecutable(process.MainModule?.FileName)) continue;
                    TrustedCodexProcessId = process.Id;
                    TrustedCodexProcessStart = startedAt;
                    return true;
                }
                catch (Exception exception) when (exception is Win32Exception or InvalidOperationException or NotSupportedException)
                {
                    // A process may exit or deny module inspection between enumeration and probing.
                }
            }
            return false;
        }
        finally
        {
            foreach (var process in processes) process.Dispose();
        }
    }

    private static bool IsTrustedCodexProcessRunning()
    {
        if (TrustedCodexProcessId is not int processId) return false;
        try
        {
            using var process = Process.GetProcessById(processId);
            if (!process.HasExited && process.StartTime == TrustedCodexProcessStart) return true;
        }
        catch (Exception exception) when (exception is ArgumentException or Win32Exception or InvalidOperationException or NotSupportedException)
        {
            // The trusted process exited or its identifier was reused.
        }

        TrustedCodexProcessId = null;
        TrustedCodexProcessStart = default;
        return false;
    }

    private static bool IsProcessRunning(string processName, Func<Process, bool>? predicate = null)
    {
        Process[] processes;
        try
        {
            processes = Process.GetProcessesByName(processName);
        }
        catch (InvalidOperationException)
        {
            return false;
        }

        try
        {
            foreach (var process in processes)
            {
                try
                {
                    if (predicate?.Invoke(process) ?? !process.HasExited) return true;
                }
                catch (Exception exception) when (exception is Win32Exception or InvalidOperationException or NotSupportedException)
                {
                    // A process may exit or deny module inspection between enumeration and probing.
                }
            }
            return false;
        }
        finally
        {
            foreach (var process in processes) process.Dispose();
        }
    }

    private static void TryLaunchGuardian(string guardianPath)
    {
        if (!File.Exists(guardianPath)) return;
        try
        {
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = guardianPath,
                WorkingDirectory = Path.GetDirectoryName(guardianPath)!,
                UseShellExecute = false,
            });
        }
        catch (Exception exception) when (exception is Win32Exception or InvalidOperationException)
        {
            // Stay quiet and re-arm only after Codex exits; never create a relaunch loop.
        }
    }
}
