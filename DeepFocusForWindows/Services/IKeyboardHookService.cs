using System;

namespace DeepFocusForWindows.Services;

public interface IKeyboardHookService : IDisposable
{
    /// <summary>Fired when ESC is pressed twice within the configured threshold.</summary>
    event EventHandler? EscDoublePressed;
    void Start();
    void Stop();
}
