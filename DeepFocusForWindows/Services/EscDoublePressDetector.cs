using System;

namespace DeepFocusForWindows.Services;

/// <summary>
/// Pure timing logic for detecting ESC double-presses.
/// Separated from the Win32 hook so it can be unit-tested without P/Invoke.
/// </summary>
public sealed class EscDoublePressDetector(int thresholdMs = 500)
{
    private uint _lastPressTime;

    public event EventHandler? DoublePressed;

    /// <summary>
    /// Call this each time the ESC key is pressed.
    /// <paramref name="time"/> is typically the kernel-reported timestamp (milliseconds).
    /// </summary>
    public void RecordPress(uint time)
    {
        if (_lastPressTime != 0 && time - _lastPressTime <= (uint)thresholdMs)
        {
            _lastPressTime = 0; // reset so a 3rd press starts a new sequence
            DoublePressed?.Invoke(this, EventArgs.Empty);
        }
        else
        {
            _lastPressTime = time;
        }
    }
}
