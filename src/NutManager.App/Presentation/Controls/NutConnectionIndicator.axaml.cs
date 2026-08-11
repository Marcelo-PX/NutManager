using Avalonia;
using Avalonia.Controls;

namespace NutManager.App.Presentation.Controls;

public partial class NutConnectionIndicator : UserControl
{
    public static readonly StyledProperty<string?> StatusTextProperty =
        AvaloniaProperty.Register<NutConnectionIndicator, string?>(nameof(StatusText));

    public static readonly StyledProperty<string?> DetailTextProperty =
        AvaloniaProperty.Register<NutConnectionIndicator, string?>(nameof(DetailText));

    public static readonly StyledProperty<string?> AccessibleTextProperty =
        AvaloniaProperty.Register<NutConnectionIndicator, string?>(nameof(AccessibleText));

    public static readonly StyledProperty<bool> IsHealthyProperty =
        AvaloniaProperty.Register<NutConnectionIndicator, bool>(nameof(IsHealthy));

    public static readonly StyledProperty<bool> IsPendingProperty =
        AvaloniaProperty.Register<NutConnectionIndicator, bool>(nameof(IsPending));

    public static readonly StyledProperty<bool> IsCriticalProperty =
        AvaloniaProperty.Register<NutConnectionIndicator, bool>(nameof(IsCritical));

    public static readonly StyledProperty<bool> IsUnavailableProperty =
        AvaloniaProperty.Register<NutConnectionIndicator, bool>(nameof(IsUnavailable));

    public NutConnectionIndicator() => InitializeComponent();

    public string? StatusText { get => GetValue(StatusTextProperty); set => SetValue(StatusTextProperty, value); }
    public string? DetailText { get => GetValue(DetailTextProperty); set => SetValue(DetailTextProperty, value); }
    public string? AccessibleText { get => GetValue(AccessibleTextProperty); set => SetValue(AccessibleTextProperty, value); }
    public bool IsHealthy { get => GetValue(IsHealthyProperty); set => SetValue(IsHealthyProperty, value); }
    public bool IsPending { get => GetValue(IsPendingProperty); set => SetValue(IsPendingProperty, value); }
    public bool IsCritical { get => GetValue(IsCriticalProperty); set => SetValue(IsCriticalProperty, value); }
    public bool IsUnavailable { get => GetValue(IsUnavailableProperty); set => SetValue(IsUnavailableProperty, value); }
}
