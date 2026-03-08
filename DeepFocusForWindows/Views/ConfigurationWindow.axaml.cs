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

        // Register this window's handle with the dimming service so it is
        // always excluded from the dim overlay.
        Opened += (_, _) =>
        {
            var hwnd = TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
            if (hwnd != IntPtr.Zero)
                dimmingService.SetConfigWindowHandle(hwnd);
        };

        Closed += (_, _) =>
            dimmingService.SetConfigWindowHandle(IntPtr.Zero);
    }
}
