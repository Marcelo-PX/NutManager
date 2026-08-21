using Avalonia;
using Avalonia.Controls;
using NutManager.App.Presentation.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using NutManager.App.ViewModels;
using System.ComponentModel;

namespace NutManager.App.Views;

public partial class NutConfigurationAdministrationView : UserControl
{
    private AdministrationPageViewModel? _layoutViewModel;

    public NutConfigurationAdministrationView()
    {
        InitializeComponent();
        SizeChanged += (_, _) => UpdateResponsiveLayout();
        DataContextChanged += (_, _) => ObserveLayoutViewModel();
    }

    /// <summary>
    /// The five-file switcher needs its full 540 px run. A remote context card fills the remaining
    /// wide-row space at the same height as the switcher; below that point it returns underneath
    /// instead of squeezing or wrapping the tabs. This is presentation-only and does not change the
    /// remote-session visibility or readiness rules.
    /// </summary>
    private void UpdateResponsiveLayout()
    {
        var wide = Bounds.Width >= 980;
        var filesVisible = _layoutViewModel?.IsConfigurationEditorVisible ?? true;
        var sideBySide = wide && filesVisible;
        ConfigurationEditorLayout.ColumnDefinitions = sideBySide
            ? new ColumnDefinitions("Auto,16,*")
            : new ColumnDefinitions("*");
        ConfigurationEditorLayout.RowDefinitions = sideBySide || !filesVisible
            ? new RowDefinitions("Auto")
            : new RowDefinitions("Auto,12,Auto");

        Grid.SetColumn(ConfigurationFilesRegion, 0);
        Grid.SetRow(ConfigurationFilesRegion, 0);
        Grid.SetColumn(RemoteConfigurationCard, sideBySide ? 2 : 0);
        Grid.SetRow(RemoteConfigurationCard, filesVisible && !sideBySide ? 2 : 0);
    }

    private void ObserveLayoutViewModel()
    {
        if (_layoutViewModel is not null)
        {
            _layoutViewModel.PropertyChanged -= LayoutViewModel_OnPropertyChanged;
        }

        _layoutViewModel = DataContext as AdministrationPageViewModel;
        if (_layoutViewModel is not null)
        {
            _layoutViewModel.PropertyChanged += LayoutViewModel_OnPropertyChanged;
        }

        UpdateResponsiveLayout();
    }

    private void LayoutViewModel_OnPropertyChanged(object? sender, PropertyChangedEventArgs eventArgs)
    {
        if (eventArgs.PropertyName == nameof(AdministrationPageViewModel.IsConfigurationEditorVisible))
        {
            UpdateResponsiveLayout();
        }
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

    /// <summary>
    /// Gives the selected tile's icon a single pop when it becomes current. It is deliberately a
    /// one-shot: the connection light is the only thing in this application that loops, and a strip
    /// of five breathing icons would compete with the form below it for attention.
    /// </summary>
    private void ConfigurationFileTileIcon_OnAttached(object? sender, EventArgs eventArgs)
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
    /// The strip's tiles are buttons rather than list items, so selection is an explicit click. That
    /// matters for the dirty-draft guard: a ListBox moves its own selection before anything can
    /// refuse the change, whereas a button leaves the view model in charge of whether the switch
    /// happens at all.
    /// </summary>
    private async void ConfigurationFileTile_OnClick(object? sender, RoutedEventArgs eventArgs)
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
