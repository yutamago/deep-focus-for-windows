using System;
using System.Collections.Generic;
using Avalonia.Threading;
using DeepFocusForWindows.Views;

namespace DeepFocusForWindows.Services;

/// <summary>
/// Controls the full-screen dimming overlay.
/// All public methods that manipulate the overlay must be called on the UI thread.
/// </summary>
public class DimmingService : IDimmingService
{
    private DimmingOverlayWindow? _overlay;
    private IntPtr _configWindowHandle;
    private int _dimmingLevel = 70;
    private bool _isActive;
    private bool _isTemporarilyDisabled;

    public bool IsActive              => _isActive;
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

    public ISet<IntPtr> ExcludedHandles { get; } = new HashSet<IntPtr>();

    public event EventHandler? StateChanged;

    public void SetConfigWindowHandle(IntPtr hwnd)
    {
        _configWindowHandle = hwnd;
        _overlay?.SetConfigWindowHandle(hwnd);
    }

    public void Enable()
    {
        Dispatcher.UIThread.VerifyAccess();
        _isTemporarilyDisabled = false;
        _isActive = true;
        ShowOverlay();
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Disable()
    {
        Dispatcher.UIThread.VerifyAccess();
        _isActive = false;
        _isTemporarilyDisabled = false;
        HideOverlay();
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    public void TemporarilyDisable()
    {
        Dispatcher.UIThread.VerifyAccess();
        if (!_isActive) return;
        _isTemporarilyDisabled = true;
        HideOverlay();
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    public void RestoreFromTemporaryDisable()
    {
        Dispatcher.UIThread.VerifyAccess();
        if (!_isActive || !_isTemporarilyDisabled) return;
        _isTemporarilyDisabled = false;
        ShowOverlay();
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    private void ShowOverlay()
    {
        if (_overlay is null)
        {
            _overlay = new DimmingOverlayWindow(ExcludedHandles, _dimmingLevel);
            _overlay.SetConfigWindowHandle(_configWindowHandle);
        }
        _overlay.Show();
    }

    private void HideOverlay()
    {
        _overlay?.Hide();
    }
}
