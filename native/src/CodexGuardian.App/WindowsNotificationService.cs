using System.Globalization;
using System.Runtime.InteropServices;
using CodexGuardian.Core;
using Microsoft.Windows.AppNotifications;
using Microsoft.Windows.AppNotifications.Builder;

namespace CodexGuardian.App;

public sealed class WindowsNotificationService(
    Action viewTimeline,
    Action<DateTimeOffset> silenceUntilReset) : IDisposable
{
    private bool _isRegistered;

    public bool Register()
    {
        if (_isRegistered) return true;
        try
        {
            AppNotificationManager.Default.NotificationInvoked += NotificationInvoked;
            AppNotificationManager.Default.Register();
            _isRegistered = true;
            return true;
        }
        catch (Exception exception) when (exception is COMException or InvalidOperationException or NotSupportedException)
        {
            AppNotificationManager.Default.NotificationInvoked -= NotificationInvoked;
            return false;
        }
    }

    public bool TryShow(string message, DateTimeOffset resetAt)
    {
        if (!_isRegistered) return false;
        try
        {
            if (AppNotificationManager.Default.Setting != AppNotificationSetting.Enabled) return false;
            var notification = new AppNotificationBuilder()
                .AddArgument("action", "view")
                .AddText(Localization.Text("NotificationBrand"))
                .AddText(message)
                .AddButton(new AppNotificationButton(Localization.Text("ViewTimeline"))
                    .AddArgument("action", "view"))
                .AddButton(new AppNotificationButton(Localization.Text("SilenceUntilReset"))
                    .AddArgument("action", "silence")
                    .AddArgument("resetAt", resetAt.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture)))
                .BuildNotification();
            notification.Expiration = DateTimeOffset.Now.AddMinutes(15);
            AppNotificationManager.Default.Show(notification);
            return true;
        }
        catch (Exception exception) when (exception is COMException or InvalidOperationException or NotSupportedException)
        {
            return false;
        }
    }

    private void NotificationInvoked(AppNotificationManager sender, AppNotificationActivatedEventArgs args)
    {
        var activation = NotificationActivationParser.Parse(args.Argument);
        System.Windows.Application.Current.Dispatcher.Invoke(() =>
        {
            if (activation.Action == "silence" && activation.ResetAt is { } resetAt)
            {
                silenceUntilReset(resetAt);
                return;
            }
            viewTimeline();
        });
    }

    public void Dispose()
    {
        if (!_isRegistered) return;
        try
        {
            AppNotificationManager.Default.NotificationInvoked -= NotificationInvoked;
            AppNotificationManager.Default.Unregister();
        }
        catch (Exception exception) when (exception is COMException or InvalidOperationException or NotSupportedException)
        {
            // Shutdown must remain reliable if Windows notification registration is unavailable.
        }
        _isRegistered = false;
        GC.SuppressFinalize(this);
    }
}
