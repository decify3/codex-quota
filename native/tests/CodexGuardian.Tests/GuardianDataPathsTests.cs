using CodexGuardian.Storage;

namespace CodexGuardian.Tests;

public sealed class GuardianDataPathsTests
{
    [Fact]
    public void ResolveHomeDirectoryPrefersUserConfiguration()
    {
        var result = GuardianDataPaths.ResolveHomeDirectory(
            "D:\\CodexGuardian\\runtime",
            "E:\\Ignored",
            "C:\\Users\\tester");

        Assert.Equal("D:\\CodexGuardian\\runtime", result);
    }

    [Fact]
    public void ResolveHomeDirectoryFallsBackToNonVirtualizedUserProfilePath()
    {
        var result = GuardianDataPaths.ResolveHomeDirectory(null, null, "C:\\Users\\tester");

        Assert.Equal("C:\\Users\\tester\\.codex-quota", result);
    }

    [Fact]
    public void ResolveHomeDirectoryRejectsRelativeConfiguration()
    {
        Assert.Throws<InvalidOperationException>(() =>
            GuardianDataPaths.ResolveHomeDirectory(null, "relative\\guardian", "C:\\Users\\tester"));
    }

    [Theory]
    [InlineData("..\\settings.json")]
    [InlineData("nested/settings.json")]
    public void GetFilePathRejectsDirectoryTraversal(string fileName)
    {
        Assert.Throws<ArgumentException>(() => GuardianDataPaths.GetFilePath(fileName));
    }
}

