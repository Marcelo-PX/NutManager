using Avalonia.Controls;
using Avalonia.Interactivity;
using NutManager.App.ViewModels;

namespace NutManager.App.Views;

public partial class UpsConfigurationFieldView : UserControl
{
    public UpsConfigurationFieldView() => InitializeComponent();

    private void ReplaceSensitiveButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not UpsConfigurationFieldViewModel field) return;
        var replacement = SensitiveReplacementBox.Text ?? string.Empty;
        try { field.ReplaceSensitive(replacement.AsSpan()); }
        finally { SensitiveReplacementBox.Text = string.Empty; }
    }

    private void RemoveSensitiveButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is UpsConfigurationFieldViewModel field) field.RemoveSensitive();
    }

    private void SetAutomaticButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is UpsConfigurationFieldViewModel field) field.SetAutomatic();
    }
}
