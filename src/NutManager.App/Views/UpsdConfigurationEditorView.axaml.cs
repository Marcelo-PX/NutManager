using Avalonia.Controls;
using Avalonia.Interactivity;
using NutManager.App.ViewModels;

namespace NutManager.App.Views;

public partial class UpsdConfigurationEditorView : UserControl
{
    public UpsdConfigurationEditorView() => InitializeComponent();

    private void ReplaceCertificateIdentity_OnClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not UpsdConfigurationEditorViewModel editor) return;
        var identity = CertificateIdentityBox.Text ?? string.Empty;
        var password = CertificatePasswordBox.Text ?? string.Empty;
        try { editor.ReplaceCertificateIdentity(identity, password.AsSpan()); }
        finally { CertificatePasswordBox.Text = string.Empty; }
    }

    private void RemoveCertificateIdentity_OnClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is UpsdConfigurationEditorViewModel editor) editor.RemoveCertificateIdentity();
    }
}
