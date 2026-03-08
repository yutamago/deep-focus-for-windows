using System.IO;
using System.Threading.Tasks;
using DeepFocusForWindows.Models;
using DeepFocusForWindows.Services;
using FluentAssertions;
using Xunit;

namespace DeepFocusForWindows.Tests.Services;

public class SettingsServiceTests : IAsyncLifetime
{
    private string _tempDir = string.Empty;

    public Task InitializeAsync()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(_tempDir);
        return Task.CompletedTask;
    }

    public Task DisposeAsync()
    {
        Directory.Delete(_tempDir, recursive: true);
        return Task.CompletedTask;
    }

    private SettingsServiceWithPath CreateService(string fileName = "settings.json")
        => new(Path.Combine(_tempDir, fileName));

    [Fact]
    public async Task LoadAsync_WhenFileAbsent_ReturnsDefaults()
    {
        var svc = CreateService("missing.json");
        await svc.LoadAsync();

        svc.Settings.IsFirstRun.Should().BeTrue();
        svc.Settings.StartOnBoot.Should().BeTrue();
        svc.Settings.DimmingLevel.Should().Be(70);
    }

    [Fact]
    public async Task SaveAndLoad_RoundTripsAllProperties()
    {
        var svc = CreateService();
        svc.Settings.IsFirstRun   = false;
        svc.Settings.StartOnBoot  = false;
        svc.Settings.DimmingLevel = 42;
        svc.Settings.FocusApps = [
            new FocusAppEntry { ProcessName = "notepad", Title = "Untitled - Notepad" },
            new FocusAppEntry { ProcessName = "code",    Title = "Visual Studio Code" },
        ];

        await svc.SaveAsync();
        await svc.LoadAsync();

        svc.Settings.IsFirstRun.Should().BeFalse();
        svc.Settings.StartOnBoot.Should().BeFalse();
        svc.Settings.DimmingLevel.Should().Be(42);
        svc.Settings.FocusApps.Should().HaveCount(2);
        svc.Settings.FocusApps[0].ProcessName.Should().Be("notepad");
        svc.Settings.FocusApps[1].ProcessName.Should().Be("code");
    }

    [Fact]
    public async Task SaveAsync_CreatesDirectoryIfMissing()
    {
        var nestedPath = Path.Combine(_tempDir, "sub", "settings.json");
        var svc = new SettingsServiceWithPath(nestedPath);
        await svc.SaveAsync();
        File.Exists(nestedPath).Should().BeTrue();
    }
}

/// <summary>Testable variant that redirects to a custom settings path.</summary>
internal sealed class SettingsServiceWithPath(string path) : SettingsService
{
    protected override string GetSettingsPath() => path;
}
