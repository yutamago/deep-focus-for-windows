using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using DeepFocusForWindows.Models;
using DeepFocusForWindows.Services;
using DeepFocusForWindows.ViewModels;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace DeepFocusForWindows.Tests.ViewModels;

public class ConfigurationViewModelTests
{
    private static (ConfigurationViewModel vm,
                    ISettingsService       settings,
                    IDimmingService        dimming)
        Create(AppSettings? initial = null)
    {
        var settings = Substitute.For<ISettingsService>();
        settings.Settings.Returns(initial ?? new AppSettings());

        var windowEnum = Substitute.For<IWindowEnumerationService>();
        windowEnum.GetVisibleWindows().Returns([]);

        var dimming = Substitute.For<IDimmingService>();
        dimming.ExcludedHandles.Returns(new HashSet<IntPtr>());

        var startup      = Substitute.For<IStartupService>();
        var focusSession = Substitute.For<IFocusSessionService>();
        focusSession.IsSupported.Returns(false);

        var vm = new ConfigurationViewModel(settings, windowEnum, dimming, startup, focusSession);
        return (vm, settings, dimming);
    }

    [Fact]
    public void Constructor_LoadsSettingsIntoProperties()
    {
        var initial = new AppSettings
        {
            DimmingLevel   = 55,
            StartOnBoot    = false,
            IsDimmingEnabled = true
        };
        var (vm, _, _) = Create(initial);

        vm.DimmingLevel.Should().Be(55);
        vm.StartOnBoot.Should().BeFalse();
        vm.IsDimmingEnabled.Should().BeTrue();
    }

    [Fact]
    public void DimmingLevelText_ReflectsDimmingLevel()
    {
        var (vm, _, _) = Create();
        vm.DimmingLevel = 42;
        vm.DimmingLevelText.Should().Be("42%");
    }

    [Fact]
    public void IsDimmingEnabled_True_CallsDimmingServiceEnable()
    {
        var (vm, _, dimming) = Create();
        vm.IsDimmingEnabled = true;
        dimming.Received(1).Enable();
    }

    [Fact]
    public void IsDimmingEnabled_False_CallsDimmingServiceDisable()
    {
        var (vm, _, dimming) = Create(new AppSettings { IsDimmingEnabled = true });
        vm.IsDimmingEnabled = false;
        dimming.Received(1).Disable();
    }

    [Fact]
    public async Task ApplyCommand_PersistsSettings()
    {
        var (vm, settings, _) = Create();
        vm.DimmingLevel = 80;
        vm.StartOnBoot  = false;

        await vm.ApplyCommand.ExecuteAsync(null);

        await settings.Received(1).SaveAsync();
        settings.Settings.DimmingLevel.Should().Be(80);
        settings.Settings.StartOnBoot.Should().BeFalse();
    }

    [Fact]
    public void CloseRequested_RaisedOnSaveAndClose()
    {
        var (vm, _, _) = Create();
        bool raised = false;
        vm.CloseRequested += (_, _) => raised = true;

        vm.SaveAndCloseCommand.Execute(null);

        raised.Should().BeTrue();
    }
}
