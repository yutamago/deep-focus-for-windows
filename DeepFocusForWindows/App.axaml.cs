using System;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Data.Core.Plugins;
using Avalonia.Markup.Xaml;
using Avalonia.Platform;
using Avalonia.Threading;
using DeepFocusForWindows.Native;
using DeepFocusForWindows.Services;
using DeepFocusForWindows.ViewModels;
using DeepFocusForWindows.Views;
using Microsoft.Extensions.DependencyInjection;

namespace DeepFocusForWindows;

public partial class App : Application
{
    private const string AppAumid = "DeepFocusForWindows.App";

    private IServiceProvider?    _services;
    private TrayIcon?            _trayIcon;
    private ConfigurationWindow? _configWindow;

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            LogError("UnhandledException", e.ExceptionObject as Exception);

        // Register AUMID so Windows toast notifications work for unpackaged apps.
        NativeMethods.SetCurrentProcessExplicitAppUserModelID(AppAumid);

        DisableAvaloniaDataAnnotationValidation();
        _services = BuildServices();

        bool isFirstRun = false;

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;
            desktop.Exit += OnExit;

            var settings = _services.GetRequiredService<ISettingsService>();
            settings.LoadAsync().GetAwaiter().GetResult();

            isFirstRun = settings.Settings.IsFirstRun;
            if (isFirstRun)
            {
                _services.GetRequiredService<IStartupService>()
                    .SetStartOnBoot(settings.Settings.StartOnBoot);
                settings.Settings.IsFirstRun = false;
                settings.SaveAsync().GetAwaiter().GetResult();
            }

            // Start background services.
            _services.GetRequiredService<IFocusSessionService>().Start();
            SetupFocusSessionAutoDim();
            _services.GetRequiredService<IKeyboardHookService>().Start();
            WireEscDoublePress();

            // Restore saved focus-app handles (best-effort match by ProcessName+Title).
            var dimming    = _services.GetRequiredService<IDimmingService>();
            var windowEnum = _services.GetRequiredService<IWindowEnumerationService>();
            dimming.DimmingLevel = settings.Settings.DimmingLevel;
            dimming.DimTaskbar   = settings.Settings.DimTaskbar;
            var saved = settings.Settings.FocusApps;
            lock (dimming.ExcludedHandles)
            {
                foreach (var w in windowEnum.GetVisibleWindows())
                {
                    if (saved.Any(e =>
                            string.Equals(e.ProcessName, w.ProcessName, StringComparison.OrdinalIgnoreCase)
                            && string.Equals(e.Title, w.Title, StringComparison.OrdinalIgnoreCase)))
                    {
                        dimming.ExcludedHandles.Add(w.Handle);
                    }
                }
            }

            // Show a toast whenever dimming state changes.
            dimming.StateChanged += (_, _) =>
                Dispatcher.UIThread.Post(() => ShowDimmingToast(dimming));
        }

        // Call base FIRST so Avalonia's TrayIconManager and message-loop infrastructure
        // are fully initialised before we create any OS-level resources.
        base.OnFrameworkInitializationCompleted();

        // Defer tray + config window to the first dispatcher tick so the
        // Win32 message pump is already running when we create the OS tray icon.
        Dispatcher.UIThread.Post(() =>
        {
            SetupTrayIcon();
            // Only show the config window automatically on first run;
            // afterwards the user opens it via the tray icon.
            if (isFirstRun) ShowConfigurationWindow();
        });
    }

    // ── DI ───────────────────────────────────────────────────────────────────

    private static IServiceProvider BuildServices()
    {
        var sc = new ServiceCollection();
        sc.AddSingleton<ISettingsService,          SettingsService>();
        sc.AddSingleton<IWindowEnumerationService, WindowEnumerationService>();
        sc.AddSingleton<IDimmingService,           DimmingService>();
        sc.AddSingleton<IStartupService,           StartupService>();
        sc.AddSingleton<IFocusSessionService,      FocusSessionService>();
        sc.AddSingleton<IKeyboardHookService,      KeyboardHookService>();
        sc.AddTransient<ConfigurationViewModel>();
        sc.AddSingleton<TrayIconViewModel>();
        return sc.BuildServiceProvider();
    }

    // ── Focus Session auto-dim ────────────────────────────────────────────────

    private void SetupFocusSessionAutoDim()
    {
        var focus   = _services!.GetRequiredService<IFocusSessionService>();
        var dimming = _services!.GetRequiredService<IDimmingService>();

        focus.FocusActiveChanged += (_, _) =>
            Dispatcher.UIThread.Post(() =>
            {
                if (focus.IsFocusActive) dimming.Enable();
                else                     dimming.Disable();
            });
    }

    // ── ESC double-press ──────────────────────────────────────────────────────

    private void WireEscDoublePress()
    {
        var kbd     = _services!.GetRequiredService<IKeyboardHookService>();
        var dimming = _services!.GetRequiredService<IDimmingService>();

        kbd.EscDoublePressed += (_, _) =>
            Dispatcher.UIThread.Post(() =>
            {
                if (dimming.IsTemporarilyDisabled) dimming.RestoreFromTemporaryDisable();
                else                               dimming.TemporarilyDisable();
            });
    }

    // ── Tray icon ─────────────────────────────────────────────────────────────

    private void SetupTrayIcon()
    {
        try
        {
            var dimming = _services!.GetRequiredService<IDimmingService>();

            var statusItem   = new NativeMenuItem { Header = GetStatusHeader(dimming), IsEnabled = false };
            var settingsItem = new NativeMenuItem { Header = "Open Settings" };
            var exitItem     = new NativeMenuItem { Header = "Exit" };

            dimming.StateChanged += (_, _) =>
                statusItem.Header = GetStatusHeader(dimming);

            settingsItem.Click += (_, _) =>
                Dispatcher.UIThread.Post(ShowConfigurationWindow);

            exitItem.Click += (_, _) =>
            {
                if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime d)
                    d.Shutdown();
            };

            var menu = new NativeMenu();
            menu.Items.Add(statusItem);
            menu.Items.Add(new NativeMenuItemSeparator());
            menu.Items.Add(settingsItem);
            menu.Items.Add(new NativeMenuItemSeparator());
            menu.Items.Add(exitItem);

            _trayIcon = new TrayIcon
            {
                ToolTipText = "DeepFocus",
                Icon        = new WindowIcon(
                    AssetLoader.Open(new Uri("avares://DeepFocusForWindows/Assets/avalonia-logo.ico"))),
                Menu      = menu,
                IsVisible = true
            };

            // Double-click the tray icon to open settings directly.
            _trayIcon.Clicked += (_, _) => ShowConfigurationWindow();

            // Explicit instantiation — collection expressions are unreliable for TrayIcons.
            TrayIcon.SetIcons(this, new TrayIcons { _trayIcon });
        }
        catch (Exception ex)
        {
            LogError("SetupTrayIcon", ex);
        }
    }

    private static string GetStatusHeader(IDimmingService d)
        => d.IsActive ? "● Dimming active" : "○ Dimming inactive";

    private void ShowDimmingToast(IDimmingService dimming)
    {
        string message = dimming.IsTemporarilyDisabled
            ? "Dimming paused — press ESC twice to restore"
            : dimming.IsActive
                ? "Dimming enabled"
                : "Dimming disabled";
        ShowToast(message);
    }

    private static void ShowToast(string message)
    {
        try
        {
            var xml = new Windows.Data.Xml.Dom.XmlDocument();
            xml.LoadXml(
                $"<toast duration=\"short\"><visual><binding template=\"ToastGeneric\">" +
                $"<text>DeepFocus</text>" +
                $"<text>{System.Security.SecurityElement.Escape(message)}</text>" +
                $"</binding></visual></toast>");
            var toast = new Windows.UI.Notifications.ToastNotification(xml);
            Windows.UI.Notifications.ToastNotificationManager
                .CreateToastNotifier(AppAumid).Show(toast);
        }
        catch { /* Silently ignore notification failures */ }
    }

    // ── Configuration window ──────────────────────────────────────────────────

    private void ShowConfigurationWindow()
    {
        if (_configWindow is { IsVisible: true })
        {
            _configWindow.Activate();
            return;
        }

        var vm = _services!.GetRequiredService<ConfigurationViewModel>();
        _configWindow = new ConfigurationWindow(
            vm,
            _services!.GetRequiredService<IDimmingService>());
        _configWindow.Closed += (_, _) => _configWindow = null;
        _configWindow.Show();
    }

    // ── Cleanup ───────────────────────────────────────────────────────────────

    private void OnExit(object? sender, ControlledApplicationLifetimeExitEventArgs e)
    {
        _services?.GetRequiredService<IKeyboardHookService>().Dispose();
        _services?.GetRequiredService<IFocusSessionService>().Stop();
        // Ensure all windows minimized by us are restored before exit.
        _services?.GetRequiredService<IDimmingService>().ForceDisableAll();
        _trayIcon?.Dispose();
    }

    private static void DisableAvaloniaDataAnnotationValidation()
    {
        var toRemove = BindingPlugins.DataValidators
            .OfType<DataAnnotationsValidationPlugin>().ToArray();
        foreach (var p in toRemove)
            BindingPlugins.DataValidators.Remove(p);
    }

    private static void LogError(string context, Exception? ex)
    {
        try
        {
            var log = Path.Combine(Path.GetTempPath(), "deepfocus_error.log");
            File.AppendAllText(log,
                $"[{DateTime.Now:u}] {context}: {ex}{Environment.NewLine}");
        }
        catch { /* swallow logging failures */ }
    }
}
