using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Styling;
using NutManager.App.ViewModels;

namespace NutManager.App;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }

    private void Window_OnSizeChanged(object? sender, SizeChangedEventArgs eventArgs)
    {
        if (DataContext is MainWindowViewModel viewModel)
        {
            viewModel.UpdateLayoutWidth(eventArgs.NewSize.Width);
        }
    }

    private void Window_OnKeyDown(object? sender, KeyEventArgs eventArgs)
    {
        if (eventArgs.Key == Key.B && eventArgs.KeyModifiers.HasFlag(KeyModifiers.Control) && DataContext is MainWindowViewModel viewModel)
        {
            viewModel.ToggleNavigationCommand.Execute(null);
            eventArgs.Handled = true;
        }
    }

    private void ThemeToggleButton_OnClick(object? sender, Avalonia.Interactivity.RoutedEventArgs eventArgs)
    {
        if (DataContext is MainWindowViewModel viewModel)
        {
            viewModel.ToggleThemeCommand.Execute(ActualThemeVariant == ThemeVariant.Dark);
        }
    }
}
