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

            // ── Gather virtual screen ────────────────────────────────────────
            int vx = NativeMethods.GetSystemMetrics(NativeMethods.SM_XVIRTUALSCREEN);
            int vy = NativeMethods.GetSystemMetrics(NativeMethods.SM_YVIRTUALSCREEN);
            int vw = NativeMethods.GetSystemMetrics(NativeMethods.SM_CXVIRTUALSCREEN);
            int vh = NativeMethods.GetSystemMetrics(NativeMethods.SM_CYVIRTUALSCREEN);

            // Brief lock just to snapshot the handle set.
            IntPtr[] handles;
            lock (_excludedHandles)
                handles = [.. _excludedHandles];

            var handlesSet = new HashSet<IntPtr>(handles);
            var cfgHwnd    = _configWindowHandle;

            // curRects tracks every HWND whose rect contributed to this frame
            // (focus windows + covering non-focus windows + config window).
            // Used for change detection: if this dict equals prevRects, skip SetWindowRgn.
            var curRects = new Dictionary<IntPtr, (int L, int T, int R, int B)>();

            // Per-focus-window list of covering rects to subtract.
            var focusCoverMap =
                new Dictionary<IntPtr, List<(int L, int T, int R, int B)>>();

            foreach (var h in handles)
            {
                if (!NativeMethods.IsWindowVisible(h) || NativeMethods.IsIconic(h)) continue;
                if (!NativeMethods.GetWindowRect(h, out var r) || r.Width <= 0 || r.Height <= 0) continue;

                curRects[h] = (r.Left, r.Top, r.Right, r.Bottom);

                // Walk windows above h in Z-order; collect non-focus windows
                // that overlap h's rect so we can subtract them from the hole.
                var covers = new List<(int L, int T, int R, int B)>();
                var above  = NativeMethods.GetWindow(h, NativeMethods.GW_HWNDPREV);
                while (above != IntPtr.Zero)
                {
                    if (above != hwnd                              // skip our own overlay
                        && !handlesSet.Contains(above)            // skip other focus windows
                        && NativeMethods.IsWindowVisible(above)
                        && !NativeMethods.IsIconic(above)
                        && NativeMethods.GetWindowRect(above, out var ar)
                        && ar.Width > 0 && ar.Height > 0
                        && ar.Right > r.Left && ar.Left < r.Right // overlaps focus rect
                        && ar.Bottom > r.Top && ar.Top < r.Bottom)
                    {
                        covers.Add((ar.Left, ar.Top, ar.Right, ar.Bottom));
                        // Track for change detection (don't overwrite a focus-window entry).
                        if (!curRects.ContainsKey(above))
                            curRects[above] = (ar.Left, ar.Top, ar.Right, ar.Bottom);
                    }
                    above = NativeMethods.GetWindow(above, NativeMethods.GW_HWNDPREV);
                }

                focusCoverMap[h] = covers;
            }

            // Config window: always-excluded, no Z-order check needed.
            NativeMethods.RECT cfgR = default;
            bool cfgVisible = cfgHwnd != IntPtr.Zero
                && NativeMethods.IsWindowVisible(cfgHwnd)
                && !NativeMethods.IsIconic(cfgHwnd)
                && NativeMethods.GetWindowRect(cfgHwnd, out cfgR)
                && cfgR.Width > 0 && cfgR.Height > 0;
            if (cfgVisible)
                curRects[cfgHwnd] = (cfgR.Left, cfgR.Top, cfgR.Right, cfgR.Bottom);

            // ── Skip if nothing changed ──────────────────────────────────────
            if (vx == pvx && vy == pvy && vw == pvw && vh == pvh
                && RectDictionariesEqual(prevRects, curRects))
                continue;

            // ── Build and apply new region ───────────────────────────────────
            IntPtr region = NativeMethods.CreateRectRgn(vx, vy, vx + vw, vy + vh);

            // Config window: simple full-rect hole.
            if (cfgVisible)
            {
                IntPtr hole = NativeMethods.CreateRectRgn(cfgR.Left, cfgR.Top, cfgR.Right, cfgR.Bottom);
                NativeMethods.CombineRgn(region, region, hole, NativeMethods.RGN_DIFF);
                NativeMethods.DeleteObject(hole);
            }

            // Focus windows: carve only the visible portion
            // (full rect minus all covering non-focus windows above them).
            foreach (var (h, covers) in focusCoverMap)
            {
                var (fL, fT, fR, fB) = curRects[h];
                IntPtr focusRgn = NativeMethods.CreateRectRgn(fL, fT, fR, fB);

                foreach (var (cL, cT, cR, cB) in covers)
                {
                    IntPtr coverRgn = NativeMethods.CreateRectRgn(cL, cT, cR, cB);
                    NativeMethods.CombineRgn(focusRgn, focusRgn, coverRgn, NativeMethods.RGN_DIFF);
                    NativeMethods.DeleteObject(coverRgn);
                }

                NativeMethods.CombineRgn(region, region, focusRgn, NativeMethods.RGN_DIFF);
                NativeMethods.DeleteObject(focusRgn);
            }

            // bRedraw = false: DWM picks up the region change at its next
            // composition step; safe from a non-owning thread.
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
