using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using CodexGuardian.Core;
using CodexGuardian.Providers;
using CodexGuardian.Storage;
using MediaBrush = System.Windows.Media.Brush;
using MediaColor = System.Windows.Media.Color;
using MediaColorConverter = System.Windows.Media.ColorConverter;
using MediaSolidColorBrush = System.Windows.Media.SolidColorBrush;

namespace CodexGuardian.App;

public sealed class MainViewModel : INotifyPropertyChanged, IDisposable
{
    private readonly CodexHybridQuotaProvider _provider = new();
    private readonly QuotaMonitor _monitor;
    private readonly JsonlQuotaHistoryStore _history = new();
    private bool _isRefreshing;
    private double _fiveHourRemaining;
    private double _weeklyRemaining;
    private string _fiveHourValue = "—";
    private string _weeklyValue = "—";
    private string _fiveHourReset = string.Empty;
    private string _weeklyReset = string.Empty;
    private string _fiveHourCompactStatus = string.Empty;
    private string _weeklyCompactStatus = string.Empty;
    private string _statusText = string.Empty;
    private string _statusDetail = string.Empty;
    private string _planText = string.Empty;
    private string _freshnessText = string.Empty;
    private string _lastUpdated = string.Empty;
    private string _refreshFeedback = string.Empty;
    private string _statusIcon = "·";
    private MediaBrush _fiveHourBrush = CreateBrush("#687185");
    private MediaBrush _weeklyBrush = CreateBrush("#687185");
    private MediaBrush _statusBrush = CreateBrush("#687185");
    private BurnRatePrediction _latestPrediction = new(0, null, PredictionConfidence.None, 0);
    private string? _lastFailureCode;

    public MainViewModel()
    {
        _monitor = new(_provider);
        ApplyInitialText();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public double FiveHourRemaining { get => _fiveHourRemaining; private set => SetField(ref _fiveHourRemaining, value); }
    public double WeeklyRemaining { get => _weeklyRemaining; private set => SetField(ref _weeklyRemaining, value); }
    public string FiveHourValue { get => _fiveHourValue; private set => SetField(ref _fiveHourValue, value); }
    public string WeeklyValue { get => _weeklyValue; private set => SetField(ref _weeklyValue, value); }
    public string FiveHourReset { get => _fiveHourReset; private set => SetField(ref _fiveHourReset, value); }
    public string WeeklyReset { get => _weeklyReset; private set => SetField(ref _weeklyReset, value); }
    public string FiveHourCompactStatus { get => _fiveHourCompactStatus; private set => SetField(ref _fiveHourCompactStatus, value); }
    public string WeeklyCompactStatus { get => _weeklyCompactStatus; private set => SetField(ref _weeklyCompactStatus, value); }
    public string StatusText { get => _statusText; private set => SetField(ref _statusText, value); }
    public string StatusDetail { get => _statusDetail; private set => SetField(ref _statusDetail, value); }
    public string PlanText { get => _planText; private set => SetField(ref _planText, value); }
    public string FreshnessText { get => _freshnessText; private set => SetField(ref _freshnessText, value); }
    public string LastUpdated { get => _lastUpdated; private set => SetField(ref _lastUpdated, value); }
    public string RefreshFeedback { get => _refreshFeedback; private set => SetField(ref _refreshFeedback, value); }
    public string StatusIcon { get => _statusIcon; private set => SetField(ref _statusIcon, value); }
    public MediaBrush FiveHourBrush { get => _fiveHourBrush; private set => SetField(ref _fiveHourBrush, value); }
    public MediaBrush WeeklyBrush { get => _weeklyBrush; private set => SetField(ref _weeklyBrush, value); }
    public MediaBrush StatusBrush { get => _statusBrush; private set => SetField(ref _statusBrush, value); }
    public bool IsRefreshing { get => _isRefreshing; private set => SetField(ref _isRefreshing, value); }
    public QuotaSnapshot? LatestSnapshot { get; private set; }
    public Advice? LatestAdvice { get; private set; }
    public BurnRatePrediction LatestPrediction => _latestPrediction;
    public WeeklyUsageBaseline WeeklyBaseline { get; private set; } = new(null, 0, "insufficient-complete-days");
    public bool IsPersonalizedAnalysisEnabled { get; private set; } = true;

    public void SetPersonalizedAnalysis(bool enabled)
    {
        IsPersonalizedAnalysisEnabled = enabled;
        if (enabled) return;
        WeeklyBaseline = new(null, 0, "analysis-disabled");
        _latestPrediction = new(0, null, PredictionConfidence.None, 0);
        LatestAdvice = null;
    }

    public void SetHistoryRetention(int days) => _history.SetRetention(TimeSpan.FromDays(Math.Clamp(days, 30, 365)));
    private bool _personalAccountMode;
    public void SetPersonalAccountMode(bool enabled)
    {
        if (_personalAccountMode == enabled) return;
        _personalAccountMode = enabled;
        _provider.SetPersonalAccountMode(enabled);
        LatestSnapshot = null;
        WeeklyBaseline = new(null, 0, "identity-mode-changed");
        _latestPrediction = new(0, null, PredictionConfidence.None, 0);
        ApplyFailure("identity_mode_changed");
    }

    public void ShowDemo(string scenario)
    {
        LatestSnapshot = DemoSnapshots.Create(scenario, DateTimeOffset.UtcNow);
        LatestAdvice = null;
        if (LatestSnapshot is { } snapshot) ApplySnapshotText(snapshot, _latestPrediction);
        else ApplyFailure("quota_metadata_not_found");
    }

    public async Task RefreshAsync(
        AdvisorContext? advisorContext = null,
        bool userInitiated = false,
        CancellationToken cancellationToken = default)
    {
        if (IsRefreshing) return;
        if (userInitiated) _provider.RequestActiveRefresh();
        var previousFiveHourObservedAt = LatestSnapshot?.FiveHourObservedAt;
        var previousWeeklyObservedAt = LatestSnapshot?.WeeklyObservedAt;
        IsRefreshing = true;
        try
        {
            var result = await _monitor.RefreshAsync(cancellationToken);
            if (result.Snapshot is not { } snapshot)
            {
                WeeklyBaseline = new(null, 0, "quota-unavailable");
                LatestSnapshot = null;
                LatestAdvice = null;
                _lastFailureCode = result.ErrorCode;
                ApplyFailure(result.ErrorCode);
                if (userInitiated) RefreshFeedback = Localization.Text("RefreshScanFailed");
                return;
            }

            var state = await UpdateHistoryAndPredictionAsync(snapshot, cancellationToken);
            snapshot = state.Snapshot;
            var prediction = state.Prediction;
            LatestSnapshot = snapshot;
            _latestPrediction = prediction;
            _lastFailureCode = null;
            ApplySnapshotText(snapshot, prediction);
            if (userInitiated)
            {
                var fiveHourUpdated = snapshot.FiveHourObservedAt > previousFiveHourObservedAt;
                var weeklyUpdated = snapshot.WeeklyObservedAt > previousWeeklyObservedAt;
                var feedbackKey = (fiveHourUpdated, weeklyUpdated) switch
                {
                    (true, true) => "RefreshScanUpdated",
                    (true, false) => "RefreshScanFiveHourOnly",
                    (false, true) => "RefreshScanWeeklyOnly",
                    _ => "RefreshScanNoChange",
                };
                RefreshFeedback = Localization.Text(feedbackKey);
                LastUpdated = RefreshFeedback;
            }
            LatestAdvice = !IsPersonalizedAnalysisEnabled || snapshot.OtherWindows is { Count: > 0 }
                ? new(NotificationLevel.Silent, "Analysis disabled or quota period not recognized.")
                : QuotaAdvisor.Evaluate(
                snapshot,
                prediction,
                advisorContext ?? new(false, false, false, false),
                DateTimeOffset.UtcNow);
        }
        finally
        {
            IsRefreshing = false;
        }
    }

    public void Relocalize()
    {
        if (LatestSnapshot is { } snapshot)
        {
            ApplySnapshotText(snapshot, _latestPrediction);
            return;
        }
        if (_lastFailureCode is not null) ApplyFailure(_lastFailureCode);
        else ApplyInitialText();
    }

    private void ApplyInitialText()
    {
        FiveHourReset = Localization.Text("WaitingMetadata");
        WeeklyReset = Localization.Text("WaitingMetadata");
        FiveHourCompactStatus = Localization.Text("WindowCompactUnavailable");
        WeeklyCompactStatus = Localization.Text("WindowCompactUnavailable");
        StatusText = Localization.Text("Connecting");
        StatusDetail = Localization.Text("RunTaskForSnapshot");
        PlanText = Localization.Text("LocalPlan");
        FreshnessText = Localization.Text("NoData");
        LastUpdated = Localization.Text("NotUpdated");
        RefreshFeedback = Localization.Text("RefreshQuota");
    }

    private void ApplySnapshotText(QuotaSnapshot snapshot, BurnRatePrediction prediction)
    {
        var now = DateTimeOffset.UtcNow;
        var fiveHourFreshness = snapshot.GetFiveHourFreshness(now);
        var weeklyFreshness = snapshot.GetWeeklyFreshness(now);
        ApplyWindow(snapshot.FiveHour, snapshot.FiveHourObservedAt, fiveHourFreshness, true);
        ApplyWindow(snapshot.Weekly, snapshot.WeeklyObservedAt, weeklyFreshness, false);
        PlanText = string.IsNullOrWhiteSpace(snapshot.PlanType) ? "CODEX" : snapshot.PlanType.ToUpperInvariant();
        var availableWindows = new[] { fiveHourFreshness, weeklyFreshness }.Count(value => value != DataFreshness.Unavailable);
        var currentWindows = new[] { fiveHourFreshness, weeklyFreshness }.Count(value => value == DataFreshness.Fresh);
        FreshnessText = currentWindows == 2
            ? Localization.Text("Live")
            : currentWindows > 0
                ? Localization.Text("Partial")
                : snapshot.Freshness switch
                {
                    DataFreshness.Stale => Localization.Text("Stale"),
                    DataFreshness.Expired => Localization.Text("Expired"),
                    _ => Localization.Text("NoData"),
                };
        StatusText = currentWindows == 2
            ? Localization.Text("SignalCurrent")
            : currentWindows > 0 || availableWindows > 0
                ? Localization.Text("PartialSignalCurrent")
                : snapshot.Freshness switch
                {
                    DataFreshness.Stale => Localization.Text("WaitingNewerEvent"),
                    _ => Localization.Text("EventExpired"),
                };
        StatusDetail = currentWindows == 2
            ? Localization.Text("LocalPrivacy")
            : currentWindows > 0 || availableWindows > 0
                ? Localization.Text("PartialSignalDetail")
                : Localization.Text("RunTaskRefresh");
        LastUpdated = Localization.Text("Captured", FormatAge(snapshot.CapturedAt));
        if (prediction.PercentPerHour > 0)
        {
            StatusDetail = prediction.EstimatedExhaustionEarliest is { } earliest &&
                           prediction.EstimatedExhaustionLatest is { } latest
                ? Localization.Text(
                    "UsingMayRunOutRange",
                    prediction.PercentPerHour,
                    earliest.ToLocalTime(),
                    latest.ToLocalTime())
                : Localization.Text("UsingResetFirst", prediction.PercentPerHour);
        }
    }

    private async Task<(QuotaSnapshot Snapshot, BurnRatePrediction Prediction)> UpdateHistoryAndPredictionAsync(
        QuotaSnapshot snapshot,
        CancellationToken cancellationToken)
    {
        WeeklyBaseline = new(null, 0, "insufficient-complete-days");
        try
        {
            if (snapshot.ScopeKey is null)
                return (snapshot, new(0, null, PredictionConfidence.None, 0));
            await _history.AppendAsync(snapshot, cancellationToken);
            if (!IsPersonalizedAnalysisEnabled)
                return (snapshot, new(0, null, PredictionConfidence.None, 0));
            var history = await _history.ReadSinceAsync(DateTimeOffset.UtcNow.AddDays(-7), cancellationToken);
            if (!IsPersonalizedAnalysisEnabled)
                return (snapshot, new(0, null, PredictionConfidence.None, 0));
            history = history.Where(entry => entry.ScopeKey == snapshot.ScopeKey).ToArray();
            WeeklyBaseline = WeeklyUsageForecaster.Measure(history, snapshot, DateTimeOffset.UtcNow, TimeZoneInfo.Local);
            snapshot = BackfillLastKnownWindows(snapshot, history);
            if (snapshot.FiveHour is not { } fiveHour ||
                snapshot.GetFiveHourFreshness(DateTimeOffset.UtcNow) == DataFreshness.Expired)
                return (snapshot, new(0, null, PredictionConfidence.None, 0));
            var currentWindow = history
                .Where(entry => entry.FiveHourResetsAt == fiveHour.ResetsAt && entry.FiveHourRemainingPercent is not null)
                .Select(entry => new QuotaHistoryPoint(entry.FiveHourObservedAt!.Value, entry.FiveHourRemainingPercent!.Value))
                .ToArray();
            var prediction = QuotaPredictor.Predict(currentWindow, fiveHour.ResetsAt);
            return (snapshot, prediction);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            StatusDetail = Localization.Text("HistoryUnavailable");
            return (snapshot, new(0, null, PredictionConfidence.None, 0));
        }
    }

    private void ApplyFailure(string? errorCode)
    {
        FiveHourRemaining = 0;
        WeeklyRemaining = 0;
        FiveHourValue = "—";
        WeeklyValue = "—";
        FiveHourCompactStatus = Localization.Text("WindowCompactUnavailable");
        WeeklyCompactStatus = Localization.Text("WindowCompactUnavailable");
        FiveHourBrush = CreateBrush("#687185");
        WeeklyBrush = CreateBrush("#687185");
        StatusBrush = CreateBrush("#687185");
        FreshnessText = Localization.Text("NoData");
        StatusText = Localization.Text("SignalUnavailable");
        StatusDetail = Localization.Text(errorCode switch
        {
            "sessions_not_found" => "ProviderSessionsNotFound",
            "sessions_unreadable" => "ProviderSessionsUnreadable",
            "quota_metadata_not_found" => "ProviderMetadataNotFound",
            _ => "ProviderUnavailable",
        });
        LastUpdated = Localization.Text("NoTrustedSnapshot");
        StatusIcon = "·";
    }

    private static string FormatReset(DateTimeOffset resetAt)
    {
        var local = resetAt.ToLocalTime();
        var remaining = resetAt - DateTimeOffset.UtcNow;
        if (remaining <= TimeSpan.Zero) return Localization.Text("ResetDue");
        if (remaining < TimeSpan.FromHours(24))
            return Localization.Text("ResetsIn", (int)remaining.TotalHours, remaining.Minutes, local);
        return Localization.Text(
            "ResetsDays",
            local.ToString("ddd HH:mm", System.Globalization.CultureInfo.CurrentCulture),
            Math.Ceiling(remaining.TotalDays));
    }

    private void ApplyWindow(QuotaWindow? window, DateTimeOffset? observedAt, DataFreshness freshness, bool isFiveHour)
    {
        var remaining = window?.RemainingPercent ?? 0;
        var value = window is null ? "—" : $"{remaining}%";
        var detail = window is null
            ? Localization.Text("WindowNotReported")
            : freshness == DataFreshness.Expired && observedAt is { } oldObservation
                ? Localization.Text("LastSeen", FormatAge(oldObservation))
                : FormatReset(window.ResetsAt);
        var compact = window is null
            ? Localization.Text("WindowCompactUnavailable")
            : freshness == DataFreshness.Expired && observedAt is { } compactObservation
                ? FormatAge(compactObservation)
                : FormatCompactReset(window.ResetsAt);
        var brush = QuotaBrush(window, freshness);

        if (isFiveHour)
        {
            FiveHourRemaining = remaining;
            FiveHourValue = value;
            FiveHourReset = detail;
            FiveHourCompactStatus = compact;
            FiveHourBrush = brush;
        }
        else
        {
            WeeklyRemaining = remaining;
            WeeklyValue = value;
            WeeklyReset = detail;
            WeeklyCompactStatus = compact;
            WeeklyBrush = brush;
        }
        StatusBrush = WorstBrush(FiveHourBrush, WeeklyBrush);
        StatusIcon = StatusIconFor(FiveHourBrush, WeeklyBrush);
    }

    internal static QuotaSnapshot BackfillLastKnownWindows(
        QuotaSnapshot snapshot,
        IReadOnlyList<QuotaHistoryEntry> history)
    {
        // A live response is authoritative: a removed short window must not be
        // resurrected from history after a plan/account change.
        if (snapshot.Source == "Codex app-server rate limits") return snapshot;
        var lastFiveHour = snapshot.FiveHour is null
            ? history.LastOrDefault(entry => entry.FiveHourRemainingPercent is not null && entry.FiveHourResetsAt is not null)
            : null;
        var lastWeekly = snapshot.Weekly is null
            ? history.LastOrDefault(entry => entry.WeeklyRemainingPercent is not null && entry.WeeklyResetsAt is not null)
            : null;
        return snapshot with
        {
            FiveHour = snapshot.FiveHour ?? (lastFiveHour is null ? null : new(
                100 - lastFiveHour.FiveHourRemainingPercent!.Value,
                300,
                lastFiveHour.FiveHourResetsAt!.Value)),
            Weekly = snapshot.Weekly ?? (lastWeekly is null ? null : new(
                100 - lastWeekly.WeeklyRemainingPercent!.Value,
                10080,
                lastWeekly.WeeklyResetsAt!.Value)),
            FiveHourCapturedAt = snapshot.FiveHourObservedAt ?? lastFiveHour?.FiveHourObservedAt,
            WeeklyCapturedAt = snapshot.WeeklyObservedAt ?? lastWeekly?.WeeklyObservedAt,
        };
    }

    private static string FormatCompactReset(DateTimeOffset resetAt)
    {
        var remaining = resetAt - DateTimeOffset.UtcNow;
        if (remaining <= TimeSpan.Zero) return Localization.Text("ResetDueCompact");
        if (remaining < TimeSpan.FromDays(1))
            return Localization.Text("ResetsCompactHours", Math.Max(0, (int)remaining.TotalHours), remaining.Minutes);
        return Localization.Text("ResetsCompactDays", (int)Math.Ceiling(remaining.TotalDays));
    }

    private static MediaSolidColorBrush QuotaBrush(QuotaWindow? window, DataFreshness freshness)
    {
        if (window is null || freshness != DataFreshness.Fresh || window.ResetsAt <= DateTimeOffset.UtcNow)
            return CreateBrush("#687185");
        if (window.RemainingPercent == 0) return CreateBrush("#FF7770");
        return CreateBrush("#79B9E8");
    }

    private static MediaSolidColorBrush WorstBrush(MediaBrush fiveHour, MediaBrush weekly)
    {
        var colors = new[] { fiveHour, weekly }.OfType<MediaSolidColorBrush>().Select(value => value.Color).ToArray();
        if (colors.Contains(MediaColor.FromRgb(255, 119, 112))) return CreateBrush("#FF7770");
        if (colors.Contains(MediaColor.FromRgb(240, 197, 102))) return CreateBrush("#F0C566");
        if (colors.Contains(MediaColor.FromRgb(72, 220, 203))) return CreateBrush("#48DCCB");
        return CreateBrush("#687185");
    }

    private static string StatusIconFor(MediaBrush fiveHour, MediaBrush weekly)
    {
        var colors = new[] { fiveHour, weekly }
            .OfType<MediaSolidColorBrush>()
            .Select(value => value.Color)
            .ToArray();
        if (colors.Contains(MediaColor.FromRgb(255, 119, 112))) return "!";
        if (colors.Contains(MediaColor.FromRgb(240, 197, 102))) return "▲";
        if (colors.Contains(MediaColor.FromRgb(72, 220, 203))) return "●";
        return "·";
    }

    private static MediaSolidColorBrush CreateBrush(string color) => new((MediaColor)MediaColorConverter.ConvertFromString(color));

    private static string FormatAge(DateTimeOffset capturedAt)
    {
        var age = DateTimeOffset.UtcNow - capturedAt;
        if (age < TimeSpan.FromMinutes(1)) return Localization.Text("JustNow");
        if (age < TimeSpan.FromHours(1)) return Localization.Text("MinutesAgo", (int)age.TotalMinutes);
        return Localization.Text("HoursAgo", (int)age.TotalHours);
    }

    private void SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        PropertyChanged?.Invoke(this, new(propertyName));
    }

    public void Dispose()
    {
        _monitor.Dispose();
        _history.Dispose();
    }
}
