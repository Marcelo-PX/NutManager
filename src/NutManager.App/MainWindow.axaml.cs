using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Styling;
using Avalonia.Threading;
using NutManager.App.ViewModels;

namespace NutManager.App;

public partial class MainWindow : Window
{
    private MainWindowViewModel? _observedViewModel;

    public MainWindow()
    {
        InitializeComponent();
        DataContextChanged += MainWindow_OnDataContextChanged;
        Opened += (_, _) => SynchronizeEffectiveTheme();
        ActualThemeVariantChanged += (_, _) => SynchronizeEffectiveTheme();
    }

    private void MainWindow_OnDataContextChanged(object? sender, EventArgs eventArgs)
    {
        if (_observedViewModel is not null) _observedViewModel.PropertyChanged -= ViewModel_OnPropertyChanged;
        _observedViewModel = DataContext as MainWindowViewModel;
        if (_observedViewModel is null) return;

        _observedViewModel.PropertyChanged += ViewModel_OnPropertyChanged;
        SynchronizeEffectiveTheme();
    }

    private void ViewModel_OnPropertyChanged(object? sender, PropertyChangedEventArgs eventArgs)
    {
        if (eventArgs.PropertyName != nameof(MainWindowViewModel.IsOverlayOpen)) return;

        Dispatcher.UIThread.Post(
            () =>
            {
                if (_observedViewModel?.IsOverlayOpen == true) OverlayCloseButton.Focus();
                else HeaderNavigationButton.Focus();
            },
            DispatcherPriority.Input);
    }

    private void SynchronizeEffectiveTheme() =>
        (DataContext as MainWindowViewModel)?.UpdateEffectiveTheme(ActualThemeVariant == ThemeVariant.Dark);

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
        else if (eventArgs.Key == Key.Escape && DataContext is MainWindowViewModel overlayViewModel && overlayViewModel.IsOverlayOpen)
        {
            overlayViewModel.CloseNavigationOverlay();
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

    private void NavigationOverlayScrim_OnPointerPressed(object? sender, PointerPressedEventArgs eventArgs)
    {
        if (DataContext is MainWindowViewModel viewModel)
        {
            viewModel.CloseNavigationOverlay();
            eventArgs.Handled = true;
        }
    }
}
