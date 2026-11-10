using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.ComponentModel;
using CodexGuardian.Storage;
using Microsoft.Win32;
using WpfMessageBox = System.Windows.MessageBox;

namespace CodexGuardian.App;

public partial class SettingsWindow : Window, IDisposable
{
    private readonly GuardianSettingsStore _store;
    private readonly GuardianSettings _initialSettings;
    private readonly Action<GuardianSettings>? _previewSettings;
    private readonly AmbientEffectRenderer _previewRenderer;
    private bool _isInitializing = true;
    private bool _previewCommitted;
    private bool _isSaving;
    private bool _disposed;

    public SettingsWindow(
        GuardianSettingsStore store,
        GuardianSettings settings,
        Action<GuardianSettings>? previewSettings = null)
    {
        _initialSettings = settings;
        _previewSettings = previewSettings;
        InitializeComponent();
        _previewRenderer = new AmbientEffectRenderer(ThemePreviewEffects);
        _store = store;
        AlwaysOnTop.IsChecked = settings.AlwaysOnTop;
        StartWithWindows.IsChecked = settings.StartWithWindows;
        FollowCodex.IsChecked = settings.FollowCodex;
        EnableNotifications.IsChecked = settings.EnableActiveNotifications;
        DoNotDisturb.IsChecked = settings.DoNotDisturb;
        ReduceMotion.IsChecked = settings.ReduceMotion;
        LowPresenceMode.IsChecked = settings.LowPresenceMode;
        EnablePersonalizedAnalysis.IsChecked = settings.EnablePersonalizedAnalysis;
        PersonalAccountOnly.IsChecked = settings.PersonalAccountOnly;
        SelectByTag(LanguageCode, settings.LanguageCode);
        foreach (var item in StrategyMode.Items.OfType<ComboBoxItem>())
        {
            if (string.Equals(item.Tag?.ToString(), settings.StrategyMode, StringComparison.OrdinalIgnoreCase))
            {
                StrategyMode.SelectedItem = item;
                break;
            }
        }
        StrategyMode.SelectedIndex = Math.Max(0, StrategyMode.SelectedIndex);
        foreach (var item in HistoryRetention.Items.OfType<ComboBoxItem>())
        {
            if (string.Equals(item.Tag?.ToString(), settings.HistoryRetentionDays.ToString(System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal))
            {
                HistoryRetention.SelectedItem = item;
                break;
            }
        }
        HistoryRetention.SelectedIndex = Math.Max(0, HistoryRetention.SelectedIndex);
        foreach (var item in ThemeMode.Items.OfType<ComboBoxItem>())
        {
            if (string.Equals(item.Tag?.ToString(), settings.ThemeMode, StringComparison.Ordinal))
            {
                ThemeMode.SelectedItem = item;
                break;
            }
        }
        ThemeMode.SelectedIndex = Math.Max(0, ThemeMode.SelectedIndex);
        SelectByTag(AmbientEffectMode, settings.AmbientEffect);

        foreach (var item in RefreshInterval.Items.OfType<ComboBoxItem>())
        {
            if (string.Equals(item.Tag?.ToString(), settings.RefreshSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal))
            {
                RefreshInterval.SelectedItem = item;
                break;
            }
        }
        RefreshInterval.SelectedIndex = Math.Max(0, RefreshInterval.SelectedIndex);
        _isInitializing = false;
        UpdateAppearancePreview(settings);
    }

    public GuardianSettings? SavedSettings { get; private set; }
    public bool WasSaved => _previewCommitted;

    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left) DragMove();
    }

    private async void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        if (_isSaving) return;
        SavedSettings = BuildSettings();

        if (SavedSettings.FollowCodex && !FollowCodexRegistration.IsWatcherAvailable)
        {
            WpfMessageBox.Show(this, Localization.Text("FollowCodexUnavailable"), Localization.Text("Settings"),
                MessageBoxButton.OK, MessageBoxImage.Warning);
            SavedSettings = null;
            return;
        }

        var startupChanged = SavedSettings.StartWithWindows != _initialSettings.StartWithWindows;
        var followChanged = SavedSettings.FollowCodex != _initialSettings.FollowCodex;
        if ((startupChanged && !StartupRegistration.TryApply(SavedSettings.StartWithWindows)) ||
            (followChanged && !FollowCodexRegistration.TryApply(SavedSettings.FollowCodex)))
        {
            if (startupChanged) StartupRegistration.TryApply(_initialSettings.StartWithWindows);
            if (followChanged) FollowCodexRegistration.TryApply(_initialSettings.FollowCodex);
            WpfMessageBox.Show(this, Localization.Text("FollowCodexRegistrationFailed"), Localization.Text("Settings"),
                MessageBoxButton.OK, MessageBoxImage.Warning);
            SavedSettings = null;
            return;
        }
        try
        {
            _isSaving = true;
            await _store.SaveAsync(SavedSettings);
        }
        catch (Exception exception) when (exception is System.IO.IOException or UnauthorizedAccessException)
        {
            if (startupChanged) StartupRegistration.TryApply(_initialSettings.StartWithWindows);
            if (followChanged) FollowCodexRegistration.TryApply(_initialSettings.FollowCodex);
            SavedSettings = null;
            WpfMessageBox.Show(this, Localization.Text("SettingsSaveFailed"), Localization.Text("Settings"),
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        finally
        {
            _isSaving = false;
        }
        _previewCommitted = true;
        Close();
    }

    private void StartupMode_Changed(object sender, RoutedEventArgs e)
    {
        if (_isInitializing) return;
        if (ReferenceEquals(sender, StartWithWindows) && StartWithWindows.IsChecked == true)
            FollowCodex.IsChecked = false;
        else if (ReferenceEquals(sender, FollowCodex) && FollowCodex.IsChecked == true)
            StartWithWindows.IsChecked = false;
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e) => Close();

    private void PreviewSetting_Changed(object sender, RoutedEventArgs e)
    {
        if (_isInitializing) return;
        var settings = BuildSettings();
        UpdateAppearancePreview(settings);
        _previewSettings?.Invoke(settings);
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (_isSaving)
        {
            e.Cancel = true;
            base.OnClosing(e);
            return;
        }
        if (!_previewCommitted) _previewSettings?.Invoke(_initialSettings);
        Dispose();
        base.OnClosing(e);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _previewRenderer.Dispose();
        GC.SuppressFinalize(this);
    }

    private void UpdateAppearancePreview(GuardianSettings settings)
    {
        var palette = GuardianAppearanceCatalog.Resolve(settings.ThemeMode);
        ThemePreviewSurface.Background = new SolidColorBrush(palette.Canvas);
        ThemePreviewSurface.BorderBrush = new SolidColorBrush(palette.Hairline);
        ThemePreviewChip.Background = new SolidColorBrush(palette.Surface);
        ThemePreviewAccent.Fill = new SolidColorBrush(palette.Accent);
        ThemePreviewPrimary.Foreground = new SolidColorBrush(palette.Accent);
        ThemePreviewPrimaryTrack.Background = new SolidColorBrush(palette.Raised);
        ThemePreviewPrimaryFill.Background = new SolidColorBrush(palette.Accent);
        ThemePreviewSecondaryTrack.Background = new SolidColorBrush(palette.Raised);
        ThemePreviewSecondaryFill.Background = new SolidColorBrush(palette.Accent) { Opacity = 0.68 };
        _previewRenderer.Apply(settings.AmbientEffect, settings.ReduceMotion, isVisible: true);
    }

    private GuardianSettings BuildSettings()
    {
        var selected = (ComboBoxItem)RefreshInterval.SelectedItem;
        var refreshSeconds = int.Parse(selected.Tag!.ToString()!, System.Globalization.CultureInfo.InvariantCulture);
        return new(
            refreshSeconds,
            AlwaysOnTop.IsChecked == true,
            StartWithWindows.IsChecked == true,
            EnableNotifications.IsChecked == true,
            DoNotDisturb.IsChecked == true,
            ReduceMotion.IsChecked == true,
            ((ComboBoxItem)StrategyMode.SelectedItem).Tag!.ToString()!,
            int.Parse(((ComboBoxItem)HistoryRetention.SelectedItem).Tag!.ToString()!, System.Globalization.CultureInfo.InvariantCulture),
            ((ComboBoxItem)ThemeMode.SelectedItem).Tag!.ToString()!,
            ((ComboBoxItem)LanguageCode.SelectedItem).Tag!.ToString()!,
            EnablePersonalizedAnalysis.IsChecked == true,
            LowPresenceMode.IsChecked == true,
            ((ComboBoxItem)AmbientEffectMode.SelectedItem).Tag!.ToString()!,
            FollowCodex.IsChecked == true,
            PersonalAccountOnly.IsChecked == true);
    }

    private void TestNotificationButton_Click(object sender, RoutedEventArgs e)
    {
        if (System.Windows.Application.Current is App app)
        {
            var native = app.ShowQuotaNotification(
                Localization.Text("TestNotificationMessage"),
                DateTimeOffset.UtcNow.AddHours(1));
            NotificationTestStatus.Text = Localization.Text(
                native ? "TestNotificationNative" : "TestNotificationFallback");
        }
    }

    private async void ExportHistoryButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Title = Localization.Text("ExportDialogTitle"),
            FileName = $"codex-guardian-history-{DateTime.Now:yyyyMMdd}.json",
            DefaultExt = ".json",
            Filter = Localization.Text("JsonFilter"),
        };
        if (dialog.ShowDialog(this) != true) return;

        using var history = new JsonlQuotaHistoryStore();
        await history.ExportSanitizedAsync(dialog.FileName);
        WpfMessageBox.Show(this, Localization.Text("ExportCompleteMessage"),
            Localization.Text("ExportCompleteTitle"), MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private async void ClearHistoryButton_Click(object sender, RoutedEventArgs e)
    {
        var first = WpfMessageBox.Show(this,
            Localization.Text("ClearFirstMessage"),
            Localization.Text("ClearFirstTitle"), MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No);
        if (first != MessageBoxResult.Yes) return;

        var second = WpfMessageBox.Show(this,
            Localization.Text("ClearConfirmMessage"),
            Localization.Text("ClearConfirmTitle"), MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No);
        if (second != MessageBoxResult.Yes) return;

        using var history = new JsonlQuotaHistoryStore();
        await history.ClearAsync();
        WpfMessageBox.Show(this, Localization.Text("HistoryClearedMessage"), Localization.Text("HistoryClearedTitle"),
            MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private static void SelectByTag(System.Windows.Controls.ComboBox comboBox, string tag)
    {
        foreach (var item in comboBox.Items.OfType<ComboBoxItem>())
        {
            if (!string.Equals(item.Tag?.ToString(), tag, StringComparison.OrdinalIgnoreCase)) continue;
            comboBox.SelectedItem = item;
            break;
        }
        comboBox.SelectedIndex = Math.Max(0, comboBox.SelectedIndex);
    }
}
