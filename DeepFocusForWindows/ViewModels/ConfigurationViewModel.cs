using System;
using System.Collections.Generic;
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

    /// <summary>True while the user is in "edit focus apps" mode (shows full list + search).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(EditButtonText))]
    [NotifyPropertyChangedFor(nameof(IsSearchVisible))]
    [NotifyPropertyChangedFor(nameof(NoSelectionHintVisible))]
    private bool _isEditingFocusApps;

    public string DimmingLevelText     => $"{DimmingLevel}%";
    public string EditButtonText       => IsEditingFocusApps ? "Done" : "Edit";
    public bool   IsSearchVisible      => IsEditingFocusApps;
    public bool   IsFocusSessionSupported => _focusSession.IsSupported;

    /// <summary>Shown when not editing and no apps are selected yet.</summary>
    public bool NoSelectionHintVisible =>
        !IsEditingFocusApps && AvailableWindows.All(w => !w.IsSelected);

    /// <summary>Master list of all visible windows.</summary>
    public ObservableCollection<WindowInfoViewModel> AvailableWindows { get; } = [];

    /// <summary>Filtered/sorted view bound to the ListBox.</summary>
    public ObservableCollection<WindowInfoViewModel> FilteredWindows { get; } = [];

    // ── Partial handlers (auto-save on every change) ─────────────────────────

    partial void OnIsDimmingEnabledChanged(bool value)
    {
        if (value) _dimming.Enable();
        else       _dimming.Disable();
        _ = SaveAsync();
    }

    partial void OnDimmingLevelChanged(int value)
    {
        _dimming.DimmingLevel = value;
        _ = SaveAsync();
    }

    partial void OnStartOnBootChanged(bool value)
    {
        _startup.SetStartOnBoot(value);
        _ = SaveAsync();
    }

    partial void OnAutoDimOnFocusSessionChanged(bool value)
        => _ = SaveAsync();

    partial void OnSearchTextChanged(string value)
        => ApplyFilter();

    partial void OnIsEditingFocusAppsChanged(bool value)
    {
        if (!value) SearchText = string.Empty;
        ApplyFilter();
    }

    // ── Commands ────────────────────────────────────────────────────────────

    [RelayCommand]
    private void RefreshWindows()
    {
        // Preserve handles selected during this session.
        var currentHandles = AvailableWindows
            .Where(w => w.IsSelected)
            .Select(w => w.Handle)
            .ToHashSet();

        AvailableWindows.Clear();

        var windows = _windowEnum.GetVisibleWindows()
            .OrderBy(w => w.Title, StringComparer.OrdinalIgnoreCase);

        foreach (var w in windows)
        {
            var vm = new WindowInfoViewModel(w)
            {
                IsSelected = _settings.Settings.FocusApps
                    .Any(e =>
                        string.Equals(e.ProcessName, w.ProcessName, StringComparison.OrdinalIgnoreCase)
                        && string.Equals(e.Title, w.Title, StringComparison.OrdinalIgnoreCase))
                    || currentHandles.Contains(w.Handle),
                SelectionChanged = OnWindowSelectionChanged,
            };
            AvailableWindows.Add(vm);
        }

        ApplyFilter();
    }

    [RelayCommand]
    private void ToggleFocusAppsEdit()
        => IsEditingFocusApps = !IsEditingFocusApps;

    [RelayCommand]
    private void Close() => CloseRequested?.Invoke(this, EventArgs.Empty);

    // ── Events ──────────────────────────────────────────────────────────────

    public event EventHandler? CloseRequested;

    // ── Helpers ─────────────────────────────────────────────────────────────

    private void OnWindowSelectionChanged(WindowInfoViewModel vm)
    {
        SyncDimmingExclusions();
        _ = SaveAsync();
        OnPropertyChanged(nameof(NoSelectionHintVisible));

        // When not editing: refresh list so deselected items disappear.
        if (!IsEditingFocusApps)
            ApplyFilter();
    }

    private void LoadFromSettings()
    {
        var s = _settings.Settings;
        StartOnBoot           = s.StartOnBoot;
        IsDimmingEnabled      = s.IsDimmingEnabled;
        DimmingLevel          = s.DimmingLevel;
        AutoDimOnFocusSession = s.AutoDimOnFocusSession;
    }

    private async Task SaveAsync()
    {
        var s = _settings.Settings;
        s.StartOnBoot           = StartOnBoot;
        s.IsDimmingEnabled      = IsDimmingEnabled;
        s.DimmingLevel          = DimmingLevel;
        s.AutoDimOnFocusSession = AutoDimOnFocusSession;
        s.FocusApps = AvailableWindows
            .Where(w => w.IsSelected)
            .Select(w => new FocusAppEntry { ProcessName = w.ProcessName, Title = w.Title })
            .ToList();
        await _settings.SaveAsync();
    }

    private void SyncDimmingExclusions()
    {
        lock (_dimming.ExcludedHandles)
        {
            _dimming.ExcludedHandles.Clear();
            foreach (var w in AvailableWindows.Where(x => x.IsSelected))
                _dimming.ExcludedHandles.Add(w.Handle);
        }
    }

    private void ApplyFilter()
    {
        FilteredWindows.Clear();
        var query = SearchText?.Trim() ?? string.Empty;

        // When not editing: only show selected apps.
        // When editing: show all, selected first (each group sorted by title).
        IEnumerable<WindowInfoViewModel> source = IsEditingFocusApps
            ? AvailableWindows
            : AvailableWindows.Where(w => w.IsSelected);

        var sorted = source
            .Where(vm =>
                query.Length == 0
                || vm.Title.Contains(query, StringComparison.OrdinalIgnoreCase)
                || vm.ProcessName.Contains(query, StringComparison.OrdinalIgnoreCase))
            .OrderBy(vm => vm.IsSelected ? 0 : 1)
            .ThenBy(vm => vm.Title, StringComparer.OrdinalIgnoreCase);

        foreach (var vm in sorted)
            FilteredWindows.Add(vm);
    }
}
