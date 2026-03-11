using System.Collections.Generic;

namespace DeepFocusForWindows.Models;

public class AppSettings
{
    public bool IsFirstRun { get; set; } = true;
    public bool StartOnBoot { get; set; } = true;
    public int DimmingLevel { get; set; } = 70;
    public bool DimTaskbar { get; set; } = true;
    public bool MinimizeNonFocusWindows { get; set; } = true;
    /// <summary>Windows selected for focus exclusion, matched by (ProcessName + Title).</summary>
    public List<FocusAppEntry> FocusApps { get; set; } = [];
}
