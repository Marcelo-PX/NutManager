using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using NutManager.App.ViewModels;

namespace NutManager.App.Views;

public partial class SettingsPageView : UserControl
{
    public SettingsPageView()
    {
        InitializeComponent();
        SizeChanged += (_, _) => UpdateResponsiveLayout();
    }

    private void UpdateResponsiveLayout()
    {
        var compact = Bounds.Width < 760;

        AppearanceLayout.ColumnDefinitions = compact
            ? new ColumnDefinitions("*")
            : new ColumnDefinitions("*,*,*,*");
        AppearanceLayout.RowDefinitions = compact
            ? new RowDefinitions("Auto,18,Auto,18,Auto,18,Auto")
            : new RowDefinitions("Auto");
        Position(ThemePreferencePanel, 0, 0);
        Position(LanguagePreferencePanel, compact ? 0 : 1, compact ? 2 : 0);
        Position(SidebarPreferencePanel, compact ? 0 : 2, compact ? 4 : 0);
        Position(TransparencyPreferencePanel, compact ? 0 : 3, compact ? 6 : 0);

        ManagedProfilesLayout.ColumnDefinitions = compact
            ? new ColumnDefinitions("*")
            : new ColumnDefinitions("330,16,*");
        ManagedProfilesLayout.RowDefinitions = compact
            ? new RowDefinitions("Auto,16,Auto,14,Auto,16,Auto")
            : new RowDefinitions("Auto,14,Auto,16,Auto");
        Position(ProfileListPanel, 0, compact ? 0 : 2);
        Position(ProfileEditorHeader, compact ? 0 : 2, compact ? 2 : 0);
        Position(ProfileIdentityPanel, compact ? 0 : 2, compact ? 4 : 2);
        Position(ProfileEditorPanel, 0, compact ? 6 : 4);
        Grid.SetColumnSpan(ProfileEditorPanel, compact ? 1 : 3);

        GeneralPreferencesLayout.ColumnDefinitions = compact
            ? new ColumnDefinitions("*")
            : new ColumnDefinitions("*,*");
        GeneralPreferencesLayout.RowDefinitions = compact
            ? new RowDefinitions("Auto,12,Auto")
            : new RowDefinitions("Auto");
        Position(ConnectionTimeoutPanel, 0, 0);
        Position(PollingIntervalPanel, compact ? 0 : 1, compact ? 2 : 0);
    }

    private static void Position(Control control, int column, int row)
    {
        Grid.SetColumn(control, column);
        Grid.SetRow(control, row);
    }

    /// <summary>
    /// Saving reprojects the confirmed profile and rebuilds the profile cards near the top of this
    /// page. Avalonia consequently remeasures the scroll content; retaining the offset here keeps a
    /// save in "Managed NUT files" from navigating the administrator away from the control they
    /// just used. The view model still owns the command and every persistence rule.
    /// </summary>
    private async void SaveProfileButton_OnClick(object? sender, RoutedEventArgs eventArgs)
    {
        if (DataContext is not SettingsPageViewModel viewModel || !viewModel.SaveProfileCommand.CanExecute(null))
        {
            return;
        }

        var offset = SettingsScrollViewer.Offset;
        try
        {
            await viewModel.SaveProfileCommand.ExecuteAsync(null);
        }
        finally
        {
            // Selection reconciliation in the profile ListBox can request BringIntoView during the
            // next layout/render pass. Background runs after that request, so this restoration is
            // the final scroll operation instead of being immediately overwritten.
            await Dispatcher.UIThread.InvokeAsync(
                () => SettingsScrollViewer.Offset = offset,
                DispatcherPriority.Background);
        }
    }

    private async void SelectSshPrivateKeyButton_OnClick(object? sender, RoutedEventArgs eventArgs)
    {
        if (DataContext is not SettingsPageViewModel viewModel || TopLevel.GetTopLevel(this) is not { } topLevel)
        {
            return;
        }

        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = viewModel.SelectPrivateKeyDialogTitle,
            AllowMultiple = false
        });
        var path = files.FirstOrDefault()?.TryGetLocalPath();
        if (!string.IsNullOrWhiteSpace(path))
        {
            viewModel.ProfileDraft.SshPrivateKeyPath = path;
        }
    }
}
