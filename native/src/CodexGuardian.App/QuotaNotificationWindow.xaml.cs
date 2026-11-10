using System.Windows;
using System.Windows.Threading;

namespace CodexGuardian.App;

public partial class QuotaNotificationWindow : Window
{
    private readonly Action _viewAction;
    private readonly Action _silenceAction;
    private readonly DispatcherTimer _dismissTimer;

    public QuotaNotificationWindow(string message, Action viewAction, Action silenceAction, bool preview = false)
    {
        InitializeComponent();
        MessageText.Text = message;
        _viewAction = viewAction;
        _silenceAction = silenceAction;
        if (preview) { ShowInTaskbar = true; ShowActivated = true; }
        _dismissTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(preview ? 120 : 15) };
        _dismissTimer.Tick += (_, _) => Close();
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        Left = SystemParameters.WorkArea.Right - Width - 18;
        Top = SystemParameters.WorkArea.Bottom - Height - 18;
        _dismissTimer.Start();
    }

    protected override void OnClosed(EventArgs e)
    {
        _dismissTimer.Stop();
        base.OnClosed(e);
    }

    private void ViewButton_Click(object sender, RoutedEventArgs e)
    {
        _viewAction();
        Close();
    }

    private void SilenceButton_Click(object sender, RoutedEventArgs e)
    {
        _silenceAction();
        Close();
    }

    private void DismissButton_Click(object sender, RoutedEventArgs e) => Close();
}
