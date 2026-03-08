using System;
using System.Collections.Generic;
using System.Text;
using Avalonia.Threading;
using DeepFocusForWindows.Native;
using DeepFocusForWindows.Views;

namespace DeepFocusForWindows.Services;

/// <summary>
/// Controls the full-screen dimming overlay.
/// Uses reference counting so multiple callers (focus session, preview) can independently
/// enable/disable dimming — the overlay only appears when at least one caller has enabled it.
/// All public methods must be called on the UI thread.
/// </summary>
public class DimmingService : IDimmingService
{
    private DimmingOverlayWindow? _overlay;
    private IntPtr _configWindowHandle;
    private int _dimmingLevel = 70;
    private bool _dimTaskbar = true;
    private int _activeCount;
    private bool _isTemporarilyDisabled;
    private readonly List<IntPtr> _minimizedByUs = [];

    public bool IsActive => _activeCount > 0 && !_isTemporarilyDisabled;
    public bool IsTemporarilyDisabled => _isTemporarilyDisabled;

    public int DimmingLevel
    {
        get => _dimmingLevel;
        set
        {
            _dimmingLevel = Math.Clamp(value, 0, 100);
            _overlay?.ApplyAlpha(_dimmingLevel);
        }
    }

    public bool DimTaskbar
    {
        get => _dimTaskbar;
        set
        {
            _dimTaskbar = value;
            if (_overlay is not null) _overlay.DimTaskbar = value;
        }
    }

    public ISet<IntPtr> ExcludedHandles { get; } = new HashSet<IntPtr>();

    public event EventHandler? StateChanged;

    public void SetConfigWindowHandle(IntPtr hwnd)
    {
        _configWindowHandle = hwnd;
        _overlay?.SetConfigWindowHandle(hwnd);
    }

    /// <summary>
    /// Increments the enable count. On the first Enable() call the overlay is shown
    /// and all non-focus visible windows are minimized.
    /// </summary>
    public void Enable(bool minimizeNonFocusWindows = true)
    {
        Dispatcher.UIThread.VerifyAccess();
        _isTemporarilyDisabled = false;

        bool wasInactive = _activeCount == 0;
        _activeCount++;

        if (wasInactive)
        {
            ShowOverlay();

            if (minimizeNonFocusWindows)
            {
                MinimizeNonFocusWindows();
            }
        }

        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Decrements the enable count. When the count reaches zero the overlay is hidden
    /// and all windows minimized by us are restored.
    /// </summary>
    public void Disable(bool restoreNonFocusWindows = true)
    {
        Dispatcher.UIThread.VerifyAccess();
        if (_activeCount == 0) return;
        _activeCount--;

        if (_activeCount == 0)
        {
            _isTemporarilyDisabled = false;
            HideOverlay();

            if (restoreNonFocusWindows)
            {
                RestoreMinimizedByUs();
            }
        }

        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Temporarily hide the overlay (ESC×2) without affecting the count or restoring windows.</summary>
    public void TemporarilyDisable()
    {
        Dispatcher.UIThread.VerifyAccess();
        if (_activeCount == 0) return;
        _isTemporarilyDisabled = true;
        HideOverlay();
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Re-show the overlay if it was temporarily disabled.</summary>
    public void RestoreFromTemporaryDisable()
    {
        Dispatcher.UIThread.VerifyAccess();
        if (_activeCount == 0 || !_isTemporarilyDisabled) return;
        _isTemporarilyDisabled = false;
        ShowOverlay();
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Ensure all active-count enables are balanced to zero and windows are restored.
    /// Call on application exit to guarantee no windows remain minimized.
    /// </summary>
    public void ForceDisableAll()
    {
        Dispatcher.UIThread.VerifyAccess();
        _activeCount = 0;
        _isTemporarilyDisabled = false;
        HideOverlay();
        RestoreMinimizedByUs();
    }

    private void ShowOverlay()
    {
        if (_overlay is null)
        {
            _overlay = new DimmingOverlayWindow(ExcludedHandles, _dimmingLevel);
            _overlay.SetConfigWindowHandle(_configWindowHandle);
            _overlay.DimTaskbar = _dimTaskbar;
        }

        _overlay.Show();
    }

    private void HideOverlay()
    {
        _overlay?.Hide();
    }

    private void MinimizeNonFocusWindows()
    {
        _minimizedByUs.Clear();

        IntPtr[] focusHandles;
        lock (ExcludedHandles)
            focusHandles = [.. ExcludedHandles];

        var focusSet = new HashSet<IntPtr>(focusHandles);
        var cfgHwnd = _configWindowHandle;
        var toMinimize = new List<IntPtr>();

        NativeMethods.EnumWindows((hwnd, _) =>
        {
            if (!NativeMethods.IsWindowVisible(hwnd) || NativeMethods.IsIconic(hwnd))
                return true;

            if (focusSet.Contains(hwnd) || hwnd == cfgHwnd)
                return true;

            // Only real application windows (must have a title).
            var sb = new StringBuilder(2);
            if (NativeMethods.GetWindowText(hwnd, sb, sb.Capacity) == 0)
                return true;

            toMinimize.Add(hwnd);
            return true;
        }, IntPtr.Zero);

        foreach (var hwnd in toMinimize)
        {
            NativeMethods.ShowWindow(hwnd, NativeMethods.SW_SHOWMINNOACTIVE);
            _minimizedByUs.Add(hwnd);
        }
    }

    private void RestoreMinimizedByUs()
    {
        foreach (var hwnd in _minimizedByUs)
            NativeMethods.ShowWindow(hwnd, NativeMethods.SW_RESTORE);
        _minimizedByUs.Clear();
    }
}