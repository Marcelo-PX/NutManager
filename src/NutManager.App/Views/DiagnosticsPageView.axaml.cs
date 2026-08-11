using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Input.Platform;
using Avalonia.Platform.Storage;
using NutManager.App.ViewModels;

namespace NutManager.App.Views;

public partial class DiagnosticsPageView : UserControl
{
    public DiagnosticsPageView()
    {
        InitializeComponent();
    }

    private async void CopyDiagnosticsButton_OnClick(object? sender, RoutedEventArgs eventArgs)
    {
        if (DataContext is DiagnosticsPageViewModel viewModel && TopLevel.GetTopLevel(this)?.Clipboard is { } clipboard)
        {
            try
            {
                await clipboard.SetTextAsync(viewModel.CreateDiagnosticReport());
                viewModel.ReportDiagnosticCopyResult(succeeded: true);
            }
            catch
            {
                viewModel.ReportDiagnosticCopyResult(succeeded: false);
            }
        }
    }

    private async void SelectDirectoryButton_OnClick(object? sender, RoutedEventArgs eventArgs)
    {
        if (DataContext is not DiagnosticsPageViewModel viewModel ||
            !viewModel.CanInspectLocalInstallation ||
            TopLevel.GetTopLevel(this) is not { } topLevel)
        {
            return;
        }

        var folders = await topLevel.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = viewModel.Strings.Get("Diagnostics.SelectInstallation"),
            AllowMultiple = false
        });
        var path = folders.FirstOrDefault()?.TryGetLocalPath();
        if (!string.IsNullOrWhiteSpace(path))
        {
            await viewModel.InspectLocalInstallationDirectoryAsync(path);
        }
    }
}
