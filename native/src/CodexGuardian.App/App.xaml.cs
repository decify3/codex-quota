using System.Configuration;
using System.Data;
using System.Windows;
using System.Drawing;
using Forms = System.Windows.Forms;

namespace CodexGuardian.App;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : System.Windows.Application, IDisposable
{
    private static readonly string[] DemoScenarioSequence = ["dual", "stale", "reset", "missing", "weekly"];
    private Forms.NotifyIcon? _trayIcon;
    private OrbWindow? _orbWindow;
    private SettingsWindow? _settingsWindow;
    private DashboardWindow? _dashboardWindow;
    private QuotaNotificationWindow? _notificationWindow;
    private Mutex? _singleInstanceMutex;
    private EventWaitHandle? _activationEvent;
    private RegisteredWaitHandle? _activationWait;
    private Icon? _applicationIcon;
    private Forms.ContextMenuStrip? _trayMenu;
    private WindowsNotificationService? _windowsNotifications;

    public GuardianSettingsStore SettingsStore { get; } = new();
    public GuardianSettings Settings { get; private set; } = new();

    public bool IsShuttingDown { get; private set; }
    public bool IsMainWindowHiddenByUser { get; private set; }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        if (e.Args.Contains("--demo", StringComparer.Ordinal))
        {
            StartDemo(e.Args);
            return;
        }
        if (e.Args.Contains("--smoke-test", StringComparer.Ordinal))
        {
            DispatcherUnhandledException += (_, args) =>
            {
                System.IO.File.WriteAllText(CodexGuardian.Storage.GuardianDataPaths.GetFilePath("smoke-error.txt"), args.Exception.ToString());
                args.Handled = true;
                Shutdown(1);
            };
        }
        _activationEvent = new EventWaitHandle(false, EventResetMode.AutoReset, "Local\\CodexQuota.App.Show");
        _singleInstanceMutex = new Mutex(initiallyOwned: true, "Local\\CodexQuota.App", out var isFirstInstance);
        if (!isFirstInstance)
        {
            _activationEvent.Set();
            System.Windows.MessageBox.Show("额度球已经在运行。若要使用新版本，请先从旧版托盘菜单选择退出，再打开此版本。\n\nQuota Orb is already running. To use this build, exit the existing tray instance first, then launch it again.", "Codex Quota", MessageBoxButton.OK, MessageBoxImage.Information);
            Shutdown();
            return;
        }
        ShutdownMode = ShutdownMode.OnExplicitShutdown;
        Settings = SettingsStore.Load();
        Localization.Apply(Settings.LanguageCode);

        MainWindow = new MainWindow { ShowActivated = false };
        MainWindow.Loaded += HideInitialDetails;
        MainWindow.Show();
        _orbWindow = new OrbWindow(MainWindow.DataContext);
        _orbWindow.Show();
        _activationWait = ThreadPool.RegisterWaitForSingleObject(_activationEvent, (_, _) =>
        {
            if (Dispatcher.HasShutdownStarted) return;
            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (IsShuttingDown) return;
                _orbWindow?.Show();
                _orbWindow?.Activate();
            }));
        }, null, Timeout.Infinite, executeOnlyOnce: false);

        _trayMenu = CreateTrayMenu();

        _applicationIcon = string.IsNullOrWhiteSpace(Environment.ProcessPath)
            ? null
            : Icon.ExtractAssociatedIcon(Environment.ProcessPath);
        _trayIcon = new Forms.NotifyIcon
        {
            Icon = _applicationIcon ?? SystemIcons.Information,
            Text = Localization.Text("TrayTooltip"),
            Visible = true,
            ContextMenuStrip = _trayMenu,
        };
        _trayIcon.DoubleClick += (_, _) => ShowMainWindow();
        _windowsNotifications = new WindowsNotificationService(
            ShowDashboard,
            resetAt => (MainWindow as CodexGuardian.App.MainWindow)?.SilenceUntil(resetAt));
        _windowsNotifications.Register();
        if (e.Args.Contains("--smoke-test", StringComparer.Ordinal))
        {
            var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
            timer.Tick += (_, _) => { timer.Stop(); RequestExit(); };
            timer.Start();
        }
    }

    private void StartDemo(string[] args)
    {
        ShutdownMode = ShutdownMode.OnMainWindowClose;
        Localization.Apply(args.Contains("--english", StringComparer.Ordinal) ? "en-US" : "zh-CN");
        var model = new MainViewModel();
        model.ShowDemo("weekly");
        if (args.Contains("--notification", StringComparer.Ordinal))
        {
            var notification = new QuotaNotificationWindow(
                Localization.Text("DemoBanner") + "\n" + Localization.Text("RiskReserveAtRisk"),
                () => { }, () => { }, preview: true);
            MainWindow = notification;
            notification.Closed += (_, _) => model.Dispose();
            notification.Show();
            return;
        }
        if (args.Contains("--plan", StringComparer.Ordinal) || args.Contains("--plan-save-demo", StringComparer.Ordinal))
        {
            var saveDemo = args.Contains("--plan-save-demo", StringComparer.Ordinal);
            if (saveDemo) model.ShowDemo("plan-save");
            var plan = new BudgetWindow(model, saveDemo
                ? CodexGuardian.Storage.GuardianDataPaths.GetFilePath("demo-daily-plan.json") : null);
            if (saveDemo) plan.Title = Localization.Text("DemoBanner");
            MainWindow = plan;
            plan.Closed += (_, _) => model.Dispose();
            plan.Show();
            return;
        }
        var orb = new OrbWindow(model, demo: true);
        MainWindow = orb;
        orb.Closed += (_, _) => model.Dispose();
        orb.Show();
        if (args.Contains("--smoke-test", StringComparer.Ordinal))
        {
            var scenarios = new Queue<string>(DemoScenarioSequence);
            var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
            timer.Tick += (_, _) =>
            {
                if (scenarios.TryDequeue(out var scenario)) model.ShowDemo(scenario);
                else { timer.Stop(); orb.Close(); }
            };
            timer.Start();
        }
    }

    public void ShowMainWindow()
    {
        if (MainWindow is null) return;
        IsMainWindowHiddenByUser = false;
        MainWindow.Show();
        if (MainWindow.WindowState == WindowState.Minimized)
            MainWindow.WindowState = WindowState.Normal;
        MainWindow.Activate();
    }

    public void HideMainWindow()
    {
        IsMainWindowHiddenByUser = true;
        MainWindow?.Hide();
    }

    private void HideInitialDetails(object sender, RoutedEventArgs e)
    {
        // Let the details window initialize its shared data service, then keep startup compact.
        MainWindow.Loaded -= HideInitialDetails;
        HideMainWindow();
        MainWindow.ShowActivated = true;
    }

    public void ToggleMainWindowVisibility()
    {
        if (MainWindow?.IsVisible == true) HideMainWindow();
        else ShowMainWindow();
    }

    public void RequestExit()
    {
        IsShuttingDown = true;
        MainWindow?.Close();
        Shutdown();
    }

    public void ShowSettings()
    {
        if (_settingsWindow is { } existing)
        {
            if (existing.WindowState == WindowState.Minimized) existing.WindowState = WindowState.Normal;
            existing.Activate();
            return;
        }
        if (MainWindow is not MainWindow owner) return;
        var dialog = new SettingsWindow(SettingsStore, Settings, owner.PreviewAppearance);
        _settingsWindow = dialog;
        dialog.Closed += (_, _) =>
        {
            _settingsWindow = null;
            if (!dialog.WasSaved || dialog.SavedSettings is not { } saved) return;
            Settings = saved;
            Localization.Apply(saved.LanguageCode);
            owner.ApplySettings(saved);
            if (_orbWindow is not null) _orbWindow.Topmost = saved.AlwaysOnTop;
            _dashboardWindow?.ApplyLanguage();
            _notificationWindow?.Close();
            RefreshTrayLocalization();
        };
        // Settings must not disable the orb's Hide button or other application windows.
        dialog.Show();
    }

    public void ShowDashboard()
    {
        if (_dashboardWindow is null)
        {
            _dashboardWindow = new DashboardWindow { Owner = MainWindow };
            _dashboardWindow.Closed += (_, _) => _dashboardWindow = null;
        }
        _dashboardWindow.Show();
        _dashboardWindow.Activate();
    }

    public bool ShowQuotaNotification(string message, DateTimeOffset resetAt)
    {
        if (_windowsNotifications?.TryShow(message, resetAt) == true) return true;
        _notificationWindow?.Close();
        _notificationWindow = new QuotaNotificationWindow(
            message,
            ShowDashboard,
            () => (this.MainWindow as CodexGuardian.App.MainWindow)?.SilenceUntil(resetAt));
        _notificationWindow.Closed += (_, _) => _notificationWindow = null;
        _notificationWindow.Show();
        return false;
    }

    private Forms.ContextMenuStrip CreateTrayMenu()
    {
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add(Localization.Text("TrayShow"), null, (_, _) => ToggleMainWindowVisibility());
        menu.Items.Add(Localization.Text("TrayTimeline"), null, (_, _) => ShowDashboard());
        menu.Items.Add(Localization.Text("TraySettings"), null, (_, _) => ShowSettings());
        menu.Items.Add(Localization.Text("TrayExit"), null, (_, _) => RequestExit());
        menu.Items.Add(Localization.Text("ShowOrb"), null, (_, _) => _orbWindow?.Show());
        menu.Opening += (_, _) =>
            menu.Items[0].Text = Localization.Text(MainWindow?.IsVisible == true ? "TrayHide" : "TrayShow");
        return menu;
    }

    private void RefreshTrayLocalization()
    {
        var previous = _trayMenu;
        _trayMenu = CreateTrayMenu();
        if (_trayIcon is not null)
        {
            _trayIcon.Text = Localization.Text("TrayTooltip");
            _trayIcon.ContextMenuStrip = _trayMenu;
        }
        previous?.Dispose();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        Dispose();
        base.OnExit(e);
    }

    public void Dispose()
    {
        _activationWait?.Unregister(null);
        _activationWait = null;
        _activationEvent?.Dispose();
        _activationEvent = null;
        _orbWindow?.Close();
        _orbWindow = null;
        if (_trayIcon is not null)
        {
            _trayIcon.Visible = false;
            _trayIcon.Dispose();
            _trayIcon = null;
        }
        _notificationWindow?.Close();
        _notificationWindow = null;
        _windowsNotifications?.Dispose();
        _windowsNotifications = null;
        _applicationIcon?.Dispose();
        _applicationIcon = null;
        _trayMenu?.Dispose();
        _trayMenu = null;
        _singleInstanceMutex?.Dispose();
        _singleInstanceMutex = null;
        GC.SuppressFinalize(this);
    }
}

