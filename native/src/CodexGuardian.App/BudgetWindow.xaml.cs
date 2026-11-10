using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using CodexGuardian.Core;
using CodexGuardian.Storage;

namespace CodexGuardian.App;

public partial class BudgetWindow : Window
{
    private readonly MainViewModel _model;
    private readonly DailyPlanStore _store;
    private readonly DailyPlanStore _eventStore;
    private readonly DailyPlanStore _activeStore;
    private bool _ready;
    private bool _saveFailed;
    private int _lastMode;
    private readonly Dictionary<int, (string Reserve, (bool Enabled, string Weight, string Minimum)[] Rows)> _drafts = [];
    private readonly DispatcherTimer _timer;
    private readonly ObservableCollection<PlanDayRow> _rows = [];
    private string? _scope;
    private DateTimeOffset? _reset;
    private DateOnly _openedDate = DateOnly.FromDateTime(DateTime.Now);

    public BudgetWindow(MainViewModel model, string? planPath = null)
    {
        _store = new DailyPlanStore(planPath);
        _activeStore = new DailyPlanStore(planPath is null ? GuardianDataPaths.GetFilePath("active-plan.json") : planPath + ".active");
        _eventStore = new DailyPlanStore(planPath is null ? GuardianDataPaths.GetFilePath("event-plan.json") : planPath + ".event");
        _model = model;
        _scope = model.LatestSnapshot?.ScopeKey;
        _reset = model.LatestSnapshot?.Weekly?.ResetsAt;
        InitializeComponent();
        BuildVersion.Text = "Codex Quota " + System.Reflection.Assembly.GetExecutingAssembly()
            .GetCustomAttributes(typeof(System.Reflection.AssemblyInformationalVersionAttribute), false)
            .Cast<System.Reflection.AssemblyInformationalVersionAttribute>().First().InformationalVersion;
        var hours = Enumerable.Range(0, 24).Select(value => value.ToString("D2", CultureInfo.InvariantCulture)).ToArray();
        var minutes = Enumerable.Range(0, 60).Select(value => value.ToString("D2", CultureInfo.InvariantCulture)).ToArray();
        EventAtHour.ItemsSource = hours;
        EventExpiresHour.ItemsSource = hours;
        EventAtMinute.ItemsSource = minutes;
        EventExpiresMinute.ItemsSource = minutes;
        EventTimezone.Text = Localization.Text("EventTimezone", TimeZoneInfo.Local.DisplayName);
        MaxHeight = Math.Max(MinHeight, SystemParameters.WorkArea.Height - 40);
        MaxWidth = Math.Max(MinWidth, SystemParameters.WorkArea.Width - 40);
        var today = DateOnly.FromDateTime(DateTime.Now);
        if (_reset is { } reset && reset > DateTimeOffset.Now)
        {
            var last = DateOnly.FromDateTime(reset.AddTicks(-1).LocalDateTime);
            var saved = _store.Load(_scope, reset);
            if (saved is not null) Reserve.Text = saved.ReservePercent.ToString(CultureInfo.CurrentCulture);
            for (var day = today; day <= last && _rows.Count < 31; day = day.AddDays(1))
            {
                var planned = saved?.Days.FirstOrDefault(item => item.Date == day);
                _rows.Add(new(day, saved is null || planned is not null,
                    planned?.Weight ?? 1, planned?.MinimumPercent ?? 0));
            }
            WindowNote.Text = Localization.Text("PlanReset", reset.ToLocalTime());
        }
        else WindowNote.Text = Localization.Text("BudgetUnknown");
        PlanGrid.ItemsSource = _rows;
        _ready = true;
        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _timer.Tick += (_, _) => UpdateResult();
        _timer.Start();
        Closed += (_, _) => _timer.Stop();
        UpdateResult();
    }

    private void Changed(object sender, TextChangedEventArgs e) { if (_ready) UpdateResult(); }

    private void ReloadContext(object sender, RoutedEventArgs e)
    {
        if (System.Windows.MessageBox.Show(this, Localization.Text("BudgetReloadConfirm"),
            Localization.Text("BudgetReload"), MessageBoxButton.OKCancel, MessageBoxImage.Question) != MessageBoxResult.OK) return;
        _ready = false;
        _saveFailed = false;
        _openedDate = DateOnly.FromDateTime(DateTime.Now);
        var snapshot = _model.LatestSnapshot;
        _scope = snapshot?.ScopeKey;
        _reset = snapshot?.Weekly?.ResetsAt;
        _drafts.Clear();
        _rows.Clear();
        BudgetMode.SelectedIndex = 0;
        _lastMode = 0;
        EventPanel.Visibility = Visibility.Collapsed;
        EventAtDate.SelectedDate = EventExpiresDate.SelectedDate = null;
        EventAtHour.SelectedIndex = EventAtMinute.SelectedIndex = -1;
        EventExpiresHour.SelectedIndex = EventExpiresMinute.SelectedIndex = -1;
        Reserve.Text = "10";
        if (_reset is { } reset && reset > DateTimeOffset.Now)
        {
            var saved = _store.Load(_scope, reset);
            Reserve.Text = (saved?.ReservePercent ?? 10).ToString(CultureInfo.CurrentCulture);
            var last = DateOnly.FromDateTime(reset.AddTicks(-1).LocalDateTime);
            for (var day = DateOnly.FromDateTime(DateTime.Now); day <= last && _rows.Count < 31; day = day.AddDays(1))
            {
                var planned = saved?.Days.FirstOrDefault(item => item.Date == day);
                _rows.Add(new(day, saved is null || planned is not null, planned?.Weight ?? 1, planned?.MinimumPercent ?? 0));
            }
            WindowNote.Text = Localization.Text("PlanReset", reset.ToLocalTime());
        }
        else WindowNote.Text = Localization.Text("BudgetUnknown");
        _ready = true;
        UpdateResult();
    }

    private DailyQuotaPlan Calculate(DateTimeOffset now, out PlannedWorkDay[] days, out int reserve)
    {
        days = [];
        reserve = 0;
        if (_model.LatestSnapshot is null) return new([], 0, "quota-unavailable");
        if (_model.LatestSnapshot?.ScopeKey != _scope || _model.LatestSnapshot?.Weekly?.ResetsAt != _reset)
            return new([], 0, "context-changed");
        if (!int.TryParse(Reserve.Text, out reserve)) return new([], 0, "invalid-plan");
        var selected = new List<PlannedWorkDay>();
        var today = DateOnly.FromDateTime(now.LocalDateTime);
        // Keep past edits visible, but never budget or forecast against elapsed dates.
        foreach (var row in _rows.Where(row => row.Enabled && row.Date >= today))
        {
            if (!double.TryParse(row.Weight, out var weight) || !int.TryParse(row.Minimum, out var minimum))
                return new([], 0, "invalid-plan");
            selected.Add(new(row.Date, weight, minimum));
        }
        days = selected.ToArray();
        if (BudgetMode.SelectedIndex == 1)
        {
            var resetEvent = ReadEvent();
            return resetEvent is null ? new([], 0, "event-invalid")
                : ResetEventBudget.Calculate(_model.LatestSnapshot, days, reserve, resetEvent, now, TimeZoneInfo.Local);
        }
        return DailyBudgetPlanner.Calculate(_model.LatestSnapshot, days, reserve, now, TimeZoneInfo.Local);
    }

    private void UpdateResult()
    {
        var now = DateTimeOffset.Now;
        var plan = Calculate(now, out var days, out _);
        var snapshot = _model.LatestSnapshot;
        RolloverNotice.Visibility = DateOnly.FromDateTime(now.LocalDateTime) != _openedDate
            ? Visibility.Visible : Visibility.Collapsed;
        var todayAllocation = plan.Days.FirstOrDefault(day => day.Date == DateOnly.FromDateTime(now.LocalDateTime));
        TodayAdvice.Text = !plan.IsValid ? Localization.Text("BudgetTodayUnavailable")
            : todayAllocation is null ? Localization.Text("BudgetTodayRest")
            : Localization.Text("BudgetTodayAdvice", todayAllocation.Percent);
        WeeklyRemaining.Text = snapshot?.Weekly is { } weekly && weekly.ResetsAt > now &&
            snapshot.GetWeeklyFreshness(now) == DataFreshness.Fresh ? $"{weekly.RemainingPercent}%" : "—";
        AvailableBudget.Text = plan.IsValid ? $"{plan.AvailablePercent:0.#}%" : "—";
        IdentityNotice.Visibility = string.IsNullOrWhiteSpace(_scope) ? Visibility.Visible : Visibility.Collapsed;
        SavePlanButton.IsEnabled = plan.IsValid && !string.IsNullOrWhiteSpace(_scope) && _reset is not null;
        foreach (var row in _rows)
        {
            var amount = plan.Days.FirstOrDefault(day => day.Date == row.Date);
            row.Allocation = amount is null ? "—" : amount.Percent.ToString("F1", CultureInfo.CurrentCulture);
        }
        Result.Text = plan.Error switch
        {
            null => Localization.Text("PlanTotal", plan.AvailablePercent),
            "event-invalid" => Localization.Text("EventInvalid"),
            "event-expiry" => Localization.Text("EventExpiryError"),
            "event-after-reset" => Localization.Text("EventAfterReset"),
            "context-changed" => Localization.Text("PlanContextChanged"),
            "minimum-exceeds-quota" => Localization.Text("PlanOverBudget"),
            "quota-unavailable" => Localization.Text("BudgetUnknown"),
            _ => Localization.Text("PlanInvalid"),
        };
        TopIssue.Text = plan.Error == "context-changed" && string.IsNullOrWhiteSpace(_scope)
            ? Localization.Text("PlanNoIdentity") + "\n" + Result.Text : Result.Text;
        TopIssue.Visibility = plan.IsValid ? Visibility.Collapsed : Visibility.Visible;
        if (_saveFailed)
        {
            TopIssue.Text = Localization.Text("BudgetSaveFailed");
            TopIssue.Visibility = Visibility.Visible;
        }
        var forecast = plan.IsValid
            ? WeeklyUsageForecaster.Project(_model.WeeklyBaseline, days.Where(day => plan.Days.Any(item => item.Date == day.Date)).ToArray(), plan.AvailablePercent)
            : new PlannedUsageForecast(null, null, 0);
        Forecast.Text = !plan.IsValid ? Localization.Text("BudgetForecastUnavailable")
            : !_model.IsPersonalizedAnalysisEnabled ? Localization.Text("PersonalizedAnalysisOff")
            : forecast.ExpectedConsumption is not { } demand
            ? Localization.Text("ForecastNeedHistory", _model.WeeklyBaseline.CompleteDays)
            : forecast.ReserveCrossingDate is { } crossing
                ? Localization.Text("ForecastCrossing", forecast.EvidenceDays, demand, crossing.ToString("yyyy-MM-dd", CultureInfo.CurrentCulture))
                : Localization.Text("ForecastWithin", forecast.EvidenceDays, demand);
        if (_model.IsPersonalizedAnalysisEnabled && forecast.ExpectedConsumption is { } partialDemand && _model.WeeklyBaseline.IsPartialDay)
            Forecast.Text = Localization.Text("ForecastPartial", forecast.EvidenceDays,
                _model.WeeklyBaseline.AverageObservedHours, partialDemand) +
                (forecast.ReserveCrossingDate is { } partialCrossing
                    ? Localization.Text("ForecastPartialCrossing", partialCrossing.ToString("yyyy-MM-dd", CultureInfo.CurrentCulture))
                    : Localization.Text("ForecastPartialUncertain"));
    }

    private void ApplyPreset(object sender, RoutedEventArgs e)
    {
        if (sender is not System.Windows.Controls.Button { Tag: string preset }) return;


        var today = DateOnly.FromDateTime(DateTime.Now);
        foreach (var row in _rows.Where(row => row.Date >= today))
        {
            row.Enabled = preset != "weekdays" || row.Date.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday);
            row.Weight = preset == "today" && row.Date == today ? "2" : "1";
        }
        PlanGrid.Items.Refresh();
        UpdateResult();
    }

    private PlannedResetEvent? ReadEvent()
    {
        var at = ReadSelectedTime(EventAtDate, EventAtHour, EventAtMinute);
        if (at is null) return null;
        DateTimeOffset? expiry = null;
        if (EventKind.SelectedIndex == 1)
        {
            expiry = ReadSelectedTime(EventExpiresDate, EventExpiresHour, EventExpiresMinute);
            if (expiry is null) return null;
        }
        return new(EventKind.SelectedIndex == 1 ? "card" : "announcement", at.Value, expiry);
    }

    private static DateTimeOffset? ReadSelectedTime(DatePicker date, System.Windows.Controls.ComboBox hour, System.Windows.Controls.ComboBox minute)
    {
        if (date.SelectedDate is not { } day || hour.SelectedIndex < 0 || minute.SelectedIndex < 0) return null;
        var local = DateTime.SpecifyKind(day.Date.AddHours(hour.SelectedIndex).AddMinutes(minute.SelectedIndex), DateTimeKind.Unspecified);
        if (TimeZoneInfo.Local.IsInvalidTime(local) || TimeZoneInfo.Local.IsAmbiguousTime(local)) return null;
        return new(local, TimeZoneInfo.Local.GetUtcOffset(local));
    }

    private void EventDateChanged(object sender, SelectionChangedEventArgs e) { if (_ready) UpdateResult(); }
    private void ChooseDate(object sender, RoutedEventArgs e)
    {
        if (sender is not System.Windows.Controls.Button { Tag: string tag }) return;
        var parts = tag.Split(':');
        var picker = parts[0] == "EventAt" ? EventAtDate : EventExpiresDate;
        picker.SelectedDate = DateTime.Today.AddDays(parts[1] == "1" ? 1 : 0);
    }

    private void EventChanged(object sender, SelectionChangedEventArgs e) { if (_ready) UpdateResult(); }
    private void ModeChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_ready) return;
        _drafts[_lastMode] = (Reserve.Text, _rows.Select(row => (row.Enabled, row.Weight, row.Minimum)).ToArray());
        _lastMode = BudgetMode.SelectedIndex;
        _ready = false;
        if (_drafts.TryGetValue(_lastMode, out var draft))
        {
            Reserve.Text = draft.Reserve;
            for (var i = 0; i < _rows.Count && i < draft.Rows.Length; i++)
            {
                _rows[i].Enabled = draft.Rows[i].Enabled;
                _rows[i].Weight = draft.Rows[i].Weight;
                _rows[i].Minimum = draft.Rows[i].Minimum;
            }
        }
        else if (_reset is { } currentReset &&
            (_lastMode == 1 ? _eventStore : _store).Load(_scope, currentReset) is { } stored)
        {
            Reserve.Text = stored.ReservePercent.ToString(CultureInfo.CurrentCulture);
            foreach (var row in _rows)
            {
                var day = stored.Days.FirstOrDefault(day => day.Date == row.Date);
                row.Enabled = day is not null;
                row.Weight = (day?.Weight ?? 1).ToString(CultureInfo.CurrentCulture);
                row.Minimum = (day?.MinimumPercent ?? 0).ToString(CultureInfo.CurrentCulture);
            }
        }
        EventPanel.Visibility = BudgetMode.SelectedIndex == 1 ? Visibility.Visible : Visibility.Collapsed;
        // Loading an event changes only event metadata; day edits stay visible until explicit save.
        if (BudgetMode.SelectedIndex == 1 && EventAtDate.SelectedDate is null && _reset is { } reset &&
            _eventStore.Load(_scope, reset)?.ResetEvent is { } saved)
        {
            EventKind.SelectedIndex = saved.Kind == "card" ? 1 : 0;
            EventAtDate.SelectedDate = saved.PlannedAt.LocalDateTime.Date;
            EventAtHour.SelectedIndex = saved.PlannedAt.LocalDateTime.Hour;
            EventAtMinute.SelectedIndex = saved.PlannedAt.LocalDateTime.Minute;
            if (saved.ExpiresAt is { } expires)
            {
                EventExpiresDate.SelectedDate = expires.LocalDateTime.Date;
                EventExpiresHour.SelectedIndex = expires.LocalDateTime.Hour;
                EventExpiresMinute.SelectedIndex = expires.LocalDateTime.Minute;
            }
        }
        PlanGrid.Items.Refresh();
        _ready = true;
        UpdateResult();
    }

    private void ChooseEffort(object sender, RoutedEventArgs e)
    {
        if (sender is not System.Windows.Controls.Button { DataContext: PlanDayRow row, Tag: string value }) return;
        row.Enabled = value != "0";
        row.Weight = (value == "0" ? 1 : double.Parse(value, CultureInfo.InvariantCulture)).ToString(CultureInfo.CurrentCulture);
        row.NotifyEffort();
        UpdateResult();
    }
    private void Save(object sender, RoutedEventArgs e)
    {


        var plan = Calculate(DateTimeOffset.Now, out var days, out var reserve);
        if (!plan.IsValid) { UpdateResult(); return; }
        if (string.IsNullOrWhiteSpace(_scope) || _reset is null)
        { Result.Text = Localization.Text("PlanNoIdentity"); return; }
        try
        {
            _saveFailed = false;
            var eventMode = BudgetMode.SelectedIndex == 1;
            (eventMode ? _eventStore : _store).Save(new(_scope, _reset.Value, reserve, days, eventMode ? ReadEvent() : null));
            _activeStore.Save(new(_scope, _reset.Value, reserve, days, eventMode ? ReadEvent() : null));
            Close();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        { _saveFailed = true; UpdateResult(); }
    }
}

public sealed class PlanDayRow(DateOnly date, bool enabled, double weight, int minimum) : INotifyPropertyChanged
{
    public DateOnly Date { get; } = date;
    public string DateLabel => Date.ToString("yyyy-MM-dd", CultureInfo.CurrentCulture);
    public bool Enabled { get; set; } = enabled;
    public string Weight { get; set; } = weight.ToString(CultureInfo.CurrentCulture);
    public string EffortLabel => !Enabled ? Localization.Text("DayRest") : Weight == "1" ? Localization.Text("DayNormal") : Weight == "2" ? Localization.Text("DayFocus") : Weight == (0.5).ToString(CultureInfo.CurrentCulture) ? Localization.Text("DayLight") : Weight;
    public void NotifyEffort() => PropertyChanged?.Invoke(this, new(nameof(EffortLabel)));
    public string Minimum { get; set; } = minimum.ToString(CultureInfo.CurrentCulture);
    private string _allocation = "—";
    public string Allocation
    {
        get => _allocation;
        set { if (_allocation == value) return; _allocation = value; PropertyChanged?.Invoke(this, new(nameof(Allocation))); }
    }
    public event PropertyChangedEventHandler? PropertyChanged;
}
