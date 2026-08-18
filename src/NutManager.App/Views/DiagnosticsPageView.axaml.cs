using Avalonia;
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
        SizeChanged += (_, args) => ApplySummaryLayout(args.NewSize.Width);
    }

    private void ApplySummaryLayout(double width)
    {
        var columns = width >= 1120 ? 4 : width >= 650 ? 2 : 1;
        DiagnosticsSummaryGrid.ColumnDefinitions = new ColumnDefinitions(string.Join(',', Enumerable.Repeat("*", columns)));
        DiagnosticsSummaryGrid.RowDefinitions = new RowDefinitions(string.Join(',', Enumerable.Repeat("Auto", 4 / columns)));

        var cards = new Control[]
        {
            ConnectionSummaryCard,
            FreshnessSummaryCard,
            DiscoverySummaryCard,
            TechnicalSummaryCard
        };
        for (var index = 0; index < cards.Length; index++)
        {
            Grid.SetColumn(cards[index], index % columns);
            Grid.SetRow(cards[index], index / columns);
        }
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
