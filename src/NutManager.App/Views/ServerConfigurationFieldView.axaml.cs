using Avalonia.Controls;
using Avalonia.Interactivity;
using NutManager.App.ViewModels;

namespace NutManager.App.Views;

public partial class ServerConfigurationFieldView : UserControl
{
    public ServerConfigurationFieldView() => InitializeComponent();

    private void SetAutomaticButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is ServerConfigurationFieldViewModel field) field.SetAutomatic();
    }
}
