using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Media.Animation;
using System.Windows.Navigation;
using System.Windows.Shapes;
using System.Windows.Threading;
using System.ComponentModel;
using CodexGuardian.Core;
using CodexGuardian.Storage;

namespace CodexGuardian.App;

/// <summary>
/// Interaction logic for MainWindow.xaml
/// </summary>
public partial class MainWindow : Window, IDisposable
{
    private readonly MainViewModel _viewModel = new();
    private readonly WindowStateStore _windowStateStore = new();
    private readonly PlanNotificationGate _planNotifications = new();
    private readonly DailyPlanStore _dailyPlans = new();
    private readonly DispatcherTimer _refreshTimer;
    private readonly DispatcherTimer _passiveTimer;
    private readonly DispatcherTimer _activityExpiryTimer;
    private readonly DispatcherTimer _quotaChangeTimer;
    private readonly CodexSessionActivityObserver _activityObserver = new();
    private readonly AmbientEffectRenderer _ambientEffectRenderer;
    private bool _isCollapsed;
    private bool _reduceMotion;
    private bool _isMoveLocked = true;
    private bool _quietMode;
    private bool _quotaChangeFeedbackActive;
    private int _refreshFeedbackVersion;
    private string _ambientEffect = "None";

    public MainWindow()
    {
        InitializeComponent();
        _ambientEffectRenderer = new AmbientEffectRenderer(AmbientEffectCanvas);
        IsVisibleChanged += Window_IsVisibleChanged;
        DataContext = _viewModel;
        _refreshTimer = new(TimeSpan.FromSeconds(15), DispatcherPriority.Background, RefreshTimer_Tick, Dispatcher);
        _passiveTimer = new(TimeSpan.FromSeconds(8), DispatcherPriority.Background, PassiveTimer_Tick, Dispatcher);
        _activityExpiryTimer = new(TimeSpan.FromSeconds(4), DispatcherPriority.Background, ActivityExpiryTimer_Tick, Dispatcher);
        _quotaChangeTimer = new(TimeSpan.FromSeconds(2), DispatcherPriority.Background, QuotaChangeTimer_Tick, Dispatcher);
        _activityObserver.ActivityObserved += ActivityObserver_ActivityObserved;
        if (System.Windows.Application.Current is App app) ApplySettings(app.Settings);
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        RestoreWindowState();
        await RefreshQuotaAsync();
        _refreshTimer.Start();
    }

    private void Window_Closed(object? sender, EventArgs e)
    {
        _refreshTimer.Stop();
        _passiveTimer.Stop();
        _activityExpiryTimer.Stop();
        _quotaChangeTimer.Stop();
        Dispose();
    }

    private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (!_isMoveLocked && e.ButtonState == MouseButtonState.Pressed)
        {
            DragMove();
            _ = SaveWindowStateAsync();
        }
    }

    private async void RefreshButton_Click(object sender, RoutedEventArgs e)
    {
        RefreshButton.Content = "…";
        RefreshButton.IsEnabled = false;
        await RefreshQuotaAsync(userInitiated: true);
        RefreshButton.IsEnabled = true;
        await ShowRefreshFeedbackAsync();
    }

    private void LockButton_Click(object sender, RoutedEventArgs e)
    {
        _isMoveLocked = !_isMoveLocked;
        UpdateLockButton();
        _ = SaveWindowStateAsync();
    }

    private void Window_MouseEnter(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (_quietMode) ChromePanel.Opacity = 1;
    }

    private void Window_MouseLeave(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (_quietMode) ChromePanel.Opacity = 0.24;
    }

    private void SettingsButton_Click(object sender, RoutedEventArgs e)
    {
        if (System.Windows.Application.Current is App app) app.ShowSettings();
    }

    private void DashboardButton_Click(object sender, RoutedEventArgs e)
    {
        if (System.Windows.Application.Current is App app) app.ShowDashboard();
    }

    private void MoreButton_Click(object sender, RoutedEventArgs e)
    {
        if (MoreButton.ContextMenu is not { } menu) return;
        menu.PlacementTarget = MoreButton;
        menu.IsOpen = true;
    }

    private void CollapseButton_Click(object sender, RoutedEventArgs e)
    {
        _passiveTimer.Stop();
        _isCollapsed = !_isCollapsed;
        PassiveBanner.Visibility = Visibility.Collapsed;
        StatusCard.Visibility = Visibility.Visible;
        ExpandedContent.Visibility = _isCollapsed ? Visibility.Collapsed : Visibility.Visible;
        Height = _isCollapsed ? CollapsedHeight : ExpandedHeight;
        EnsureWindowWithinVirtualScreen();
        CollapseButton.Content = _isCollapsed ? "⌄" : "⌃";
        CollapseButton.ToolTip = Localization.Text(_isCollapsed ? "Expand" : "Collapse");
        _ = SaveWindowStateAsync();
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        if (System.Windows.Application.Current is App app) app.HideMainWindow();
        else Hide();
    }

    private async void RefreshTimer_Tick(object? sender, EventArgs e) => await RefreshQuotaAsync();

    public void ApplySettings(GuardianSettings settings)
    {
        Topmost = settings.AlwaysOnTop;
        _refreshTimer.Interval = TimeSpan.FromSeconds(settings.RefreshSeconds);
        _viewModel.SetHistoryRetention(settings.HistoryRetentionDays);
        _viewModel.SetPersonalAccountMode(settings.PersonalAccountOnly);
        _viewModel.SetPersonalizedAnalysis(settings.EnablePersonalizedAnalysis);
        _viewModel.Relocalize();
        PreviewAppearance(settings);
        CollapseButton.ToolTip = Localization.Text(_isCollapsed ? "Expand" : "Collapse");
        UpdateLockButton();
        if (IsLoaded) _ = RefreshQuotaAsync();
    }

    public void PreviewAppearance(GuardianSettings settings)
    {
        _reduceMotion = settings.ReduceMotion;
        _ambientEffect = settings.AmbientEffect;
        ApplyTheme(settings.ThemeMode);
        ApplyDisplayMode(settings.LowPresenceMode);
        ApplyMotion();
        _ambientEffectRenderer.Apply(_ambientEffect, _reduceMotion, IsVisible);
        if (_reduceMotion) StopTransientAnimations();
        else if (_activityExpiryTimer.IsEnabled && !_quotaChangeFeedbackActive) SetActivityVisual(true);
    }

    private void ApplyTheme(string themeMode)
    {
        var theme = GuardianAppearanceCatalog.Resolve(themeMode);
        Resources["CanvasBrush"] = new SolidColorBrush(theme.Canvas);
        Resources["SurfaceBrush"] = new SolidColorBrush(theme.Surface);
        Resources["RaisedBrush"] = new SolidColorBrush(theme.Raised);
        Resources["HairlineBrush"] = new SolidColorBrush(theme.Hairline);
        Resources["AccentBrush"] = new SolidColorBrush(theme.Accent);
    }

    private void ApplyDisplayMode(bool lowPresenceMode)
    {
        _quietMode = lowPresenceMode;
        ApplyDensity();
    }

    private async Task RefreshQuotaAsync(bool userInitiated = false)
    {
        var settings = System.Windows.Application.Current is App app ? app.Settings : new GuardianSettings();
        var previousSnapshot = _viewModel.LatestSnapshot;
        await _viewModel.RefreshAsync(UserContextDetector.Capture(settings), userInitiated);
        ApplyMotion();

        if (_viewModel.LatestSnapshot is { } currentSnapshot)
        {
            var changes = QuotaChangeDetector.Detect(previousSnapshot, currentSnapshot);
            if (changes.Count > 0) ShowQuotaChangeFeedback(changes);
        }

        if (_viewModel.LatestSnapshot is not { } snapshot) return;
        var now = DateTimeOffset.UtcNow;
        var saved = snapshot.Weekly is { } weekly
            ? _dailyPlans.Load(snapshot.ScopeKey, weekly.ResetsAt) : null;
        var risk = PlanRiskEvaluator.Evaluate(snapshot, _viewModel.WeeklyBaseline,
            _viewModel.IsPersonalizedAnalysisEnabled ? saved?.Days : null, saved?.ReservePercent ?? 0, now, TimeZoneInfo.Local, saved?.ResetEvent);
        var reset = risk == PlanRisk.Exhausted && snapshot.FiveHour is { RemainingPercent: 0 } shortWindow
            ? shortWindow.ResetsAt : snapshot.Weekly?.ResetsAt;
        if (reset is null || !_planNotifications.ShouldDeliver(risk, snapshot.ScopeKey, reset.Value,
            now, settings.EnableActiveNotifications, UserContextDetector.Capture(settings))) return;
        if (System.Windows.Application.Current is App guardianApp)
            guardianApp.ShowQuotaNotification(Localization.Text("Risk" + risk), reset.Value);
    }

    public void SilenceUntil(DateTimeOffset resetAt) => _planNotifications.SilenceUntil(resetAt);
    private void ApplyMotion()
    {
        if (_reduceMotion ||
            _viewModel.LatestSnapshot?.Freshness != DataFreshness.Fresh ||
            _viewModel.LatestAdvice?.Level is null or NotificationLevel.Silent)
        {
            StatusDot.BeginAnimation(OpacityProperty, null);
            HeaderStatusIcon.BeginAnimation(OpacityProperty, null);
            RootBorder.BorderBrush?.BeginAnimation(System.Windows.Media.Brush.OpacityProperty, null);
            StatusDot.Opacity = 1;
            HeaderStatusIcon.Opacity = 1;
            return;
        }

        var burnRate = _viewModel.LatestPrediction.PercentPerHour;
        if (_viewModel.LatestSnapshot.FiveHour is not { } fiveHour ||
            _viewModel.LatestSnapshot.GetFiveHourFreshness(DateTimeOffset.UtcNow) != DataFreshness.Fresh) return;
        var remaining = fiveHour.RemainingPercent;
        var seconds = remaining <= 10
            ? 0.8
            : Math.Clamp(3 - burnRate / 20, 0.9, 3);
        var animation = new DoubleAnimation(0.45, 1, TimeSpan.FromSeconds(seconds))
        {
            AutoReverse = true,
            RepeatBehavior = RepeatBehavior.Forever,
        };
        StatusDot.BeginAnimation(OpacityProperty, animation);
        HeaderStatusIcon.BeginAnimation(OpacityProperty, animation);
        RootBorder.BorderBrush?.BeginAnimation(System.Windows.Media.Brush.OpacityProperty, animation);
    }

    private void ShowPassiveAdvice()
    {
        if (_reduceMotion) return;
        if (!_isCollapsed) return;
        if (System.Windows.Application.Current is App { IsMainWindowHiddenByUser: true }) return;
        var now = DateTimeOffset.UtcNow;
        PassiveTitle.Text = _viewModel.LatestPrediction.EstimatedExhaustionAt is { } exhaustion
            ? Localization.Text("PassivePredictionTitle", Math.Max(1, (int)Math.Ceiling((exhaustion - now).TotalMinutes)))
            : Localization.Text("PassiveLowNowTitle");
        PassiveDetail.Text = Localization.Text("PassivePredictionDetail");
        StatusCard.Visibility = Visibility.Collapsed;
        PassiveBanner.Visibility = Visibility.Visible;
        _isCollapsed = false;
        ExpandedContent.Visibility = Visibility.Visible;
        Height = ExpandedHeight;
        EnsureWindowWithinVirtualScreen();
        CollapseButton.Content = "⌃";
        CollapseButton.ToolTip = Localization.Text("Collapse");
        Show();
        _passiveTimer.Stop();
        _passiveTimer.Start();
    }

    private void PassiveTimer_Tick(object? sender, EventArgs e)
    {
        _passiveTimer.Stop();
        if (_isCollapsed) return;
        _isCollapsed = true;
        PassiveBanner.Visibility = Visibility.Collapsed;
        StatusCard.Visibility = Visibility.Visible;
        ExpandedContent.Visibility = Visibility.Collapsed;
        Height = CollapsedHeight;
        EnsureWindowWithinVirtualScreen();
        CollapseButton.Content = "⌄";
        CollapseButton.ToolTip = Localization.Text("Expand");
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (System.Windows.Application.Current is App { IsShuttingDown: false })
        {
            e.Cancel = true;
            if (System.Windows.Application.Current is App app) app.HideMainWindow();
            else Hide();
            return;
        }
        base.OnClosing(e);
    }

    private void RestoreWindowState()
    {
        var state = _windowStateStore.Load();
        _isMoveLocked = state?.IsMoveLocked ?? true;
        UpdateLockButton();
        if (state?.IsCollapsed == true)
        {
            _isCollapsed = true;
            ExpandedContent.Visibility = Visibility.Collapsed;
            Height = CollapsedHeight;
            CollapseButton.Content = "⌄";
            CollapseButton.ToolTip = Localization.Text("Expand");
        }

        if (state is null) DockToBottomRight();
        else
        {
            Left = state.Left;
            Top = state.Top;
            EnsureWindowWithinVirtualScreen();
        }
    }

    private void EnsureWindowWithinVirtualScreen()
    {
        var maximumLeft = SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth - Width;
        var maximumTop = SystemParameters.VirtualScreenTop + SystemParameters.VirtualScreenHeight - Height;
        Left = Math.Clamp(Left, SystemParameters.VirtualScreenLeft, Math.Max(SystemParameters.VirtualScreenLeft, maximumLeft));
        Top = Math.Clamp(Top, SystemParameters.VirtualScreenTop, Math.Max(SystemParameters.VirtualScreenTop, maximumTop));
    }

    private double CollapsedHeight => _quietMode ? 58 : 82;

    private double ExpandedHeight => _quietMode ? 216 : 240;

    private void ApplyDensity()
    {
        Width = _quietMode ? 440 : 560;
        HeaderRow.Height = new GridLength(_quietMode ? 48 : 58);
        BrandColumn.Width = new GridLength(_quietMode ? 0 : 152);
        BrandPanel.Visibility = _quietMode ? Visibility.Collapsed : Visibility.Visible;
        BrandDivider.Visibility = _quietMode ? Visibility.Collapsed : Visibility.Visible;
        RootLayout.Margin = _quietMode ? new Thickness(12, 5, 12, 5) : new Thickness(16, 12, 16, 14);
        QuotaGrid.Margin = _quietMode ? new Thickness(0, 2, 8, 2) : new Thickness(15, 3, 14, 3);
        ChromePanel.Opacity = _quietMode ? 0.24 : 1;
        RootBorder.CornerRadius = new CornerRadius(_quietMode ? 12 : 18);
        RootShadow.Opacity = _quietMode ? 0 : 0.28;
        Height = _isCollapsed ? CollapsedHeight : ExpandedHeight;
        EnsureWindowWithinVirtualScreen();
    }

    private void DockToBottomRight()
    {
        const double margin = 16;
        var workArea = SystemParameters.WorkArea;
        Left = workArea.Right - Width - margin;
        Top = workArea.Bottom - Height - margin;
    }

    private void UpdateLockButton()
    {
        LockButton.Content = _isMoveLocked ? "🔒" : "🔓";
        LockButton.ToolTip = Localization.Text(_isMoveLocked ? "PositionLocked" : "PositionUnlocked");
    }

    private async Task ShowRefreshFeedbackAsync()
    {
        var version = ++_refreshFeedbackVersion;
        RefreshButton.Content = "✓";
        RefreshButton.ToolTip = _viewModel.RefreshFeedback;
        await Task.Delay(TimeSpan.FromSeconds(2));
        if (version != _refreshFeedbackVersion) return;
        RefreshButton.Content = "↻";
        RefreshButton.ToolTip = Localization.Text("RefreshQuota");
    }

    private void ActivityObserver_ActivityObserved(object? sender, EventArgs e)
    {
        if (Dispatcher.HasShutdownStarted) return;
        _ = Dispatcher.BeginInvoke(() =>
        {
            _activityExpiryTimer.Stop();
            _activityExpiryTimer.Start();
            if (!_quotaChangeFeedbackActive) SetActivityVisual(true);
        }, DispatcherPriority.Background);
    }

    private void ActivityExpiryTimer_Tick(object? sender, EventArgs e)
    {
        _activityExpiryTimer.Stop();
        if (!_quotaChangeFeedbackActive) SetActivityVisual(false);
    }

    private void SetActivityVisual(bool isActive)
    {
        ActivityFlowTransform.BeginAnimation(TranslateTransform.XProperty, null);
        if (!isActive || _reduceMotion)
        {
            ActivityFlow.Visibility = Visibility.Collapsed;
            ActivityFlow.Opacity = 0;
            return;
        }

        ActivityFlow.Visibility = Visibility.Visible;
        ActivityFlow.Opacity = 0.16;
        ActivityFlowTransform.BeginAnimation(
            TranslateTransform.XProperty,
            new DoubleAnimation(-46, Math.Max(QuotaGrid.ActualWidth, 180), TimeSpan.FromSeconds(1.45))
            {
                RepeatBehavior = RepeatBehavior.Forever,
            });
    }

    private void ShowQuotaChangeFeedback(IReadOnlyList<QuotaChange> changes)
    {
        _quotaChangeFeedbackActive = true;
        SetActivityVisual(false);
        QuotaDeltaText.Text = string.Join(" · ", changes.Select(FormatQuotaChange));
        QuotaDeltaBadge.Visibility = Visibility.Visible;
        _quotaChangeTimer.Stop();
        _quotaChangeTimer.Start();

        if (_reduceMotion) return;
        foreach (var change in changes)
        {
            if (change.Window == QuotaWindowKind.FiveHour)
                AnimateQuotaChange(FiveHourChangeFlash, FiveHourValueText, FiveHourValueTransform);
            else
                AnimateQuotaChange(WeeklyChangeFlash, WeeklyValueText, WeeklyValueTransform);
        }
    }

    private static string FormatQuotaChange(QuotaChange change)
    {
        var label = change.Window == QuotaWindowKind.FiveHour
            ? Localization.Text("FiveHourCompactLabel")
            : Localization.Text("WeeklyCompactLabel");
        if (change.ResetObserved) return Localization.Text("QuotaResetSignal", label);
        var delta = change.DeltaPercentagePoints > 0
            ? $"+{change.DeltaPercentagePoints}%"
            : $"−{Math.Abs(change.DeltaPercentagePoints)}%";
        return $"{label} {delta}";
    }

    private static void AnimateQuotaChange(Border flash, TextBlock value, TranslateTransform transform)
    {
        flash.BeginAnimation(OpacityProperty, new DoubleAnimation(0.18, 0, TimeSpan.FromSeconds(0.8)));
        value.BeginAnimation(OpacityProperty, new DoubleAnimation(0.48, 1, TimeSpan.FromSeconds(0.38)));
        transform.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(3, 0, TimeSpan.FromSeconds(0.38))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
        });
    }

    private void QuotaChangeTimer_Tick(object? sender, EventArgs e)
    {
        _quotaChangeTimer.Stop();
        QuotaDeltaBadge.Visibility = Visibility.Collapsed;
        _quotaChangeFeedbackActive = false;
        if (_activityExpiryTimer.IsEnabled) SetActivityVisual(true);
    }

    private void StopTransientAnimations()
    {
        ActivityFlowTransform.BeginAnimation(TranslateTransform.XProperty, null);
        ActivityFlow.Visibility = Visibility.Collapsed;
        FiveHourChangeFlash.BeginAnimation(OpacityProperty, null);
        WeeklyChangeFlash.BeginAnimation(OpacityProperty, null);
        FiveHourValueText.BeginAnimation(OpacityProperty, null);
        WeeklyValueText.BeginAnimation(OpacityProperty, null);
        FiveHourValueTransform.BeginAnimation(TranslateTransform.YProperty, null);
        WeeklyValueTransform.BeginAnimation(TranslateTransform.YProperty, null);
        FiveHourChangeFlash.Opacity = 0;
        WeeklyChangeFlash.Opacity = 0;
    }

    private void Window_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        _ambientEffectRenderer?.Apply(_ambientEffect, _reduceMotion, IsVisible);
    }

    private Task SaveWindowStateAsync() =>
        _windowStateStore.SaveAsync(new(Left, Top, _isCollapsed, _isMoveLocked));

    public void Dispose()
    {
        _activityObserver.ActivityObserved -= ActivityObserver_ActivityObserved;
        IsVisibleChanged -= Window_IsVisibleChanged;
        _activityObserver.Dispose();
        _ambientEffectRenderer.Dispose();
        _viewModel.Dispose();

        GC.SuppressFinalize(this);
    }
}


