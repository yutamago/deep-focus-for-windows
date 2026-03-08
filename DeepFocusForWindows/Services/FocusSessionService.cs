using System;
using Windows.UI.Shell;

namespace DeepFocusForWindows.Services;

/// <summary>
/// Monitors the Windows Focus Session via the WinRT FocusSessionManager API.
/// Requires Windows 11 22H2+ with the feature available.
/// </summary>
public class FocusSessionService : IFocusSessionService
{
    private FocusSessionManager? _manager;

    public bool IsSupported { get; private set; }
    public bool IsFocusActive => _manager?.IsFocusActive ?? false;

    public event EventHandler? FocusActiveChanged;

    public void Start()
    {
        try
        {
            IsSupported = FocusSessionManager.IsSupported;
            if (!IsSupported) return;

            _manager = FocusSessionManager.GetDefault();
            _manager.IsFocusActiveChanged += OnFocusActiveChanged;
        }
        catch
        {
            IsSupported = false;
        }
    }

    public void Stop()
    {
        if (_manager is not null)
        {
            _manager.IsFocusActiveChanged -= OnFocusActiveChanged;
            _manager = null;
        }
    }

    private void OnFocusActiveChanged(FocusSessionManager sender, object args)
        => FocusActiveChanged?.Invoke(this, EventArgs.Empty);
}
