using System.Collections.Generic;
using DeepFocusForWindows.Models;

namespace DeepFocusForWindows.Services;

public interface IWindowEnumerationService
{
    IReadOnlyList<WindowInfo> GetVisibleWindows();
}
