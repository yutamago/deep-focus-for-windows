using System;

namespace DeepFocusForWindows.Services;

public interface IFocusSessionService
{
    bool IsSupported { get; }
    bool IsFocusActive { get; }
    event EventHandler? FocusActiveChanged;
    void Start();
    void Stop();
}
