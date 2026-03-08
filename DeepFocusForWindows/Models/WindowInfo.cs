using System;

namespace DeepFocusForWindows.Models;

public class WindowInfo
{
    public IntPtr Handle { get; init; }
    public string Title { get; init; } = string.Empty;
    public string ProcessName { get; init; } = string.Empty;
    public bool IsSelected { get; set; }
}
