using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DeepFocusForWindows.Models;
using DeepFocusForWindows.Services;

namespace DeepFocusForWindows.ViewModels;

public partial class ConfigurationViewModel : ViewModelBase
{
    private readonly ISettingsService           _settings;
    private readonly IWindowEnumerationService  _windowEnum;
    private readonly IDimmingService            _dimming;
    private readonly IStartupService            _startup;
    private readonly IFocusSessionService       _focusSession;

    public ConfigurationViewModel(
        ISettingsService          settings,
        IWindowEnumerationService windowEnum,
        IDimmingService           dimming,
        IStartupService           startup,
        IFocusSessionService      focusSession)
    {
        _settings     = settings;
        _windowEnum   = windowEnum;
        _dimming      = dimming;
        _startup      = startup;
        _focusSession = focusSession;

        LoadFromSettings();
        RefreshWindows();
    }

    // ── Bindable properties ─────────────────────────────────────────────────

    [ObservableProperty]
    private bool _startOnBoot;

    [ObservableProperty]
    private bool _isDimmingEnabled;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DimmingLevelText))]
    private int _dimmingLevel;

    [ObservableProperty]
    private bool _autoDimOnFocusSession;

    [ObservableProperty]
    private string _searchText = string.Empty;

    public string DimmingLevelText => $"{DimmingLevel}%";

    public bool IsFocusSessionSupported => _focusSession.IsSupported;

    /// <summary>Master list of all visible windows, sorted by title.</summary>
    public ObservableCollection<WindowInfoViewModel> AvailableWindows { get; } = [];

    /// <summary>Filtered view of AvailableWindows bound to the ListBox.</summary>
    public ObservableCollection<WindowInfoViewModel> FilteredWindows { get; } = [];

    // ── Partial handlers ────────────────────────────────────────────────────

    partial void OnIsDimmingEnabledChanged(bool value)
    {
        if (value) _dimming.Enable();
        else       _dimming.Disable();
    }

    partial void OnDimmingLevelChanged(int value)
        => _dimming.DimmingLevel = value;

    partial void OnSearchTextChanged(string value)
        => ApplyFilter();

    // ── Commands ────────────────────────────────────────────────────────────

    [RelayCommand]
    private void RefreshWindows()
    {
        // Preserve handles that were selected during this session.
        var currentHandles = AvailableWindows
            .Where(w => w.IsSelected)
            .Select(w => w.Handle)
            .ToHashSet();

        AvailableWindows.Clear();

        // Sort by title so the list is easy to scan.
        var windows = _windowEnum.GetVisibleWindows()
            .OrderBy(w => w.Title, StringComparer.OrdinalIgnoreCase);

        foreach (var w in windows)
        {
            var vm = new WindowInfoViewModel(w)
            {
                // Re-select if: saved in settings (matched by ProcessName + Title)
                //               OR selected earlier in this session (matched by HWND).
                IsSelected = _settings.Settings.FocusApps
                    .Any(e =>
                        string.Equals(e.ProcessName, w.ProcessName, StringComparison.OrdinalIgnoreCase)
                        && string.Equals(e.Title, w.Title, StringComparison.OrdinalIgnoreCase))
                    || currentHandles.Contains(w.Handle)
            };
            AvailableWindows.Add(vm);
        }

        ApplyFilter();
    }

    [RelayCommand]
    private async Task ApplyAsync()
    {
        ApplyToSettings();
        await _settings.SaveAsync();
        SyncDimmingExclusions();
        _startup.SetStartOnBoot(StartOnBoot);
    }

    [RelayCommand]
    private async Task SaveAndCloseAsync()
    {
        await ApplyAsync();
        CloseRequested?.Invoke(this, EventArgs.Empty);
    }

    // ── Events ──────────────────────────────────────────────────────────────

    public event EventHandler? CloseRequested;

    // ── Helpers ─────────────────────────────────────────────────────────────

    private void LoadFromSettings()
    {
        var s = _settings.Settings;
        StartOnBoot            = s.StartOnBoot;
        IsDimmingEnabled       = s.IsDimmingEnabled;
        DimmingLevel           = s.DimmingLevel;
        AutoDimOnFocusSession  = s.AutoDimOnFocusSession;
    }

    private void ApplyToSettings()
    {
        var s = _settings.Settings;
        s.StartOnBoot           = StartOnBoot;
        s.IsDimmingEnabled      = IsDimmingEnabled;
        s.DimmingLevel          = DimmingLevel;
        s.AutoDimOnFocusSession = AutoDimOnFocusSession;
        // Save by (ProcessName + Title) so we can re-match after a restart.
        s.FocusApps = AvailableWindows
            .Where(w => w.IsSelected)
            .Select(w => new FocusAppEntry { ProcessName = w.ProcessName, Title = w.Title })
            .ToList();
    }

    private void SyncDimmingExclusions()
    {
        // Push the exact HWNDs so the overlay only reveals those specific windows,
        // not every window belonging to the same wrapper process.
        _dimming.ExcludedHandles.Clear();
        foreach (var w in AvailableWindows.Where(x => x.IsSelected))
            _dimming.ExcludedHandles.Add(w.Handle);
    }

    private void ApplyFilter()
    {
        FilteredWindows.Clear();
        var query = SearchText?.Trim() ?? string.Empty;

        foreach (var vm in AvailableWindows)
        {
            if (query.Length == 0
                || vm.Title.Contains(query, StringComparison.OrdinalIgnoreCase)
                || vm.ProcessName.Contains(query, StringComparison.OrdinalIgnoreCase))
            {
                FilteredWindows.Add(vm);
            }
        }
    }
}
