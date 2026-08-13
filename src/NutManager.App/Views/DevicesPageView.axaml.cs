using Avalonia.Controls;
using NutManager.App.ViewModels;

namespace NutManager.App.Views;

public partial class DevicesPageView : UserControl
{
    public DevicesPageView() => InitializeComponent();

    // The table, the selected-device panel and the technical details stack vertically and each
    // reflows on its own, so no manual column switching is required at narrow widths.
    private async void OnDeviceSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (DataContext is DevicesPageViewModel viewModel)
        {
            await viewModel.SelectDeviceCommand.ExecuteAsync(viewModel.SelectedDevice);
        }
    }
}
