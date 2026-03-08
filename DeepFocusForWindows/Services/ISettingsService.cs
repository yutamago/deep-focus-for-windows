using System.Threading.Tasks;
using DeepFocusForWindows.Models;

namespace DeepFocusForWindows.Services;

public interface ISettingsService
{
    AppSettings Settings { get; }
    Task LoadAsync();
    Task SaveAsync();
}
