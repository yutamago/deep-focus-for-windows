# DeepFocus for Windows

A lightweight, performance-focused screen dimming application for Windows that helps you stay focused on your work by selectively highlighting specific windows while dimming everything else.

## 🚀 Key Features

- **Selective Window Highlighting**: Choose which applications should remain bright. The rest of your screen will be dimmed to your preferred level.
- **Real-Time Tracking**: The dimming "holes" automatically follow your windows as you move, resize, or switch between them.
- **Windows 11 Focus Integration**: Automatically enable dimming when you start a Windows 11 Focus Session.
- **High Performance**: Uses low-level Win32 Region and Layered Window APIs to ensure minimal CPU and GPU impact, even on high-refresh-rate displays.
- **System Tray Integration**: Quickly toggle dimming or open configuration from the system tray.
- **Customizable Dimming**: Fine-tune the dimming intensity from 0% to 100%.

## 🛠️ How It Works

DeepFocus is built using **Avalonia** and **.NET 10**, leveraging deep Windows integration:

1.  **Window Dimming**: Instead of a simple semi-transparent overlay, DeepFocus creates a Win32 "Layered Window" that covers the entire virtual screen.
2.  **Dynamic Regions**: It uses the `SetWindowRgn` API to "cut holes" into the overlay. This means the OS handles the transparency efficiently, and clicks "pass through" the holes to the actual windows underneath.
3.  **Background Optimization**: A low-priority background thread monitors window positions and updates the region only when changes occur, preventing unnecessary redraws and saving battery life.
4.  **WinRT Integration**: It monitors the `FocusSessionManager` via WinRT to seamlessly integrate with built-in Windows 11 focus features.

## 📋 Requirements

- **OS**: Windows 11 (recommended for Focus Session integration) or Windows 10.
- **Runtime**: .NET 10.0.

## 🔨 Getting Started

### Building from Source

1.  Clone the repository:
    ```bash
    git clone https://github.com/yourusername/DeepFocusForWindows.git
    cd DeepFocusForWindows
    ```
2.  Build the solution:
    ```bash
    dotnet build DeepFocusForWindows.sln
    ```
3.  Run the application:
    ```bash
    dotnet run --project DeepFocusForWindows\DeepFocusForWindows.csproj
    ```

### Running Tests

To run the unit tests:
```bash
dotnet test DeepFocusForWindows.Tests\DeepFocusForWindows.Tests.csproj
```

## 📖 Usage

1.  **Launch the App**: Once started, look for the DeepFocus icon in your system tray.
2.  **Configure Apps**: Open the **Configuration** window to see a list of all visible windows. Check the boxes next to the apps you want to keep highlighted.
3.  **Set Dimming Level**: Use the slider to adjust how much the background should be dimmed.
4.  **Auto-Dim**: Enable "Auto-Dim on Focus Session" to have DeepFocus automatically activate whenever you start a focus session in Windows 11.
5.  **Apply Changes**: Click "Apply" to save your settings. The overlay will update immediately.

## 🔧 Troubleshooting

- **Admin Rights**: Some windows (like Task Manager) may require DeepFocus to run with administrator privileges to be correctly identified and "cut out" of the dimming overlay.
- **Multi-Monitor Support**: DeepFocus supports multiple monitors by default, covering the entire virtual screen area.

## 📝 License

This project is currently provided "as is" without a formal license. (Update this section if a license is added).
