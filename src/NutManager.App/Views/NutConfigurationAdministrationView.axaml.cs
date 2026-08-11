using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using NutManager.App.ViewModels;

namespace NutManager.App.Views;

public partial class NutConfigurationAdministrationView : UserControl
{
    public NutConfigurationAdministrationView()
    {
        InitializeComponent();
        SizeChanged += (_, _) => UpdateResponsiveLayout();
    }

    private void UpdateResponsiveLayout()
    {
        var compact = Bounds.Width < 760;
        ConfigurationEditorLayout.ColumnDefinitions = compact ? new ColumnDefinitions("*") : new ColumnDefinitions("260,16,*");
        ConfigurationEditorLayout.RowDefinitions = compact ? new RowDefinitions("Auto,16,Auto") : new RowDefinitions("Auto");
        Grid.SetColumn(ConfigurationFilesPanel, 0);
        Grid.SetRow(ConfigurationFilesPanel, 0);
        Grid.SetColumn(ConfigurationEditorPanel, compact ? 0 : 2);
        Grid.SetRow(ConfigurationEditorPanel, compact ? 2 : 0);
    }

    private async void SelectDirectoryButton_OnClick(object? sender, RoutedEventArgs eventArgs)
    {
        if (DataContext is not AdministrationPageViewModel viewModel ||
            !viewModel.IsLocalManagementProfile ||
            !viewModel.CanChangeInstallation ||
            TopLevel.GetTopLevel(this) is not { } topLevel)
        {
            return;
        }

        var folders = await topLevel.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = viewModel.Strings.Get("Administration.Configuration.SelectInstallation"),
            AllowMultiple = false
        });
        var path = folders.FirstOrDefault()?.TryGetLocalPath();
        if (!string.IsNullOrWhiteSpace(path)) await viewModel.InspectInstallationDirectoryAsync(path);
    }

    private async void ConfigurationFileList_OnSelectionChanged(object? sender, SelectionChangedEventArgs eventArgs)
    {
        if (DataContext is AdministrationPageViewModel viewModel &&
            eventArgs.AddedItems.OfType<NutConfigurationFileItemViewModel>().FirstOrDefault() is { } file)
        {
            await viewModel.SelectFileAsync(file);
        }
    }
}
