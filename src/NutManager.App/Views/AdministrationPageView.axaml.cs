using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using NutManager.App.ViewModels;

namespace NutManager.App.Views;

public partial class AdministrationPageView : UserControl
{
    public AdministrationPageView()
    {
        InitializeComponent();
    }

    private async void SelectDirectoryButton_OnClick(object? sender, RoutedEventArgs eventArgs)
    {
        if (DataContext is not AdministrationPageViewModel viewModel || TopLevel.GetTopLevel(this) is not { } topLevel)
        {
            return;
        }

        var folders = await topLevel.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Selecionar instalação local do NUT",
            AllowMultiple = false
        });
        var path = folders.FirstOrDefault()?.TryGetLocalPath();
        if (!string.IsNullOrWhiteSpace(path))
        {
            await viewModel.InspectInstallationDirectoryAsync(path);
        }
    }

    private async void ConfigurationFileList_OnSelectionChanged(object? sender, SelectionChangedEventArgs eventArgs)
    {
        if (DataContext is AdministrationPageViewModel viewModel && eventArgs.AddedItems.OfType<NutConfigurationFileItemViewModel>().FirstOrDefault() is { } file)
        {
            await viewModel.SelectFileAsync(file);
        }
    }
}
