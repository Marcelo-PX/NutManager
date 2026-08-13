using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using NutManager.App.ViewModels;

namespace NutManager.App.Views;

public partial class UpsmonConfigurationEditorView : UserControl
{
    public UpsmonConfigurationEditorView() => InitializeComponent();

    /// <summary>
    /// Adds a monitor with the credential typed into the two boxes. The value is read here, passed
    /// as spans, and both boxes are cleared straight away, so it never reaches a view-model
    /// property.
    /// </summary>
    private void ConfirmAddMonitor_OnClick(object? sender, RoutedEventArgs eventArgs)
    {
        if (DataContext is not UpsmonConfigurationEditorViewModel viewModel) return;
        var result = viewModel.ConfirmAddMonitor(
            (NewMonitorPassword.Text ?? string.Empty).AsSpan(),
            (ConfirmMonitorPassword.Text ?? string.Empty).AsSpan());
        if (result.Succeeded) ClearAddMonitorBoxes();
    }

    private void CancelAddMonitor_OnClick(object? sender, RoutedEventArgs eventArgs) => ClearAddMonitorBoxes();

    private void ClearAddMonitorBoxes()
    {
        NewMonitorPassword.Clear();
        ConfirmMonitorPassword.Clear();
    }

    /// <summary>
    /// Replaces one monitor's credential. The two boxes live inside the row's own template, so they
    /// are located from the clicked button rather than by name, and cleared once handed over.
    /// </summary>
    private void ConfirmMonitorPassword_OnClick(object? sender, RoutedEventArgs eventArgs)
    {
        if (sender is not Control control || control.DataContext is not UpsmonMonitorRowViewModel row) return;
        var boxes = FindPasswordBoxes(control);
        if (boxes.New is null || boxes.Confirm is null) return;

        var result = row.ConfirmPasswordChange(
            (boxes.New.Text ?? string.Empty).AsSpan(),
            (boxes.Confirm.Text ?? string.Empty).AsSpan());
        if (result.Succeeded)
        {
            boxes.New.Clear();
            boxes.Confirm.Clear();
        }
    }

    private void CancelMonitorPassword_OnClick(object? sender, RoutedEventArgs eventArgs)
    {
        if (sender is not Control control) return;
        var boxes = FindPasswordBoxes(control);
        boxes.New?.Clear();
        boxes.Confirm?.Clear();
    }

    /// <summary>Enter confirms, so the credential does not have to be re-typed after a mistake.</summary>
    private void MonitorPassword_OnKeyDown(object? sender, KeyEventArgs eventArgs)
    {
        if (eventArgs.Key == Key.Enter) ConfirmMonitorPassword_OnClick(sender, eventArgs);
    }

    private static (TextBox? New, TextBox? Confirm) FindPasswordBoxes(Control control)
    {
        var container = control.FindAncestorOfType<WrapPanel>();
        if (container is null) return (null, null);
        var boxes = container.GetVisualDescendants().OfType<TextBox>().ToArray();
        return (boxes.FirstOrDefault(box => Equals(box.Tag, "new")),
            boxes.FirstOrDefault(box => Equals(box.Tag, "confirm")));
    }
}
