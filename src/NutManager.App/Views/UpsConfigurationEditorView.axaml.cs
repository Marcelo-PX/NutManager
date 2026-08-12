using Avalonia.Controls;
using Avalonia.Interactivity;
using NutManager.App.ViewModels;

namespace NutManager.App.Views;

public partial class UpsConfigurationEditorView : UserControl
{
    public UpsConfigurationEditorView() => InitializeComponent();

    private void RemoveCustomButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is UpsConfigurationEditorViewModel editor && sender is Button { DataContext: UpsCustomParameterViewModel parameter })
            editor.RemoveCustom(parameter);
    }
}
