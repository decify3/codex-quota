using System.IO;

namespace CodexGuardian.App;

internal sealed class CodexSessionActivityObserver : IDisposable
{
    private readonly FileSystemWatcher? _watcher;

    public CodexSessionActivityObserver(string? sessionsRoot = null)
    {
        sessionsRoot ??= Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".codex",
            "sessions");
        if (!Directory.Exists(sessionsRoot)) return;

        try
        {
            _watcher = new FileSystemWatcher(sessionsRoot, "*.jsonl")
            {
                IncludeSubdirectories = true,
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size,
                EnableRaisingEvents = true,
            };
            _watcher.Changed += OnActivity;
            _watcher.Created += OnActivity;
            _watcher.Renamed += OnActivity;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            _watcher?.Dispose();
            _watcher = null;
        }
    }

    public event EventHandler? ActivityObserved;

    private void OnActivity(object sender, FileSystemEventArgs e) =>
        ActivityObserved?.Invoke(this, EventArgs.Empty);

    public void Dispose()
    {
        if (_watcher is not null)
        {
            _watcher.EnableRaisingEvents = false;
            _watcher.Changed -= OnActivity;
            _watcher.Created -= OnActivity;
            _watcher.Renamed -= OnActivity;
            _watcher.Dispose();
        }
        GC.SuppressFinalize(this);
    }
}
