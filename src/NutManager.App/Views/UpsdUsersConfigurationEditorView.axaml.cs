using Avalonia.Controls;
using Avalonia.Interactivity;
using NutManager.App.ViewModels;

namespace NutManager.App.Views;

public partial class UpsdUsersConfigurationEditorView : UserControl
{
    public UpsdUsersConfigurationEditorView() => InitializeComponent();

    /// <summary>
    /// The only place a typed password exists. It is read straight out of the two boxes, handed to
    /// the view model as spans, and both boxes are cleared immediately - so it is never assigned to
    /// a view-model property and never survives the click.
    /// </summary>
    private void ConfirmPassword_OnClick(object? sender, RoutedEventArgs eventArgs)
    {
        if (DataContext is not UpsdUsersConfigurationEditorViewModel viewModel) return;
        var result = viewModel.ConfirmPasswordChange(
            (NewPasswordBox.Text ?? string.Empty).AsSpan(),
            (ConfirmPasswordBox.Text ?? string.Empty).AsSpan());
        if (result.Succeeded) ClearPasswordBoxes();
    }

    private void CancelPassword_OnClick(object? sender, RoutedEventArgs eventArgs) => ClearPasswordBoxes();

    private void ClearPasswordBoxes()
    {
        NewPasswordBox.Clear();
        ConfirmPasswordBox.Clear();
    }
}
