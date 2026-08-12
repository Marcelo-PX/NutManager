using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using NutManager.App.ViewModels;
using NutManager.Core.Models;

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
        SynchronizeWindowStateGlyph();
    }

    /// <summary>
    /// Window chrome behaviour for the extended client area. Drag, maximise and close remain the
    /// standard Avalonia window operations; no platform interop is introduced.
    /// </summary>
    private void TitleBar_OnPointerPressed(object? sender, PointerPressedEventArgs eventArgs)
    {
        if (eventArgs.Source is Visual source && source.FindAncestorOfType<Button>() is not null) return;
        if (!eventArgs.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        BeginMoveDrag(eventArgs);
    }

    private void TitleBar_OnDoubleTapped(object? sender, Avalonia.Input.TappedEventArgs eventArgs)
    {
        if (eventArgs.Source is Visual source && source.FindAncestorOfType<Button>() is not null) return;
        ToggleMaximized();
    }

    private void MinimizeButton_OnClick(object? sender, Avalonia.Interactivity.RoutedEventArgs eventArgs) =>
        WindowState = WindowState.Minimized;

    private void MaximizeButton_OnClick(object? sender, Avalonia.Interactivity.RoutedEventArgs eventArgs) =>
        ToggleMaximized();

    private void CloseButton_OnClick(object? sender, Avalonia.Interactivity.RoutedEventArgs eventArgs) => Close();

    private void ToggleMaximized()
    {
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
        SynchronizeWindowStateGlyph();
    }

    private void SynchronizeWindowStateGlyph()
    {
        var maximized = WindowState == WindowState.Maximized;
        if (MaximizeGlyph is not null) MaximizeGlyph.IsVisible = !maximized;
        if (RestoreGlyph is not null) RestoreGlyph.IsVisible = maximized;
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == WindowStateProperty) SynchronizeWindowStateGlyph();
    }

    private void LightThemeButton_OnClick(object? sender, Avalonia.Interactivity.RoutedEventArgs eventArgs) =>
        (DataContext as MainWindowViewModel)?.SetTheme(ThemePreference.Light);

    private void DarkThemeButton_OnClick(object? sender, Avalonia.Interactivity.RoutedEventArgs eventArgs) =>
        (DataContext as MainWindowViewModel)?.SetTheme(ThemePreference.Dark);

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

    private void NavigationOverlayScrim_OnPointerPressed(object? sender, PointerPressedEventArgs eventArgs)
    {
        if (DataContext is MainWindowViewModel viewModel)
        {
            viewModel.CloseNavigationOverlay();
            eventArgs.Handled = true;
        }
    }
}
