using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using Avalonia.Logging;
using DeepFocusForWindows.Models;

namespace DeepFocusForWindows.Services;

public class SettingsService : ISettingsService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static string DefaultSettingsPath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "DeepFocus", "settings.json");

    public AppSettings Settings { get; protected set; } = new();

    protected virtual string GetSettingsPath() => DefaultSettingsPath;

    public async Task LoadAsync()
    {
        var path = GetSettingsPath();
        if (!File.Exists(path))
        {
            Settings = GetDefaultSettings();
            return;
        }

        try
        {
            var settings = await File.ReadAllTextAsync(path).ConfigureAwait(false);
            Settings = JsonSerializer.Deserialize<AppSettings>(settings) ?? GetDefaultSettings();
        }
        catch(Exception ex)
        {
            Console.WriteLine("Failed to load settings.json: " + ex.Message);
            Settings = GetDefaultSettings();
        }
    }

    private static AppSettings GetDefaultSettings()
    {
        return new AppSettings
        {
            StartOnBoot = true,
            DimmingLevel = 70,
            DimTaskbar = false,
            FocusApps =
            [
                new FocusAppEntry
                {
                    ProcessName = "ApplicationFrameHost",
                    Title = "Clock"
                }
            ]
        };
    }

    public async Task SaveAsync()
    {
        var path = GetSettingsPath();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await using var stream = File.Create(path);
        await JsonSerializer.SerializeAsync(stream, Settings, JsonOptions);
        await stream.FlushAsync();
    }
}
