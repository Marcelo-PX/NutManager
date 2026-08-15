using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
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
            : new ColumnDefinitions("290,16,*");
        ManagedProfilesLayout.RowDefinitions = compact
            ? new RowDefinitions("Auto,16,Auto")
            : new RowDefinitions("Auto");
        Grid.SetColumn(ProfileListPanel, 0);
        Grid.SetRow(ProfileListPanel, 0);
        Grid.SetColumn(ProfileEditorPanel, compact ? 0 : 2);
        Grid.SetRow(ProfileEditorPanel, compact ? 2 : 0);

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
