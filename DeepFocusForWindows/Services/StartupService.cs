using System.Diagnostics;
using Microsoft.Win32;

namespace DeepFocusForWindows.Services;

public class StartupService : IStartupService
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

    protected virtual string AppName => "DeepFocus";

    public bool IsStartOnBootEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: false);
        return key?.GetValue(AppName) is not null;
    }

    public void SetStartOnBoot(bool enable)
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true)
                        ?? Registry.CurrentUser.CreateSubKey(RunKey);

        if (enable)
        {
            var exePath = Process.GetCurrentProcess().MainModule?.FileName ?? string.Empty;
            key.SetValue(AppName, $"\"{exePath}\"");
        }
        else
        {
            key.DeleteValue(AppName, throwOnMissingValue: false);
        }
    }
}
