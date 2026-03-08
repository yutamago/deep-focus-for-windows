using System;
using System.Collections.Generic;
using System.Threading;
using Avalonia;
using Avalonia.Controls;
using DeepFocusForWindows.Native;

namespace DeepFocusForWindows.Views;

public partial class DimmingOverlayWindow : Window
{
    private readonly ISet<IntPtr> _excludedHandles;
    private int _dimmingLevel;
    private IntPtr _hwnd;
    private volatile IntPtr _configWindowHandle;
    private volatile bool _active;
    private CancellationTokenSource? _cts;

    // Parameterless constructor required by Avalonia XAML loader.
    public DimmingOverlayWindow() : this(new HashSet<IntPtr>(), 70) { }

    public DimmingOverlayWindow(ISet<IntPtr> excludedHandles, int dimmingLevel)
    {
        _excludedHandles = excludedHandles;
        _dimmingLevel    = dimmingLevel;

        InitializeComponent();
        PositionOnVirtualScreen();

        // Pause region updates while the overlay is hidden.
        this.PropertyChanged += (_, e) =>
        {
            if (e.Property == IsVisibleProperty)
                _active = IsVisible;
        };
    }

    public void SetConfigWindowHandle(IntPtr hwnd) => _configWindowHandle = hwnd;

    public void ApplyAlpha(int dimmingLevel)
    {
        _dimmingLevel = dimmingLevel;
        if (_hwnd != IntPtr.Zero)
            ApplyLayeredAttributes(_hwnd);
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);

        _hwnd = TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
        if (_hwnd == IntPtr.Zero) return;

        ApplyWin32Styles(_hwnd);
        ApplyLayeredAttributes(_hwnd);

        _active = true;
        _cts    = new CancellationTokenSource();
        var ct  = _cts.Token;

        // Run the region-update loop on a low-priority background thread so it
        // never competes with the Avalonia render loop, even at 144 Hz.
        new Thread(() => RegionLoop(ct))
        {
            IsBackground = true,
            Name         = "DimmingRegion",
            Priority     = ThreadPriority.BelowNormal,
        }.Start();
    }

    protected override void OnClosed(EventArgs e)
    {
        _cts?.Cancel();
        base.OnClosed(e);
    }

    // ── Background region loop ───────────────────────────────────────────────

    private void RegionLoop(CancellationToken ct)
    {
        var prevRects = new Dictionary<IntPtr, (int L, int T, int R, int B)>();
        int pvx = 0, pvy = 0, pvw = 0, pvh = 0;

        while (!ct.IsCancellationRequested)
        {
            Thread.Sleep(16);

            if (ct.IsCancellationRequested || !_active) continue;

            var hwnd = _hwnd; // written once in OnOpened before this thread starts
            if (hwnd == IntPtr.Zero) continue;

            // ── Gather current state ─────────────────────────────────────────
            int vx = NativeMethods.GetSystemMetrics(NativeMethods.SM_XVIRTUALSCREEN);
            int vy = NativeMethods.GetSystemMetrics(NativeMethods.SM_YVIRTUALSCREEN);
            int vw = NativeMethods.GetSystemMetrics(NativeMethods.SM_CXVIRTUALSCREEN);
            int vh = NativeMethods.GetSystemMetrics(NativeMethods.SM_CYVIRTUALSCREEN);

            // Brief lock just to snapshot the handle set; callers must also lock
            // when mutating ExcludedHandles to prevent torn reads.
            IntPtr[] handles;
            lock (_excludedHandles)
                handles = [.. _excludedHandles];

            var cfgHwnd  = _configWindowHandle;
            var curRects = new Dictionary<IntPtr, (int L, int T, int R, int B)>(handles.Length + 1);

            if (cfgHwnd != IntPtr.Zero
                && NativeMethods.IsWindowVisible(cfgHwnd)
                && !NativeMethods.IsIconic(cfgHwnd)
                && NativeMethods.GetWindowRect(cfgHwnd, out var cr)
                && cr.Width > 0 && cr.Height > 0)
            {
                curRects[cfgHwnd] = (cr.Left, cr.Top, cr.Right, cr.Bottom);
            }

            foreach (var h in handles)
            {
                if (!NativeMethods.IsWindowVisible(h) || NativeMethods.IsIconic(h)) continue;
                if (NativeMethods.GetWindowRect(h, out var r) && r.Width > 0 && r.Height > 0)
                    curRects[h] = (r.Left, r.Top, r.Right, r.Bottom);
            }

            // ── Skip if nothing changed ──────────────────────────────────────
            // This is the main perf win: when the user isn't moving windows,
            // SetWindowRgn is never called, so DWM gets zero extra work.
            if (vx == pvx && vy == pvy && vw == pvw && vh == pvh
                && RectDictionariesEqual(prevRects, curRects))
                continue;

            // ── Build and apply new region ───────────────────────────────────
            IntPtr region = NativeMethods.CreateRectRgn(vx, vy, vx + vw, vy + vh);
            foreach (var rect in curRects.Values)
            {
                IntPtr hole = NativeMethods.CreateRectRgn(rect.L, rect.T, rect.R, rect.B);
                NativeMethods.CombineRgn(region, region, hole, NativeMethods.RGN_DIFF);
                NativeMethods.DeleteObject(hole);
            }

            // bRedraw = false: DWM picks up the region change at its next
            // composition step; no explicit window repaint is needed for a
            // layered alpha overlay whose content never changes.
            // Safe to call from a non-owning thread when bRedraw is false
            // because no window messages are posted.
            NativeMethods.SetWindowRgn(hwnd, region, false);
            // OS now owns the region handle; do not DeleteObject.

            // ── Update caches ────────────────────────────────────────────────
            pvx = vx; pvy = vy; pvw = vw; pvh = vh;
            prevRects.Clear();
            foreach (var kvp in curRects)
                prevRects[kvp.Key] = kvp.Value;
        }
    }

    private static bool RectDictionariesEqual(
        Dictionary<IntPtr, (int L, int T, int R, int B)> a,
        Dictionary<IntPtr, (int L, int T, int R, int B)> b)
    {
        if (a.Count != b.Count) return false;
        foreach (var (k, v) in a)
            if (!b.TryGetValue(k, out var bv) || v != bv) return false;
        return true;
    }

    // ── Win32 helpers ────────────────────────────────────────────────────────

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

    private static void ApplyWin32Styles(IntPtr hwnd)
    {
        var current = NativeMethods.GetWindowLongPtr(hwnd, NativeMethods.GWL_EXSTYLE).ToInt64();
        current |= NativeMethods.WS_EX_LAYERED
                 | NativeMethods.WS_EX_TRANSPARENT
                 | NativeMethods.WS_EX_TOOLWINDOW
                 | NativeMethods.WS_EX_NOACTIVATE;
        NativeMethods.SetWindowLongPtr(hwnd, NativeMethods.GWL_EXSTYLE, new IntPtr(current));

        NativeMethods.SetWindowPos(hwnd, NativeMethods.HWND_TOPMOST,
            0, 0, 0, 0,
            NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOACTIVATE);
    }

    private void ApplyLayeredAttributes(IntPtr hwnd)
    {
        byte alpha = (byte)(_dimmingLevel / 100.0 * 255);
        NativeMethods.SetLayeredWindowAttributes(hwnd, 0, alpha, NativeMethods.LWA_ALPHA);
    }
}
