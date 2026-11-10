using System.Diagnostics;
using System.Runtime.InteropServices;
using CodexGuardian.Core;
using Forms = System.Windows.Forms;

namespace CodexGuardian.App;

internal static partial class UserContextDetector
{
    public static AdvisorContext Capture(GuardianSettings settings)
    {
        if (settings.DoNotDisturb)
            return new(false, false, true, false);

        try
        {
            var foreground = GetForegroundWindow();
            if (foreground == 0 || !GetWindowRect(foreground, out var rect))
                return new(false, false, false, false);

            var screen = Forms.Screen.FromHandle(foreground).Bounds;
            var isFullScreen = Math.Abs(rect.Left - screen.Left) <= 2 &&
                               Math.Abs(rect.Top - screen.Top) <= 2 &&
                               Math.Abs(rect.Right - screen.Right) <= 2 &&
                               Math.Abs(rect.Bottom - screen.Bottom) <= 2;

            _ = GetWindowThreadProcessId(foreground, out var processId);
            var processName = processId == 0
                ? string.Empty
                : Process.GetProcessById((int)processId).ProcessName;
            var isPresenting = processName.Equals("obs64", StringComparison.OrdinalIgnoreCase) ||
                               processName.Equals("obs32", StringComparison.OrdinalIgnoreCase) ||
                               processName.Equals("powerpnt", StringComparison.OrdinalIgnoreCase);
            var isCoding = processName.Equals("code", StringComparison.OrdinalIgnoreCase) ||
                           processName.Equals("cursor", StringComparison.OrdinalIgnoreCase) ||
                           processName.Equals("windsurf", StringComparison.OrdinalIgnoreCase) ||
                           processName.Equals("devenv", StringComparison.OrdinalIgnoreCase) ||
                           processName.Equals("rider64", StringComparison.OrdinalIgnoreCase) ||
                           processName.Equals("idea64", StringComparison.OrdinalIgnoreCase) ||
                           processName.Equals("pycharm64", StringComparison.OrdinalIgnoreCase) ||
                           processName.Equals("webstorm64", StringComparison.OrdinalIgnoreCase);

            return new(isFullScreen, isPresenting, false, false, isCoding);
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
        {
            return new(false, false, false, false);
        }
    }

    [LibraryImport("user32.dll")]
    private static partial nint GetForegroundWindow();

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetWindowRect(nint window, out NativeRect rect);

    [LibraryImport("user32.dll")]
    private static partial uint GetWindowThreadProcessId(nint window, out uint processId);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }
}
