using System;
using Avalonia.Controls;
using DeepFocusForWindows.Services;
using DeepFocusForWindows.ViewModels;

namespace DeepFocusForWindows.Views;

public partial class ConfigurationWindow : Window
{
    public ConfigurationWindow()
    {
        InitializeComponent();
    }

    public ConfigurationWindow(ConfigurationViewModel viewModel, IDimmingService dimmingService) : this()
    {
        DataContext = viewModel;
        viewModel.CloseRequested += (_, _) => Close();

        // Register this window's HWND with both the dimming service (overlay exclusion)
        // and the view-model (window-picker exclusion).
        Opened += (_, _) =>
        {
            var hwnd = TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
            if (hwnd != IntPtr.Zero)
            {
                dimmingService.SetConfigWindowHandle(hwnd);
                viewModel.SetConfigWindowHwnd(hwnd);
            }
        };

        // Stop any active preview / window-picker before the window is destroyed.
        Closing += (_, _) => viewModel.OnWindowClosing();

        Closed += (_, _) => dimmingService.SetConfigWindowHandle(IntPtr.Zero);
    }
}
