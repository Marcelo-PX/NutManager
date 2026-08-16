using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using NutManager.App.ViewModels;
using NutManager.Core.Administration;

namespace NutManager.App.Views;

public partial class WindowsServiceAdministrationView : UserControl
{
    public WindowsServiceAdministrationView() => InitializeComponent();

    /// <summary>
    /// The remote monitor polls only while this section is on screen. Sections here are switched by
    /// visibility rather than by being created and destroyed, so attachment alone would leave a timer
    /// running against a host nobody is looking at, for as long as the window stays open.
    /// </summary>
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == IsVisibleProperty || change.Property == DataContextProperty)
        {
            UpdateRemoteMonitoring();
        }
    }

    private bool _attached;

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs eventArgs)
    {
        base.OnAttachedToVisualTree(eventArgs);
        _attached = true;
        UpdateRemoteMonitoring();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs eventArgs)
    {
        base.OnDetachedFromVisualTree(eventArgs);
        _attached = false;
        // Nothing awaits this: the stop is idempotent, and the monitor invalidates its own generation
        // before cancelling, so a probe still inside Win32 cannot publish into a view that has gone.
        _ = Monitor?.StopMonitoringAsync();
    }

    private RemoteWindowsServiceViewModel? Monitor => (DataContext as AdministrationPageViewModel)?.RemoteWindowsService;

    private void UpdateRemoteMonitoring()
    {
        if (Monitor is not { } monitor) return;

        if (IsVisible && _attached) monitor.StartMonitoring();
        else _ = monitor.StopMonitoringAsync();
    }

    private void StartServiceButton_OnClick(object? sender, RoutedEventArgs eventArgs) => (DataContext as AdministrationPageViewModel)?.PrepareServiceAction(NutAdministrativeAction.StartService);
    private void StopServiceButton_OnClick(object? sender, RoutedEventArgs eventArgs) => (DataContext as AdministrationPageViewModel)?.PrepareServiceAction(NutAdministrativeAction.StopService);
    private void RestartServiceButton_OnClick(object? sender, RoutedEventArgs eventArgs) => (DataContext as AdministrationPageViewModel)?.PrepareServiceAction(NutAdministrativeAction.RestartService);
    private void RepairPermissionsButton_OnClick(object? sender, RoutedEventArgs eventArgs) => (DataContext as AdministrationPageViewModel)?.PreparePermissionRepair();
}
