namespace CodexGuardian.Storage;

public static class GuardianDataPaths
{
    public const string HomeEnvironmentVariable = "CODEX_QUOTA_HOME";

    public static string HomeDirectory => ResolveHomeDirectory(
        OperatingSystem.IsWindows()
            ? Environment.GetEnvironmentVariable(HomeEnvironmentVariable, EnvironmentVariableTarget.User)
            : null,
        Environment.GetEnvironmentVariable(HomeEnvironmentVariable),
        Environment.GetEnvironmentVariable("USERPROFILE") ??
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));

    public static string RootDirectory
        => Path.Combine(HomeDirectory, "data");

    internal static string ResolveHomeDirectory(
        string? userConfiguredHome,
        string? processConfiguredHome,
        string? userProfile)
    {
        var configuredHome = string.IsNullOrWhiteSpace(userConfiguredHome)
            ? processConfiguredHome
            : userConfiguredHome;
        if (!string.IsNullOrWhiteSpace(configuredHome))
        {
            if (!Path.IsPathFullyQualified(configuredHome))
                throw new InvalidOperationException($"{HomeEnvironmentVariable} must be an absolute path.");
            return Path.GetFullPath(configuredHome);
        }

        if (string.IsNullOrWhiteSpace(userProfile))
            throw new InvalidOperationException("The current user profile directory is unavailable.");
        return Path.Combine(userProfile, ".codex-quota");
    }

    public static string GetFilePath(string fileName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        if (Path.GetFileName(fileName) != fileName)
            throw new ArgumentException("A Guardian data filename cannot contain a directory.", nameof(fileName));

        var path = Path.Combine(RootDirectory, fileName);

        return path;
    }

    private static void TryCopyFromLegacyLocalAppData(string destinationPath, string fileName)
    {
        if (File.Exists(destinationPath)) return;

        var localAppDataCandidates = new[]
        {
            Path.Combine(
                Environment.GetEnvironmentVariable("USERPROFILE") ??
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".codex-quota",
                "data"),
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            Environment.GetEnvironmentVariable("LOCALAPPDATA"),
        };
        foreach (var localAppData in localAppDataCandidates
                     .Where(value => !string.IsNullOrWhiteSpace(value))
                     .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var sourceRoot = localAppData!.EndsWith(
                $"{Path.DirectorySeparatorChar}data",
                StringComparison.OrdinalIgnoreCase)
                ? localAppData
                : Path.Combine(localAppData, "CodexGuardian");
            var sourcePath = Path.Combine(sourceRoot, fileName);
            if (string.Equals(sourcePath, destinationPath, StringComparison.OrdinalIgnoreCase)) continue;
            if (!File.Exists(sourcePath)) continue;

            try
            {
                Directory.CreateDirectory(RootDirectory);
                File.Copy(sourcePath, destinationPath, overwrite: false);
                return;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // A migration failure must not prevent the app from using its stable path.
            }
        }
    }
}

