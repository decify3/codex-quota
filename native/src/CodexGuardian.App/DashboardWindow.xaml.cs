using System.Windows;
using System.Windows.Controls;
using CodexGuardian.Core;
using CodexGuardian.Providers;
using CodexGuardian.Storage;

namespace CodexGuardian.App;

public partial class DashboardWindow : Window, IDisposable
{
    private readonly CodexHybridQuotaProvider _provider = new();
    private readonly JsonlQuotaHistoryStore _history;
    private QuotaSnapshot? _snapshot;
    private BurnRatePrediction _prediction = new(0, null, PredictionConfidence.None, 0);
    private IReadOnlyList<TimelinePoint> _observed = [];
    private QuotaSnapshot? _taskStartSnapshot;

    public DashboardWindow()
    {
        var settings = System.Windows.Application.Current is App app ? app.Settings : new GuardianSettings();
        _history = new JsonlQuotaHistoryStore(retention: TimeSpan.FromDays(settings.HistoryRetentionDays));
        InitializeComponent();
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e) => await LoadDataAsync();
    private async void RefreshButton_Click(object sender, RoutedEventArgs e)
    {
        _provider.RequestActiveRefresh();
        await LoadDataAsync();
    }

    private async Task LoadDataAsync()
    {
        _provider.SetPersonalAccountMode((System.Windows.Application.Current as App)?.Settings.PersonalAccountOnly == true);
        var result = await _provider.ReadAsync();
        if (result.Snapshot is not { } snapshot)
        {
            _snapshot = null;
            StatusText.Text = Localization.Text("NoTrustedQuota");
            return;
        }

        var settings = System.Windows.Application.Current is App app ? app.Settings : new GuardianSettings();
        var entries = await _history.ReadSinceAsync(DateTimeOffset.UtcNow.AddDays(-7));
        entries = snapshot.ScopeKey is null ? [] : entries.Where(entry => entry.ScopeKey == snapshot.ScopeKey).ToArray();
        snapshot = MainViewModel.BackfillLastKnownWindows(snapshot, entries);
        _snapshot = snapshot;
        if (settings.EnablePersonalizedAnalysis)
        {
            var weeklyReport = WeeklyQuotaReporter.Generate(entries, DateTimeOffset.UtcNow);
            var weeklySummary = weeklyReport.SampleCount == 0
                ? Localization.Text("WeeklyNoSamples")
                : Localization.Text(
                    "WeeklySummary",
                    weeklyReport.WeeklyConsumedPercent,
                    weeklyReport.FiveHourWindowCount,
                    weeklyReport.PeakFiveHourBurnPercentPerHour);
            WeeklyReportText.Text = Localization.Text("WeeklyReport", weeklySummary);
        }
        else
        {
            WeeklyReportText.Text = Localization.Text("PersonalizedAnalysisOff");
        }
        var trends = QuotaTrendAnalyzer.Analyze(entries, DateTimeOffset.UtcNow);
        TrendText.Text = settings.EnablePersonalizedAnalysis ? string.Join(" · ", trends.Select(FormatTrend)) : Localization.Text("PersonalizedAnalysisOff");
        if (snapshot.FiveHour is { } fiveHour)
        {
            var currentWindow = entries
                .Where(entry => entry.FiveHourResetsAt == fiveHour.ResetsAt && entry.FiveHourRemainingPercent is not null)
                .ToArray();
            _observed = currentWindow.Select(entry => new TimelinePoint(entry.FiveHourObservedAt!.Value, entry.FiveHourRemainingPercent!.Value, false)).ToArray();
            var points = currentWindow.Select(entry => new QuotaHistoryPoint(entry.FiveHourObservedAt!.Value, entry.FiveHourRemainingPercent!.Value)).ToArray();
            _prediction = !settings.EnablePersonalizedAnalysis || snapshot.GetFiveHourFreshness(DateTimeOffset.UtcNow) != DataFreshness.Fresh
                ? new(0, null, PredictionConfidence.None, 0)
                : QuotaPredictor.Predict(points, fiveHour.ResetsAt);
            var backtest = PredictionBacktester.Evaluate(points, fiveHour.ResetsAt);
            BacktestText.Text = !settings.EnablePersonalizedAnalysis ? Localization.Text("PersonalizedAnalysisOff") : backtest.EvaluationCount == 0
                ? Localization.Text("ReplayCollecting")
                : Localization.Text(
                    "ReplayResult",
                    backtest.MedianAbsoluteErrorPercent,
                    backtest.EvaluationCount,
                    Localization.Text(backtest.TargetMet ? "ReplayWithin" : "ReplayOutside"));
            FiveHourValue.Text = $"{fiveHour.RemainingPercent}%";
        }
        else
        {
            _observed = [];
            _prediction = new(0, null, PredictionConfidence.None, 0);
            BacktestText.Text = Localization.Text("ReplayCollecting");
            FiveHourValue.Text = "—";
        }
        BurnRateValue.Text = _prediction.PercentPerHour > 0
            ? Localization.Text("BurnRateValue", _prediction.PercentPerHour)
            : Localization.Text("Learning");
        ConfidenceValue.Text = LocalizeConfidence(_prediction.Confidence);
        StatusText.Text = _prediction.Confidence == PredictionConfidence.None
            ? Localization.Text("ProjectionLearning")
            : Localization.Text("ProjectionEstimate");
        if (!settings.EnablePersonalizedAnalysis)
        {
            StrategyText.Text = Localization.Text("PersonalizedAnalysisOff");
            TaskText.Text = Localization.Text("PersonalizedAnalysisOff");
            TaskButton.IsEnabled = false;
            Timeline.ReservePercent = 0;
            RenderScenario();
            return;
        }

        TaskButton.IsEnabled = true;
        var mode = Enum.TryParse<QuotaStrategyMode>(settings.StrategyMode, true, out var parsedMode)
            ? parsedMode
            : QuotaStrategyMode.Balanced;
        var policy = QuotaStrategyPolicy.For(mode);
        var assessment = QuotaStrategyAdvisor.Evaluate(snapshot, _prediction, policy, DateTimeOffset.UtcNow);
        StrategyText.Text = FormatStrategy(
            mode,
            policy,
            assessment,
            _prediction.SampleCount,
            _prediction.Confidence >= PredictionConfidence.Medium);
        if (_taskStartSnapshot is null)
        {
            var budget = TaskBudgetAdvisor.Estimate(_prediction, TimeSpan.FromHours(1), policy);
            TaskText.Text = budget.Confidence == PredictionConfidence.None
                ? Localization.Text("TaskBudgetUnavailable")
                : Localization.Text(
                    "TaskBudgetValue",
                    budget.MinimumPercent,
                    budget.MaximumPercent,
                    LocalizeConfidence(budget.Confidence),
                    _prediction.SampleCount,
                    _prediction.PercentPerHour,
                    policy.FiveHourReservePercent);
        }
        Timeline.ReservePercent = policy.FiveHourReservePercent;
        RenderScenario();
    }

    private static string FormatTrend(QuotaTrendWindow trend) =>
        trend.Confidence == PredictionConfidence.None
            ? Localization.Text("TrendLearning", LocalizeTrendLabel(trend.Label))
            : Localization.Text(
                "TrendValue",
                LocalizeTrendLabel(trend.Label),
                trend.ConsumedPercent,
                trend.PercentPerHour,
                LocalizeConfidence(trend.Confidence));

    private static string LocalizeTrendLabel(string label) =>
        Localization.Text(label switch
        {
            "30m" => "TrendLabel30m",
            "2h" => "TrendLabel2h",
            "5h" => "TrendLabel5h",
            _ => "TrendLabel7d",
        });

    private static string LocalizeConfidence(PredictionConfidence confidence) =>
        Localization.Text(confidence switch
        {
            PredictionConfidence.Low => "ConfidenceLow",
            PredictionConfidence.Medium => "ConfidenceMedium",
            PredictionConfidence.High => "ConfidenceHigh",
            _ => "ConfidenceNone",
        });

    private static string FormatStrategy(
        QuotaStrategyMode mode,
        QuotaStrategyPolicy policy,
        QuotaStrategyAssessment assessment,
        int sampleCount,
        bool trusted)
    {
        var name = Localization.Text(mode switch
        {
            QuotaStrategyMode.Sprint => "StrategyNameSprint",
            QuotaStrategyMode.Conservative => "StrategyNameConservative",
            _ => "StrategyNameBalanced",
        });
        if (assessment.IsBelowReserve)
            return Localization.Text("StrategyBelow", name, policy.FiveHourReservePercent, policy.WeeklyReservePercent);
        if (assessment.IsProjectedBelowReserve)
            return Localization.Text("StrategyProjected", name, policy.FiveHourReservePercent, sampleCount);
        return Localization.Text(
            trusted ? "StrategyHealthyTrusted" : "StrategyHealthyLearning",
            name);
    }

    private void Scenario_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (IsLoaded) RenderScenario();
    }

    private void RenderScenario()
    {
        if (_snapshot is null || Scenario.SelectedItem is not ComboBoxItem item) return;
        var selected = Enum.TryParse<UsageScenario>(item.Tag?.ToString(), out var scenario)
            ? scenario
            : UsageScenario.Current;
        var forecast = QuotaTimeline.Project(_snapshot, _prediction, selected, DateTimeOffset.UtcNow);
        Timeline.SetData(_observed, forecast, _snapshot.FiveHour?.ResetsAt ?? DateTimeOffset.UtcNow);
    }

    private void ZoomInButton_Click(object sender, RoutedEventArgs e) => Timeline.SetZoom(Timeline.ZoomFactor * 1.5);

    private void ZoomOutButton_Click(object sender, RoutedEventArgs e) => Timeline.SetZoom(Timeline.ZoomFactor / 1.5);

    private void Timeline_SelectedTimeChanged(object? sender, TimelineSelectionChangedEventArgs e)
    {
        CursorText.Text = Localization.Text("CursorEstimate", e.Time.ToLocalTime(), e.RemainingPercent);
    }

    private async void TaskButton_Click(object sender, RoutedEventArgs e)
    {
        if (_taskStartSnapshot is null)
        {
            _provider.RequestActiveRefresh();
            await LoadDataAsync();
            if (_snapshot is null) return;
            _taskStartSnapshot = _snapshot;
            TaskButton.Content = Localization.Text("FinishTask");
            TaskText.Text = Localization.Text("TaskStarted", _snapshot.CapturedAt.ToLocalTime());
            return;
        }

        var started = _taskStartSnapshot;
        _provider.RequestActiveRefresh();
        await LoadDataAsync();
        if (_snapshot is null) return;
        var summary = TaskBudgetAdvisor.Summarize(started, _snapshot);
        _taskStartSnapshot = null;
        TaskButton.Content = Localization.Text("StartTask");
        TaskText.Text = Localization.Text(
            "TaskSummary",
            summary.FiveHourConsumedPercent?.ToString(System.Globalization.CultureInfo.CurrentCulture) ?? "—",
            summary.WeeklyConsumedPercent?.ToString(System.Globalization.CultureInfo.CurrentCulture) ?? "—");
    }

    public void ApplyLanguage()
    {
        TaskButton.Content = Localization.Text(_taskStartSnapshot is null ? "StartTask" : "FinishTask");
        CursorText.Text = Localization.Text("SelectTime");
        Timeline.InvalidateVisual();
        _ = LoadDataAsync();
    }

    private void Window_Closed(object? sender, EventArgs e) => Dispose();

    public void Dispose()
    {
        _history.Dispose();
        GC.SuppressFinalize(this);
    }
}
