using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DeepFocusForWindows.Models;
using DeepFocusForWindows.Native;
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
    [NotifyPropertyChangedFor(nameof(DimmingLevelText))]
    private int _dimmingLevel;

    [ObservableProperty]
    private bool _dimTaskbar;

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(EditButtonText))]
    [NotifyPropertyChangedFor(nameof(IsSearchVisible))]
    [NotifyPropertyChangedFor(nameof(NoSelectionHintVisible))]
    private bool _isEditingFocusApps;

    /// <summary>True while the preview button is toggled on.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PreviewButtonText))]
    private bool _isPreviewActive;

    /// <summary>True while the user is in window-picker mode.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PickButtonText))]
    private bool _isPickingWindow;

    public string DimmingLevelText  => $"{DimmingLevel}%";
    public string EditButtonText    => IsEditingFocusApps ? "Done" : "Edit";
    public bool   IsSearchVisible   => IsEditingFocusApps;
    public string PreviewButtonText => IsPreviewActive ? "◼ Stop Preview" : "▶ Preview";
    public string PickButtonText    => IsPickingWindow  ? "✕ Cancel Pick"  : "⊕ Pick Window";

    /// <summary>Shown when not editing and no apps are selected yet.</summary>
    public bool NoSelectionHintVisible =>
        !IsEditingFocusApps && AvailableWindows.All(w => !w.IsSelected);

    /// <summary>Master list of all visible windows.</summary>
    public ObservableCollection<WindowInfoViewModel> AvailableWindows { get; } = [];

    /// <summary>Filtered/sorted view bound to the ListBox.</summary>
    public ObservableCollection<WindowInfoViewModel> FilteredWindows { get; } = [];

    // ── Partial handlers ─────────────────────────────────────────────────────

    partial void OnDimmingLevelChanged(int value)
    {
        _dimming.DimmingLevel = value;
        _ = SaveAsync();
        TriggerSliderPreview();
    }

    partial void OnDimTaskbarChanged(bool value)
    {
        _dimming.DimTaskbar = value;
        _ = SaveAsync();
    }

    partial void OnStartOnBootChanged(bool value)
    {
        _startup.SetStartOnBoot(value);
        _ = SaveAsync();
    }

    partial void OnSearchTextChanged(string value)
        => ApplyFilter();

    partial void OnIsEditingFocusAppsChanged(bool value)
    {
        if (!value) SearchText = string.Empty;
        ApplyFilter();
    }

    // ── Slider preview (debounced) ────────────────────────────────────────────

    private bool _isSliderPreviewActive;
    private CancellationTokenSource? _sliderPreviewCts;

    private void TriggerSliderPreview()
    {
        // Only auto-preview when neither the Preview button nor a focus session is active.
        if (!_isSliderPreviewActive && !IsPreviewActive && !_focusSession.IsFocusActive)
        {
            _isSliderPreviewActive = true;
            _dimming.Enable(false);
        }

        _sliderPreviewCts?.Cancel();
        _sliderPreviewCts = new CancellationTokenSource();
        var cts = _sliderPreviewCts;

        _ = Task.Delay(1500, cts.Token).ContinueWith(t =>
        {
            if (t.IsCanceled) return;
            Dispatcher.UIThread.Post(() =>
            {
                if (_isSliderPreviewActive)
                {
                    _isSliderPreviewActive = false;
                    _dimming.Disable(false);
                }
            });
        }, TaskScheduler.Default);
    }

    // ── Commands ─────────────────────────────────────────────────────────────

    [RelayCommand]
    private void RefreshWindows()
    {
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
    private void TogglePreview()
    {
        if (!IsPreviewActive)
        {
            IsPreviewActive = true;
            _dimming.Enable(false);
        }
        else
        {
            IsPreviewActive = false;
            _dimming.Disable(false);
        }
    }

    [RelayCommand]
    private void TogglePickWindow()
    {
        if (!IsPickingWindow)
            StartPickingWindow();
        else
            StopPickingWindow();
    }

    [RelayCommand]
    private void Close() => CloseRequested?.Invoke(this, EventArgs.Empty);

    // ── Window picker ─────────────────────────────────────────────────────────

    private IntPtr _configWindowHwnd;
    private IntPtr _mouseHookHandle;
    private NativeMethods.LowLevelMouseProc? _mouseHookProc; // must be field to prevent GC

    /// <summary>Called by the code-behind to give the VM the config window's HWND.</summary>
    public void SetConfigWindowHwnd(IntPtr hwnd) => _configWindowHwnd = hwnd;

    private void StartPickingWindow()
    {
        IsPickingWindow = true;
        _mouseHookProc  = PickerMouseHookProc;
        _mouseHookHandle = NativeMethods.SetWindowsHookExMouse(
            NativeMethods.WH_MOUSE_LL, _mouseHookProc, IntPtr.Zero, 0);
    }

    private void StopPickingWindow()
    {
        if (_mouseHookHandle != IntPtr.Zero)
        {
            NativeMethods.UnhookWindowsHookEx(_mouseHookHandle);
            _mouseHookHandle = IntPtr.Zero;
        }
        _mouseHookProc  = null;
        IsPickingWindow = false;
    }

    private IntPtr PickerMouseHookProc(int nCode, IntPtr wParam, IntPtr lParam)
    {
        var hookHandle = _mouseHookHandle; // local copy for CallNextHookEx

        if (nCode >= 0 && (int)wParam == NativeMethods.WM_LBUTTONDOWN)
        {
            var hookStruct = Marshal.PtrToStructure<NativeMethods.MSLLHOOKSTRUCT>(lParam);
            var clickedHwnd = NativeMethods.WindowFromPoint(hookStruct.pt);
            var rootHwnd    = NativeMethods.GetAncestor(clickedHwnd, NativeMethods.GA_ROOT);

            // Ignore clicks on the config window itself.
            if (rootHwnd != _configWindowHwnd && rootHwnd != IntPtr.Zero)
            {
                var titleSb = new StringBuilder(256);
                NativeMethods.GetWindowText(rootHwnd, titleSb, titleSb.Capacity);
                var title = titleSb.ToString();

                NativeMethods.GetWindowThreadProcessId(rootHwnd, out uint pid);
                var procName = string.Empty;
                try { procName = Process.GetProcessById((int)pid).ProcessName; } catch { }

                Dispatcher.UIThread.Post(() =>
                {
                    AddPickedWindow(rootHwnd, title, procName);
                    StopPickingWindow();
                });
            }
        }

        return NativeMethods.CallNextHookEx(hookHandle, nCode, wParam, lParam);
    }

    private void AddPickedWindow(IntPtr rootHwnd, string title, string procName)
    {
        // If already in the list, just select it.
        var existing = AvailableWindows.FirstOrDefault(w => w.Handle == rootHwnd);
        if (existing is not null)
        {
            if (!existing.IsSelected)
                existing.IsSelected = true;
            return;
        }

        // Add as a new entry.
        var info = new WindowInfo { Handle = rootHwnd, Title = title, ProcessName = procName };
        var vm   = new WindowInfoViewModel(info)
        {
            IsSelected       = true,
            SelectionChanged = OnWindowSelectionChanged,
        };
        AvailableWindows.Add(vm);
        ApplyFilter();
        SyncDimmingExclusions();
        _ = SaveAsync();
    }

    // ── Called by code-behind on window closing ──────────────────────────────

    public void OnWindowClosing()
    {
        StopPickingWindow();

        if (IsPreviewActive)
        {
            IsPreviewActive = false;
            _dimming.Disable();
        }

        if (_isSliderPreviewActive)
        {
            _sliderPreviewCts?.Cancel();
            _isSliderPreviewActive = false;
            _dimming.Disable();
        }
    }

    // ── Events ───────────────────────────────────────────────────────────────

    public event EventHandler? CloseRequested;

    // ── Helpers ──────────────────────────────────────────────────────────────

    private void OnWindowSelectionChanged(WindowInfoViewModel vm)
    {
        SyncDimmingExclusions();
        _ = SaveAsync();
        OnPropertyChanged(nameof(NoSelectionHintVisible));

        if (!IsEditingFocusApps)
            ApplyFilter();
    }

    private void LoadFromSettings()
    {
        var s = _settings.Settings;
        StartOnBoot  = s.StartOnBoot;
        DimmingLevel = s.DimmingLevel;
        DimTaskbar   = s.DimTaskbar;
    }

    private async Task SaveAsync()
    {
        var s = _settings.Settings;
        s.StartOnBoot  = StartOnBoot;
        s.DimmingLevel = DimmingLevel;
        s.DimTaskbar   = DimTaskbar;
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
