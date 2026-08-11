using Avalonia;
using Avalonia.Controls;

namespace NutManager.App.Presentation.Controls;

public partial class NutStatusBadge : UserControl
{
    public static readonly StyledProperty<string?> TextProperty =
        AvaloniaProperty.Register<NutStatusBadge, string?>(nameof(Text));
    public static readonly StyledProperty<bool> IsHealthyProperty =
        AvaloniaProperty.Register<NutStatusBadge, bool>(nameof(IsHealthy));
    public static readonly StyledProperty<bool> IsWarningProperty =
        AvaloniaProperty.Register<NutStatusBadge, bool>(nameof(IsWarning));
    public static readonly StyledProperty<bool> IsCriticalProperty =
        AvaloniaProperty.Register<NutStatusBadge, bool>(nameof(IsCritical));
    public static readonly StyledProperty<bool> IsUnavailableProperty =
        AvaloniaProperty.Register<NutStatusBadge, bool>(nameof(IsUnavailable));

    public NutStatusBadge() => InitializeComponent();

    public string? Text { get => GetValue(TextProperty); set => SetValue(TextProperty, value); }
    public bool IsHealthy { get => GetValue(IsHealthyProperty); set => SetValue(IsHealthyProperty, value); }
    public bool IsWarning { get => GetValue(IsWarningProperty); set => SetValue(IsWarningProperty, value); }
    public bool IsCritical { get => GetValue(IsCriticalProperty); set => SetValue(IsCriticalProperty, value); }
    public bool IsUnavailable { get => GetValue(IsUnavailableProperty); set => SetValue(IsUnavailableProperty, value); }
}
