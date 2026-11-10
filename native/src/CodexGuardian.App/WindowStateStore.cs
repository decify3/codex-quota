using System.Text.Json;
using System.IO;
using CodexGuardian.Storage;

namespace CodexGuardian.App;

public sealed record GuardianWindowState(double Left, double Top, bool IsCollapsed, bool IsMoveLocked = true);

public sealed class WindowStateStore(string fileName = "window-state.json")
{
    private readonly string _path = GuardianDataPaths.GetFilePath(fileName);

    public GuardianWindowState? Load()
    {
        if (!File.Exists(_path)) return null;
        try
        {
            return JsonSerializer.Deserialize<GuardianWindowState>(File.ReadAllText(_path));
        }
        catch (Exception exception) when (exception is IOException or JsonException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    public async Task SaveAsync(GuardianWindowState state)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            await File.WriteAllTextAsync(_path, JsonSerializer.Serialize(state));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Position persistence must never interrupt quota monitoring.
        }
    }
}
