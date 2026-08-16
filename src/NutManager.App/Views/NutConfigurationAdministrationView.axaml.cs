using Avalonia;
using Avalonia.Controls;
using NutManager.App.Presentation.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using NutManager.App.ViewModels;

namespace NutManager.App.Views;

public partial class NutConfigurationAdministrationView : UserControl
{
    /// <summary>
    /// The page used to rebuild its own grid on every resize, moving the file rail from beside the
    /// editor to above it once the window got too narrow to hold both. The strip is above the editor
    /// at every width now, so there is nothing left to reflow: narrowing wraps the chips onto a
    /// second line and folds their labels, which the panel and the view model already handle
    /// between them.
    /// </summary>
    public NutConfigurationAdministrationView() => InitializeComponent();

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

    /// <summary>
    /// Gives the selected row's icon a single pop when it becomes current. It is deliberately a
    /// one-shot: the connection light is the only thing in this application that loops, and a rail
    /// of five breathing icons would compete with the form beside it for attention.
    /// </summary>
    private void ConfigurationFileRailIcon_OnAttached(object? sender, EventArgs eventArgs)
    {
        if (sender is not Panel panel || panel.DataContext is not NutConfigurationFileItemViewModel file)
        {
            return;
        }

        if (file.IsSelected)
        {
            NutIconMotion.PopOnce(panel, new Size(18, 18), 1.18, TimeSpan.FromMilliseconds(220));
        }
        else
        {
            NutIconMotion.Reset(panel, 1);
        }
    }

    /// <summary>
    /// The rail's rows are buttons rather than list items, so selection is an explicit click. That
    /// matters for the dirty-draft guard: a ListBox moves its own selection before anything can
    /// refuse the change, whereas a button leaves the view model in charge of whether the switch
    /// happens at all.
    /// </summary>
    private async void ConfigurationFileRailItem_OnClick(object? sender, RoutedEventArgs eventArgs)
    {
        if (DataContext is not AdministrationPageViewModel viewModel ||
            (sender as Button)?.DataContext is not NutConfigurationFileItemViewModel file)
        {
            return;
        }

        // Nothing awaits this handler, so an escaping exception would tear the process down instead
        // of surfacing. SelectFileAsync reports its own failures; this only stops the crash.
        try
        {
            await viewModel.SelectFileAsync(file);
        }
        catch (Exception)
        {
            // Already reflected in the view model status.
        }
    }
}
