using CodexGuardian.Core;

namespace CodexGuardian.Tests;

public class NotificationActivationTests
{
    [Fact]
    public void ParsesSilenceActionAndResetTime()
    {
        var resetAt = new DateTimeOffset(2026, 7, 13, 12, 0, 0, TimeSpan.Zero);

        var activation = NotificationActivationParser.Parse(
            $"action=silence&resetAt={resetAt.ToUnixTimeSeconds()}");

        Assert.Equal("silence", activation.Action);
        Assert.Equal(resetAt, activation.ResetAt);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-a-pair")]
    [InlineData("action=silence&resetAt=invalid")]
    public void InvalidArgumentsFailSafeToAViewAction(string arguments)
    {
        var activation = NotificationActivationParser.Parse(arguments);

        if (arguments.StartsWith("action=silence", StringComparison.Ordinal))
            Assert.Equal("silence", activation.Action);
        else
            Assert.Equal("view", activation.Action);
        Assert.Null(activation.ResetAt);
    }
}
