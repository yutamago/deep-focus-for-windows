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
using DeepFocusForWindows.Services;
using DeepFocusForWindows.ViewModels;
using DeepFocusForWindows.Views;
using Microsoft.Extensions.DependencyInjection;

namespace DeepFocusForWindows;

public partial class App : Application
{
    private IServiceProvider?    _services;
    private TrayIcon?            _trayIcon;
    private ConfigurationWindow? _configWindow;

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            LogError("UnhandledException", e.ExceptionObject as Exception);

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

            // Restore dimming from persisted settings.
            // We track HWNDs at runtime; on startup we re-match saved (ProcessName, Title)
            // pairs against currently visible windows as a best-effort.
            var dimming    = _services.GetRequiredService<IDimmingService>();
            var windowEnum = _services.GetRequiredService<IWindowEnumerationService>();
            dimming.DimmingLevel = settings.Settings.DimmingLevel;
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
            if (settings.Settings.IsDimmingEnabled)
                dimming.Enable();
        }

        // Call base FIRST so Avalonia's TrayIconManager and message-loop infrastructure
        // are fully initialised before we create any OS-level resources.
        base.OnFrameworkInitializationCompleted();

        // Defer tray + config window to the first dispatcher tick so the
        // Win32 message pump is already running when we create the OS tray icon.
        Dispatcher.UIThread.Post(() =>
        {
            SetupTrayIcon();
            ShowConfigurationWindow();
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
        var set     = _services!.GetRequiredService<ISettingsService>();

        focus.FocusActiveChanged += (_, _) =>
        {
            if (!set.Settings.AutoDimOnFocusSession) return;
            Dispatcher.UIThread.Post(() =>
            {
                if (focus.IsFocusActive) dimming.Enable();
                else                     dimming.Disable();
            });
        };
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

            var toggleItem   = new NativeMenuItem { Header = GetToggleHeader(dimming) };
            var settingsItem = new NativeMenuItem { Header = "Open Settings" };
            var exitItem     = new NativeMenuItem { Header = "Exit" };

            dimming.StateChanged += (_, _) =>
                toggleItem.Header = GetToggleHeader(dimming);

            toggleItem.Click += (_, _) => Dispatcher.UIThread.Post(() =>
            {
                if (dimming.IsActive) dimming.Disable();
                else                  dimming.Enable();
            });

            settingsItem.Click += (_, _) =>
                Dispatcher.UIThread.Post(ShowConfigurationWindow);

            exitItem.Click += (_, _) =>
            {
                if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime d)
                    d.Shutdown();
            };

            var menu = new NativeMenu();
            menu.Items.Add(toggleItem);
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

    private static string GetToggleHeader(IDimmingService d)
        => (d.IsActive && !d.IsTemporarilyDisabled) ? "Disable Dimming" : "Enable Dimming";

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
