using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using ButtonBase = System.Windows.Controls.Primitives.ButtonBase;

namespace CodexGuardian.App;

public partial class OrbWindow : Window
{
    private readonly System.Windows.Threading.DispatcherTimer _displayTimer;
    private readonly bool _demo;
    private readonly CodexGuardian.Storage.DailyPlanStore _planStore = new();
    private CodexGuardian.Storage.SavedDailyPlan? _savedPlan;
    private DateTimeOffset _nextPlanRead;
    private BudgetWindow? _budgetWindow;
    private readonly WindowStateStore _positionStore = new("orb-window-state.json");
    public OrbWindow(object viewModel, bool demo = false)
    {
        _demo = demo;
        InitializeComponent();
        VersionLabel.Text = "Codex Quota " + System.Reflection.Assembly.GetExecutingAssembly()
            .GetCustomAttributes(typeof(System.Reflection.AssemblyInformationalVersionAttribute), false)
            .Cast<System.Reflection.AssemblyInformationalVersionAttribute>().First().InformationalVersion;
        DataContext = viewModel;
        if (demo) Width = 252;
        if (demo)
        {
            SetExpanded(true);
            DemoControls.Visibility = Visibility.Visible;
            DetailsButton.Visibility = Visibility.Collapsed;
            SettingsButton.Visibility = Visibility.Collapsed;
            BudgetButton.Visibility = Visibility.Collapsed;
            HideButton.SetResourceReference(ToolTipProperty, "DemoClose");
            DemoChoice.SelectedIndex = 0;
        }
        _displayTimer = new() { Interval = TimeSpan.FromSeconds(1) };
        _displayTimer.Tick += (_, _) => UpdateDisplay();
        _displayTimer.Start();
        Closed += (_, _) =>
        {
            _displayTimer.Stop();
            _budgetWindow?.Close();
        };
        UpdateDisplay();
        Loaded += (_, _) => { if (!_demo) RestorePosition(); };
        IsVisibleChanged += (_, _) => { if (IsVisible && IsLoaded) KeepVisible(); };
        SizeChanged += (_, _) => { if (IsLoaded) KeepVisible(); };
        if (System.Windows.Application.Current is App app) Topmost = app.Settings.AlwaysOnTop;
    }

    private void UpdateDisplay()
    {
        UpdateDisplayCore();
        MiniOrb.ToolTip = $"{WindowLabel.Text}\n{WindowReset.Text}\n{TodayBudget.Text}\n{PlanStatus.Text}\n{Localization.Text("OrbExpand")}";
    }

    private void UpdateDisplayCore()
    {
        var now = DateTimeOffset.UtcNow;
        WindowReset.ToolTip = null;
        var snapshot = (DataContext as MainViewModel)?.LatestSnapshot;
        if (!_demo && now >= _nextPlanRead)
        {
            _savedPlan = snapshot?.Weekly is { } weekly
                ? _planStore.Load(snapshot.ScopeKey, weekly.ResetsAt) : null;
            _nextPlanRead = now.AddSeconds(10);
        }
        var validPlan = (DataContext as MainViewModel)?.IsPersonalizedAnalysisEnabled == true &&
            _savedPlan?.ScopeKey == snapshot?.ScopeKey && _savedPlan?.ResetsAt == snapshot?.Weekly?.ResetsAt ? _savedPlan : null;
        TodayBudget.Text = Localization.Text("OrbBudgetSetup");
        if (validPlan is not null)
        {
            var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(now, TimeZoneInfo.Local).DateTime);
            var allocation = CodexGuardian.Core.DailyBudgetPlanner.Calculate(snapshot,
                validPlan.Days.Where(day => day.Date >= today).ToArray(), validPlan.ReservePercent, now, TimeZoneInfo.Local);
            var todayAmount = allocation.Days.FirstOrDefault(day => day.Date == today);
            TodayBudget.Text = !allocation.IsValid ? Localization.Text("BudgetTodayUnavailable")
                : todayAmount is null ? Localization.Text("BudgetTodayRest")
                : Localization.Text("BudgetTodayAdvice", todayAmount.Percent);
        }
        var risk = CodexGuardian.Core.PlanRiskEvaluator.Evaluate(snapshot,
            (DataContext as MainViewModel)?.WeeklyBaseline ?? new(null, 0, "unknown"),
            validPlan?.Days, validPlan?.ReservePercent ?? 0, now, TimeZoneInfo.Local, validPlan?.ResetEvent);
        Ring.RiskColor = risk switch
        {
            CodexGuardian.Core.PlanRisk.WithinPlan => System.Windows.Media.Color.FromRgb(107, 211, 154),
            CodexGuardian.Core.PlanRisk.ReserveAtRisk => System.Windows.Media.Color.FromRgb(245, 195, 105),
            CodexGuardian.Core.PlanRisk.MinimumUnavailable or CodexGuardian.Core.PlanRisk.Exhausted => System.Windows.Media.Color.FromRgb(241, 126, 126),
            _ => System.Windows.Media.Color.FromRgb(121, 185, 232),
        };
        PlanStatus.Text = Localization.Text("Risk" + risk);
        if (validPlan?.ResetEvent is { } resetEvent)
            PlanStatus.Text = resetEvent.PlannedAt <= now
                ? Localization.Text("EventDueOrb")
                : Localization.Text("EventActiveOrb", resetEvent.PlannedAt.ToLocalTime()) + "\n" + PlanStatus.Text;
        var plan = snapshot?.PlanType?.ToLowerInvariant();
        var planName = plan switch { "plus" => "Plus", "pro" => "Pro", "free" => "Free", "team" => "Team", "business" => "Business", "enterprise" => "Enterprise", "edu" => "Edu", _ => null };
        AccountPlan.Text = planName is not null ? Localization.Text("PlanDetected", planName)
            : string.IsNullOrWhiteSpace(plan) ? Localization.Text("PlanUnknown")
            : Localization.Text("PlanReported", plan);
        var selected = CodexGuardian.Core.OrbSelection.Select(snapshot, now);
        WindowLabel.Text = Localization.Text(selected.Window is null ? "OrbUnknownWindow" : selected.IsWeekly ? "OrbWeeklyLabel" : "OrbRemaining");
        if (snapshot?.OtherWindows is { Count: > 0 } other)
        {
            WindowLabel.Text = Localization.Text("OrbOtherWindow", other[0].WindowMinutes);
            PlanStatus.Text = Localization.Text("UnrecognizedWindowNote");
        }
        Ring.Remaining = selected.Window?.RemainingPercent ?? 0;
        Ring.Opacity = selected.Freshness == CodexGuardian.Core.DataFreshness.Fresh ? 1 : 0.35;
        WindowValue.Text = selected.Window is { } window && selected.Freshness == CodexGuardian.Core.DataFreshness.Fresh && window.ResetsAt > now
            ? $"{window.RemainingPercent}%" : "—";
        if (selected.Window is not { } current) { WindowReset.Text = Localization.Text("WindowNotReported"); return; }
        if (current.ResetsAt <= now) { WindowReset.Text = Localization.Text("ResetDue"); return; }
        if (selected.Freshness != CodexGuardian.Core.DataFreshness.Fresh) { WindowReset.Text = Localization.Text("OrbStale"); return; }
        var remaining = current.ResetsAt - now;
        WindowReset.Text = Localization.Text("OrbCompactCountdown", (int)remaining.TotalHours, remaining.Minutes);
        WindowReset.ToolTip = Localization.Text("OrbCountdown", (int)remaining.TotalHours, remaining.Minutes, remaining.Seconds);
    }

    private async void DragWindow(object sender, MouseButtonEventArgs e)
    {
        for (var node = e.OriginalSource as DependencyObject; node is not null;)
        {
            if (node is ButtonBase or System.Windows.Controls.ComboBox or System.Windows.Controls.ComboBoxItem) return;
            node = node is Visual ? VisualTreeHelper.GetParent(node) : LogicalTreeHelper.GetParent(node);
        }
        if (e.ButtonState != MouseButtonState.Pressed) return;
        var wasMini = MiniOrb.Visibility == Visibility.Visible;
        var initialLeft = Left;
        var initialTop = Top;
        DragMove();
        if (wasMini && Math.Abs(Left - initialLeft) < 3 && Math.Abs(Top - initialTop) < 3) SetExpanded(true);
        KeepVisible();
        if (!_demo) await _positionStore.SaveAsync(new GuardianWindowState(Left, Top, false, false));
    }

    private void RestorePosition()
    {
        var saved = _positionStore.Load();
        if (saved is not null && double.IsFinite(saved.Left) && double.IsFinite(saved.Top))
        { Left = saved.Left; Top = saved.Top; }
        KeepVisible();
    }

    private void KeepVisible()
    {
        // WPF uses DIPs; Screen working areas are physical pixels.
        var dpi = VisualTreeHelper.GetDpi(this);
        var left = double.IsFinite(Left) ? Left : 0;
        var top = double.IsFinite(Top) ? Top : 0;
        var width = ActualWidth > 0 ? ActualWidth : Width;
        var height = ActualHeight > 0 ? ActualHeight : 200;
        var screen = System.Windows.Forms.Screen.FromPoint(new System.Drawing.Point(
            (int)((left + width / 2) * dpi.DpiScaleX), (int)((top + height / 2) * dpi.DpiScaleY)));
        var area = screen.WorkingArea;
        Left = Math.Clamp(left, area.Left / dpi.DpiScaleX, Math.Max(area.Left / dpi.DpiScaleX, area.Right / dpi.DpiScaleX - width));
        Top = Math.Clamp(top, area.Top / dpi.DpiScaleY, Math.Max(area.Top / dpi.DpiScaleY, area.Bottom / dpi.DpiScaleY - height));
    }

    private void ToggleExpanded(object sender, RoutedEventArgs e)
    {
        SetExpanded(DetailSurface.Visibility != Visibility.Visible);
    }

    private void SetExpanded(bool expand)
    {
        MiniOrb.Visibility = expand ? Visibility.Collapsed : Visibility.Visible;
        DetailSurface.Visibility = expand ? Visibility.Visible : Visibility.Collapsed;
        ExpandedPanel.Visibility = expand ? Visibility.Visible : Visibility.Collapsed;
        Width = expand ? 252 : 56;
        ExpandButton.SetResourceReference(ToolTipProperty, expand ? "OrbCollapse" : "OrbExpand");
        ExpandButton.SetResourceReference(System.Windows.Automation.AutomationProperties.NameProperty, expand ? "OrbCollapse" : "OrbExpand");
    }

    private void ShowDetails(object sender, RoutedEventArgs e)
    {
        if (_demo) return;
        if (System.Windows.Application.Current is App app) app.ShowMainWindow();
    }

    private void HideOrb(object sender, RoutedEventArgs e) { if (_demo) Close(); else Hide(); }
    private void ShowSettings(object sender, RoutedEventArgs e)
    {
        if (!_demo && System.Windows.Application.Current is App app) app.ShowSettings();
    }
    private void ShowBudget(object sender, RoutedEventArgs e)
    {
        if (_demo) return;
        if (_budgetWindow is { } existing)
        {
            if (existing.WindowState == WindowState.Minimized) existing.WindowState = WindowState.Normal;
            existing.Activate();
            return;
        }
        if (DataContext is MainViewModel model)
        {
            var budget = new BudgetWindow(model);
            _budgetWindow = budget;
            budget.Closed += (_, _) =>
            {
                _budgetWindow = null;
                _nextPlanRead = DateTimeOffset.MinValue;
                UpdateDisplay();
            };
            // Editing a plan must not disable Hide or disappear with the orb.
            // An owned editor stays above the always-on-top orb without making it modal.
            budget.Owner = this;
            budget.Show();
        }
    }
    private void ChangeDemo(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (DataContext is MainViewModel model && DemoChoice.SelectedItem is System.Windows.Controls.ComboBoxItem { Tag: string scenario })
        { model.ShowDemo(scenario); UpdateDisplay(); }
    }
}
