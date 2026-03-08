using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using DeepFocusForWindows.Models;
using DeepFocusForWindows.Native;

namespace DeepFocusForWindows.Services;

public class WindowEnumerationService : IWindowEnumerationService
{
    public IReadOnlyList<WindowInfo> GetVisibleWindows()
    {
        var windows = new List<WindowInfo>();

        NativeMethods.EnumWindows((hWnd, _) =>
        {
            if (!NativeMethods.IsWindowVisible(hWnd))
                return true;

            var sb = new StringBuilder(256);
            NativeMethods.GetWindowText(hWnd, sb, sb.Capacity);
            var title = sb.ToString();

            if (string.IsNullOrWhiteSpace(title))
                return true;

            NativeMethods.GetWindowThreadProcessId(hWnd, out uint pid);
            string processName;
            try { processName = Process.GetProcessById((int)pid).ProcessName; }
            catch { processName = string.Empty; }

            windows.Add(new WindowInfo
            {
                Handle      = hWnd,
                Title       = title,
                ProcessName = processName
            });
            return true;
        }, IntPtr.Zero);

        return windows;
    }
}
