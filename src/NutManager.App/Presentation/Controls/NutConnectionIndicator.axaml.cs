using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace NutManager.App.Presentation.Controls;

public partial class NutConnectionIndicator : UserControl
{
    public static readonly StyledProperty<string?> StatusTextProperty =
        AvaloniaProperty.Register<NutConnectionIndicator, string?>(nameof(StatusText));

    public static readonly StyledProperty<string?> StateTextProperty =
        AvaloniaProperty.Register<NutConnectionIndicator, string?>(nameof(StateText));

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
    public string? StateText { get => GetValue(StateTextProperty); set => SetValue(StateTextProperty, value); }
    public string? DetailText { get => GetValue(DetailTextProperty); set => SetValue(DetailTextProperty, value); }
    public string? AccessibleText { get => GetValue(AccessibleTextProperty); set => SetValue(AccessibleTextProperty, value); }
    public bool IsHealthy { get => GetValue(IsHealthyProperty); set => SetValue(IsHealthyProperty, value); }
    public bool IsPending { get => GetValue(IsPendingProperty); set => SetValue(IsPendingProperty, value); }
    public bool IsCritical { get => GetValue(IsCriticalProperty); set => SetValue(IsCriticalProperty, value); }
    public bool IsUnavailable { get => GetValue(IsUnavailableProperty); set => SetValue(IsUnavailableProperty, value); }

    public bool HasStateText => !string.IsNullOrWhiteSpace(StateText);

    /// <summary>
    /// Resolves the state label colour from the current presentation flags. Colour is redundant
    /// with <see cref="StateText"/> so it never becomes the only carrier of connection meaning.
    /// </summary>
    public IBrush? StateBrush => this.FindResource(IsHealthy
        ? "NutHealthyBrush"
        : IsPending
            ? "NutWarningBrush"
            : IsCritical
                ? "NutCriticalBrush"
                : "NutUnavailableBrush") as IBrush;

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == StateTextProperty)
        {
            RaisePropertyChanged(HasStateTextProperty, default, default);
        }
        else if (change.Property == IsHealthyProperty || change.Property == IsPendingProperty ||
                 change.Property == IsCriticalProperty || change.Property == IsUnavailableProperty)
        {
            RaisePropertyChanged(StateBrushProperty, default, default);
        }
    }

    private static readonly DirectProperty<NutConnectionIndicator, bool> HasStateTextProperty =
        AvaloniaProperty.RegisterDirect<NutConnectionIndicator, bool>(nameof(HasStateText), owner => owner.HasStateText);

    private static readonly DirectProperty<NutConnectionIndicator, IBrush?> StateBrushProperty =
        AvaloniaProperty.RegisterDirect<NutConnectionIndicator, IBrush?>(nameof(StateBrush), owner => owner.StateBrush);
}
