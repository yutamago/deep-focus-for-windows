using System;
using DeepFocusForWindows.Services;
using FluentAssertions;
using Microsoft.Win32;
using Xunit;

namespace DeepFocusForWindows.Tests.Services;

/// <summary>
/// Tests for StartupService. Uses HKCU (no admin rights required).
/// A unique key name is used per test to avoid collisions.
/// </summary>
public class StartupServiceTests : IDisposable
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private readonly string _appName = "DeepFocusTest_" + Guid.NewGuid().ToString("N")[..8];
    private readonly TestableStartupService _svc;

    public StartupServiceTests()
    {
        _svc = new TestableStartupService(_appName);
    }

    public void Dispose()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
        key?.DeleteValue(_appName, throwOnMissingValue: false);
    }

    [Fact]
    public void SetStartOnBoot_True_CreatesRegistryValue()
    {
        _svc.SetStartOnBoot(true);
        _svc.IsStartOnBootEnabled().Should().BeTrue();
    }

    [Fact]
    public void SetStartOnBoot_False_RemovesRegistryValue()
    {
        _svc.SetStartOnBoot(true);
        _svc.SetStartOnBoot(false);
        _svc.IsStartOnBootEnabled().Should().BeFalse();
    }

    [Fact]
    public void IsStartOnBootEnabled_WhenNeverSet_ReturnsFalse()
    {
        _svc.IsStartOnBootEnabled().Should().BeFalse();
    }
}

internal sealed class TestableStartupService(string appName) : StartupService
{
    protected override string AppName => appName;
}
