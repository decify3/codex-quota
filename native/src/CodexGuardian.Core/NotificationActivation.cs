using System.Globalization;

namespace CodexGuardian.Core;

public sealed record NotificationActivation(string Action, DateTimeOffset? ResetAt);

public static class NotificationActivationParser
{
    public static NotificationActivation Parse(string arguments)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var part in arguments.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var separator = part.IndexOf('=', StringComparison.Ordinal);
            if (separator <= 0) continue;
            values[Uri.UnescapeDataString(part[..separator])] = Uri.UnescapeDataString(part[(separator + 1)..]);
        }

        var action = values.TryGetValue("action", out var actionValue) ? actionValue : "view";
        DateTimeOffset? resetAt = null;
        if (values.TryGetValue("resetAt", out var resetText) &&
            long.TryParse(resetText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var resetUnix))
        {
            try
            {
                resetAt = DateTimeOffset.FromUnixTimeSeconds(resetUnix);
            }
            catch (ArgumentOutOfRangeException)
            {
                resetAt = null;
            }
        }
        return new(action, resetAt);
    }
}
