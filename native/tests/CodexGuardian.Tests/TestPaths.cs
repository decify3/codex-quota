namespace CodexGuardian.Tests;

internal static class TestPaths
{
    private const string EnvironmentVariable = "CODEX_GUARDIAN_TEST_TEMP";

    public static string Root
    {
        get
        {
            var configured = Environment.GetEnvironmentVariable(EnvironmentVariable);
            return string.IsNullOrWhiteSpace(configured)
                ? Path.Combine(AppContext.BaseDirectory, "test-temp")
                : Path.GetFullPath(configured);
        }
    }
}
