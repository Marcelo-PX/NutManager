using Avalonia.Controls;
using Avalonia.Interactivity;
using NutManager.App.ViewModels;

namespace NutManager.App.Views;

public partial class DevicesPageView : UserControl
{
    public DevicesPageView()
    {
        InitializeComponent();
        SizeChanged += (_, _) => UpdateResponsiveLayout();
    }

    private void UpdateResponsiveLayout()
    {
        var compact = Bounds.Width < 760;
        DevicesLayout.ColumnDefinitions = compact ? new ColumnDefinitions("*") : new ColumnDefinitions("260,16,*");
        DevicesLayout.RowDefinitions = compact ? new RowDefinitions("Auto,16,Auto") : new RowDefinitions("Auto");
        Grid.SetColumn(DeviceListPanel, 0);
        Grid.SetRow(DeviceListPanel, 0);
        Grid.SetColumn(DeviceDetailsPanel, compact ? 0 : 2);
        Grid.SetRow(DeviceDetailsPanel, compact ? 2 : 0);
    }

    private async void OnDeviceSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (DataContext is DevicesPageViewModel viewModel)
        {
            await viewModel.SelectDeviceCommand.ExecuteAsync(viewModel.SelectedDevice);
        }
    }
}
