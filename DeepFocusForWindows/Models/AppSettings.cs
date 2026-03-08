using System.Collections.Generic;

namespace DeepFocusForWindows.Models;

public class AppSettings
{
    public bool IsFirstRun { get; set; } = true;
    public bool StartOnBoot { get; set; } = true;
    public bool IsDimmingEnabled { get; set; } = false;
    public int DimmingLevel { get; set; } = 70;
    /// <summary>Windows selected for focus exclusion, matched by (ProcessName + Title).</summary>
    public List<FocusAppEntry> FocusApps { get; set; } = [];
    public bool AutoDimOnFocusSession { get; set; } = false;
}
