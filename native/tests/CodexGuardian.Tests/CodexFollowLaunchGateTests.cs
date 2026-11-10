using CodexGuardian.Core;

namespace CodexGuardian.Tests;

public sealed class CodexFollowLaunchGateTests
{
    [Theory]
    [InlineData(@"C:\Program Files\WindowsApps\OpenAI.Codex_1.0_x64__publisher\app\ChatGPT.exe", true)]
    [InlineData(@"c:/program files/windowsapps/openai.codex_1.0_x64__publisher/app/chatgpt.exe", true)]
    [InlineData(@"C:\Program Files\WindowsApps\OpenAI.ChatGPT_1.0_x64__publisher\app\ChatGPT.exe", false)]
    [InlineData(@"D:\Tools\ChatGPT.exe", false)]
    [InlineData(@"C:\Program Files\WindowsApps\OpenAI.Codex_1.0_x64__publisher\resources\codex.exe", false)]
    [InlineData(null, false)]
    public void CodexDesktopMatcherRequiresPackagedCodexDesktopPath(string? path, bool expected)
    {
        Assert.Equal(expected, CodexDesktopProcessMatcher.IsCodexDesktopExecutable(path));
    }

    [Fact]
    public void LaunchGateLaunchesOnlyOncePerCodexLifetime()
    {
        var gate = new CodexFollowLaunchGate();

        Assert.False(gate.ShouldLaunch(codexRunning: false, guardianRunning: false));
        Assert.True(gate.ShouldLaunch(codexRunning: true, guardianRunning: false));
        Assert.False(gate.ShouldLaunch(codexRunning: true, guardianRunning: false));
        Assert.False(gate.ShouldLaunch(codexRunning: false, guardianRunning: false));
        Assert.True(gate.ShouldLaunch(codexRunning: true, guardianRunning: false));
    }

    [Fact]
    public void LaunchGateDoesNotRelaunchAfterUserExitWhileCodexRemainsOpen()
    {
        var gate = new CodexFollowLaunchGate();

        Assert.False(gate.ShouldLaunch(codexRunning: true, guardianRunning: true));
        Assert.False(gate.ShouldLaunch(codexRunning: true, guardianRunning: false));
    }
}
