using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
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
            Settings = new AppSettings();
            return;
        }

        await using var stream = File.OpenRead(path);
        Settings = await JsonSerializer.DeserializeAsync<AppSettings>(stream) ?? new AppSettings();
    }

    public async Task SaveAsync()
    {
        var path = GetSettingsPath();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await using var stream = File.Create(path);
        await JsonSerializer.SerializeAsync(stream, Settings, JsonOptions);
    }
}
