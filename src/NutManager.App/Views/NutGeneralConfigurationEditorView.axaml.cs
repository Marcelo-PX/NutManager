using Avalonia.Controls;
using Avalonia.Interactivity;
using NutManager.App.ViewModels;

namespace NutManager.App.Views;

public partial class NutGeneralConfigurationEditorView : UserControl
{
    public NutGeneralConfigurationEditorView() => InitializeComponent();

    // Basic/Advanced is a view-only filter over the same semantic draft.
    private void ShowBasicButton_OnClick(object? sender, RoutedEventArgs eventArgs)
    {
        if (DataContext is ServerGeneralConfigurationEditorViewModel editor) editor.ShowAdvanced = false;
    }

    private void ShowAdvancedButton_OnClick(object? sender, RoutedEventArgs eventArgs)
    {
        if (DataContext is ServerGeneralConfigurationEditorViewModel editor) editor.ShowAdvanced = true;
    }
}
