using Avalonia.Controls;
using Avalonia.Interactivity;
using NutManager.App.ViewModels;

namespace NutManager.App.Views;

public partial class DevicesPageView : UserControl
{
    public DevicesPageView()
    {
        InitializeComponent();
    }

    private async void OnDeviceSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (DataContext is DevicesPageViewModel viewModel)
        {
            await viewModel.SelectDeviceCommand.ExecuteAsync(viewModel.SelectedDevice);
        }
    }
}
