namespace DeepFocusForWindows.Models;

/// <summary>
/// Identifies a specific window for focus exclusion.
/// Stored by (ProcessName + Title) so the match survives restarts.
/// </summary>
public class FocusAppEntry
{
    public string ProcessName { get; set; } = string.Empty;
    public string Title       { get; set; } = string.Empty;
}
