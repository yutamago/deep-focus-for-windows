using System;
using System.Collections.Generic;

namespace DeepFocusForWindows.Services;

public interface IDimmingService
{
    bool IsActive { get; }
    bool IsTemporarilyDisabled { get; }
    int DimmingLevel { get; set; }
    /// <summary>When true (default) the taskbar is covered by the overlay. When false it is revealed.</summary>
    bool DimTaskbar { get; set; }
    /// <summary>
    /// The exact window handles to keep visible through the overlay.
    /// Tracks individual windows rather than whole processes, so that wrapper
    /// executables (e.g. Electron, java, python) don't cause sibling windows
    /// to bleed through.
    /// </summary>
    ISet<IntPtr> ExcludedHandles { get; }

    /// <summary>Must be called on the UI thread.</summary>
    void Enable(bool minimizeNonFocusWindows = true, bool useFadeTransition = false);

    /// <summary>Must be called on the UI thread.</summary>
    void Disable(bool restoreNonFocusWindows = true, bool useFadeTransition = false);

    /// <summary>Temporarily hide the overlay (ESC×2). Must be called on the UI thread.</summary>
    void TemporarilyDisable();

    /// <summary>Re-show the overlay if it was temporarily disabled. UI thread.</summary>
    void RestoreFromTemporaryDisable();

    /// <summary>Force-clear all enable counts and restore minimized windows. Call on app exit.</summary>
    void ForceDisableAll();

    /// <summary>Register the config window handle so it is always excluded from dimming.</summary>
    void SetConfigWindowHandle(IntPtr hwnd);

    event EventHandler? StateChanged;
}
