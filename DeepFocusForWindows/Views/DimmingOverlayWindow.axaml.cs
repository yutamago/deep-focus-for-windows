using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using DeepFocusForWindows.Native;

namespace DeepFocusForWindows.Views;

public partial class DimmingOverlayWindow : Window
{
    private readonly ISet<IntPtr> _excludedHandles;
    private int _dimmingLevel;
    private IntPtr _hwnd;
    private IntPtr _configWindowHandle;
    private DispatcherTimer? _regionTimer;

    // Parameterless constructor required by Avalonia XAML loader.
    public DimmingOverlayWindow() : this(new HashSet<IntPtr>(), 70) { }

    public DimmingOverlayWindow(ISet<IntPtr> excludedHandles, int dimmingLevel)
    {
        _excludedHandles = excludedHandles;
        _dimmingLevel    = dimmingLevel;

        InitializeComponent();
        PositionOnVirtualScreen();
    }

    public void SetConfigWindowHandle(IntPtr hwnd) => _configWindowHandle = hwnd;

    public void ApplyAlpha(int dimmingLevel)
    {
        _dimmingLevel = dimmingLevel;
        if (_hwnd != IntPtr.Zero)
            ApplyLayeredAttributes();
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);

        _hwnd = TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
        if (_hwnd == IntPtr.Zero) return;

        ApplyWin32Styles();
        ApplyLayeredAttributes();

        // 16 ms ≈ 60 fps — cheap because UpdateRegion only touches the known
        // excluded HWNDs (no EnumWindows, no process-name lookups).
        _regionTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
        _regionTimer.Tick += (_, _) => UpdateRegion();
        _regionTimer.Start();
    }

    protected override void OnClosed(EventArgs e)
    {
        _regionTimer?.Stop();
        base.OnClosed(e);
    }

    // ── Win32 helpers ───────────────────────────────────────────────────────

    private void PositionOnVirtualScreen()
    {
        int vx = NativeMethods.GetSystemMetrics(NativeMethods.SM_XVIRTUALSCREEN);
        int vy = NativeMethods.GetSystemMetrics(NativeMethods.SM_YVIRTUALSCREEN);
        int vw = NativeMethods.GetSystemMetrics(NativeMethods.SM_CXVIRTUALSCREEN);
        int vh = NativeMethods.GetSystemMetrics(NativeMethods.SM_CYVIRTUALSCREEN);

        Position = new PixelPoint(vx, vy);
        Width    = vw;
        Height   = vh;
    }

    private void ApplyWin32Styles()
    {
        var current = NativeMethods.GetWindowLongPtr(_hwnd, NativeMethods.GWL_EXSTYLE).ToInt64();
        current |= NativeMethods.WS_EX_LAYERED
                 | NativeMethods.WS_EX_TRANSPARENT
                 | NativeMethods.WS_EX_TOOLWINDOW
                 | NativeMethods.WS_EX_NOACTIVATE;
        NativeMethods.SetWindowLongPtr(_hwnd, NativeMethods.GWL_EXSTYLE, new IntPtr(current));

        // Ensure it sits above all normal windows.
        NativeMethods.SetWindowPos(_hwnd, NativeMethods.HWND_TOPMOST,
            0, 0, 0, 0,
            NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOACTIVATE);
    }

    private void ApplyLayeredAttributes()
    {
        byte alpha = (byte)(_dimmingLevel / 100.0 * 255);
        NativeMethods.SetLayeredWindowAttributes(_hwnd, 0, alpha, NativeMethods.LWA_ALPHA);
    }

    private void UpdateRegion()
    {
        if (_hwnd == IntPtr.Zero) return;

        int vx = NativeMethods.GetSystemMetrics(NativeMethods.SM_XVIRTUALSCREEN);
        int vy = NativeMethods.GetSystemMetrics(NativeMethods.SM_YVIRTUALSCREEN);
        int vw = NativeMethods.GetSystemMetrics(NativeMethods.SM_CXVIRTUALSCREEN);
        int vh = NativeMethods.GetSystemMetrics(NativeMethods.SM_CYVIRTUALSCREEN);

        IntPtr region = NativeMethods.CreateRectRgn(vx, vy, vx + vw, vy + vh);

        // Always carve out the config window.
        if (_configWindowHandle != IntPtr.Zero
            && NativeMethods.IsWindowVisible(_configWindowHandle)
            && !NativeMethods.IsIconic(_configWindowHandle))
        {
            SubtractWindowFromRegion(region, _configWindowHandle);
        }

        // Carve out each individually selected window.
        // Because we track HWNDs (not process names), only the exact selected
        // window is revealed — sibling windows of the same process stay dimmed.
        foreach (var hWnd in _excludedHandles)
        {
            if (!NativeMethods.IsWindowVisible(hWnd)) continue;
            if (NativeMethods.IsIconic(hWnd))         continue; // minimized
            SubtractWindowFromRegion(region, hWnd);
        }

        NativeMethods.SetWindowRgn(_hwnd, region, true);
        // region ownership is transferred to the OS; do not call DeleteObject.
    }

    private static void SubtractWindowFromRegion(IntPtr region, IntPtr hWnd)
    {
        if (!NativeMethods.GetWindowRect(hWnd, out var rect)) return;
        if (rect.Width <= 0 || rect.Height <= 0) return;

        IntPtr hole = NativeMethods.CreateRectRgn(rect.Left, rect.Top, rect.Right, rect.Bottom);
        NativeMethods.CombineRgn(region, region, hole, NativeMethods.RGN_DIFF);
        NativeMethods.DeleteObject(hole);
    }
}
