using System;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DeepFocusForWindows.Services;

namespace DeepFocusForWindows.ViewModels;

public partial class TrayIconViewModel : ViewModelBase
{
    private readonly IDimmingService _dimming;

    public TrayIconViewModel(IDimmingService dimming)
    {
        _dimming = dimming;
        _dimming.StateChanged += (_, _) => OnPropertyChanged(nameof(IsDimmingEnabled));
    }

    public bool IsDimmingEnabled
    {
        get => _dimming.IsActive && !_dimming.IsTemporarilyDisabled;
        set
        {
            if (value) _dimming.Enable();
            else       _dimming.Disable();
        }
    }

    [RelayCommand]
    private void ToggleDimming() => IsDimmingEnabled = !IsDimmingEnabled;

    [RelayCommand]
    private void OpenSettings() => OpenSettingsRequested?.Invoke(this, EventArgs.Empty);

    [RelayCommand]
    private void Exit() => ExitRequested?.Invoke(this, EventArgs.Empty);

    public event EventHandler? OpenSettingsRequested;
    public event EventHandler? ExitRequested;
}
