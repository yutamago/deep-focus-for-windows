using System;
using System.Collections.Generic;
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
            DimmingLevel = 55,
            StartOnBoot  = false,
        };
        var (vm, _, _) = Create(initial);

        vm.DimmingLevel.Should().Be(55);
        vm.StartOnBoot.Should().BeFalse();
    }

    [Fact]
    public void DimmingLevelText_ReflectsDimmingLevel()
    {
        var (vm, _, _) = Create();
        vm.DimmingLevel = 42;
        vm.DimmingLevelText.Should().Be("42%");
    }

    [Fact]
    public void TogglePreviewCommand_Enable_CallsDimmingServiceEnable()
    {
        var (vm, _, dimming) = Create();
        vm.TogglePreviewCommand.Execute(null);
        dimming.Received(1).Enable();
        vm.IsPreviewActive.Should().BeTrue();
    }

    [Fact]
    public void TogglePreviewCommand_Disable_CallsDimmingServiceDisable()
    {
        var (vm, _, dimming) = Create();
        vm.TogglePreviewCommand.Execute(null); // enable
        vm.TogglePreviewCommand.Execute(null); // disable
        dimming.Received(1).Disable();
        vm.IsPreviewActive.Should().BeFalse();
    }

    [Fact]
    public void OnWindowClosing_StopsActivePreview()
    {
        var (vm, _, dimming) = Create();
        vm.TogglePreviewCommand.Execute(null); // enable preview
        vm.OnWindowClosing();
        dimming.Received(1).Disable();
        vm.IsPreviewActive.Should().BeFalse();
    }

    [Fact]
    public void CloseCommand_RaisesCloseRequested()
    {
        var (vm, _, _) = Create();
        bool raised = false;
        vm.CloseRequested += (_, _) => raised = true;

        vm.CloseCommand.Execute(null);

        raised.Should().BeTrue();
    }
}
