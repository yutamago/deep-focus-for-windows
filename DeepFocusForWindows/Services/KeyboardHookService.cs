using System;
using System.Runtime.InteropServices;
using DeepFocusForWindows.Native;

namespace DeepFocusForWindows.Services;

/// <summary>
/// Installs a low-level keyboard hook and raises <see cref="EscDoublePressed"/>
/// when ESC is pressed twice within the configured threshold.
/// Timing logic is delegated to <see cref="EscDoublePressDetector"/> for testability.
/// </summary>
public class KeyboardHookService : IKeyboardHookService
{
    private readonly EscDoublePressDetector _detector = new();

    private IntPtr _hookHandle = IntPtr.Zero;
    private NativeMethods.LowLevelKeyboardProc? _proc;
    private bool _disposed;

    public event EventHandler? EscDoublePressed;

    public void Start()
    {
        if (_hookHandle != IntPtr.Zero) return;

        _detector.DoublePressed += OnDoublePressed;
        _proc = HookCallback;
        var hMod = NativeMethods.GetModuleHandle(null);
        _hookHandle = NativeMethods.SetWindowsHookEx(NativeMethods.WH_KEYBOARD_LL, _proc, hMod, 0);
    }

    public void Stop()
    {
        if (_hookHandle == IntPtr.Zero) return;
        NativeMethods.UnhookWindowsHookEx(_hookHandle);
        _hookHandle = IntPtr.Zero;
        _detector.DoublePressed -= OnDoublePressed;
    }

    private void OnDoublePressed(object? sender, EventArgs e)
        => EscDoublePressed?.Invoke(this, EventArgs.Empty);

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && wParam == NativeMethods.WM_KEYDOWN)
        {
            var info = Marshal.PtrToStructure<NativeMethods.KBDLLHOOKSTRUCT>(lParam);
            if (info.vkCode == NativeMethods.VK_ESCAPE)
                _detector.RecordPress(info.time);
        }

        return NativeMethods.CallNextHookEx(_hookHandle, nCode, wParam, lParam);
    }

    public void Dispose()
    {
        if (_disposed) return;
        Stop();
        _disposed = true;
    }
}
