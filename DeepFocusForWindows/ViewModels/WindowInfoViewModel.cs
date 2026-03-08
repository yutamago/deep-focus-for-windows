using System;
using CommunityToolkit.Mvvm.ComponentModel;
using DeepFocusForWindows.Models;

namespace DeepFocusForWindows.ViewModels;

public partial class WindowInfoViewModel : ViewModelBase
{
    public WindowInfo Model { get; }

    public WindowInfoViewModel(WindowInfo model) => Model = model;

    public IntPtr  Handle      => Model.Handle;
    public string  Title       => Model.Title;
    public string  ProcessName => Model.ProcessName;
    public string  ProcessNameLabel => string.IsNullOrEmpty(Model.ProcessName)
        ? string.Empty : $"({Model.ProcessName})";

    [ObservableProperty]
    private bool _isSelected;

    /// <summary>Invoked after IsSelected changes; wired by ConfigurationViewModel.</summary>
    public Action<WindowInfoViewModel>? SelectionChanged { get; set; }

    partial void OnIsSelectedChanged(bool value)
    {
        Model.IsSelected = value;
        SelectionChanged?.Invoke(this);
    }
}
