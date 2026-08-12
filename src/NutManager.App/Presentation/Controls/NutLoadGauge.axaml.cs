using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace NutManager.App.Presentation.Controls;

/// <summary>
/// Read-only semicircular gauge for a bounded percentage. It never invents a reading: when
/// <see cref="Percent"/> is null the value arc is hidden and the caller supplies an unavailable label.
/// </summary>
public partial class NutLoadGauge : UserControl
{
    public static readonly StyledProperty<double?> PercentProperty =
        AvaloniaProperty.Register<NutLoadGauge, double?>(nameof(Percent));

    public static readonly StyledProperty<string?> DisplayTextProperty =
        AvaloniaProperty.Register<NutLoadGauge, string?>(nameof(DisplayText));

    public static readonly StyledProperty<double> DiameterProperty =
        AvaloniaProperty.Register<NutLoadGauge, double>(nameof(Diameter), 112d);

    public static readonly StyledProperty<double> ThicknessProperty =
        AvaloniaProperty.Register<NutLoadGauge, double>(nameof(Thickness), 10d);

    public NutLoadGauge() => InitializeComponent();

    public double? Percent { get => GetValue(PercentProperty); set => SetValue(PercentProperty, value); }
    public string? DisplayText { get => GetValue(DisplayTextProperty); set => SetValue(DisplayTextProperty, value); }
    public double Diameter { get => GetValue(DiameterProperty); set => SetValue(DiameterProperty, value); }
    public double Thickness { get => GetValue(ThicknessProperty); set => SetValue(ThicknessProperty, value); }

    public bool HasValue => Percent is not null;

    /// <summary>Half the diameter plus room for the centred readout under the arc.</summary>
    public double GaugeHeight => Diameter / 2 + 44;

    public double SweepAngle => Percent is not { } percent ? 0d : Math.Clamp(percent, 0d, 100d) * 1.8d;

    /// <summary>
    /// Load severity thresholds are presentation-only emphasis; the numeric text remains the
    /// authoritative reading so colour is never the sole signal.
    /// </summary>
    public IBrush? ValueBrush => this.FindResource(Percent switch
    {
        null => "NutUnavailableBrush",
        >= 90 => "NutCriticalBrush",
        >= 75 => "NutWarningBrush",
        _ => "NutCyanBrush"
    }) as IBrush;

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == PercentProperty)
        {
            RaisePropertyChanged(HasValueProperty, default, default);
            RaisePropertyChanged(SweepAngleProperty, default, default);
            RaisePropertyChanged(ValueBrushProperty, default, default);
        }
        else if (change.Property == DiameterProperty)
        {
            RaisePropertyChanged(GaugeHeightProperty, default, default);
        }
    }

    private static readonly DirectProperty<NutLoadGauge, bool> HasValueProperty =
        AvaloniaProperty.RegisterDirect<NutLoadGauge, bool>(nameof(HasValue), owner => owner.HasValue);

    private static readonly DirectProperty<NutLoadGauge, double> SweepAngleProperty =
        AvaloniaProperty.RegisterDirect<NutLoadGauge, double>(nameof(SweepAngle), owner => owner.SweepAngle);

    private static readonly DirectProperty<NutLoadGauge, double> GaugeHeightProperty =
        AvaloniaProperty.RegisterDirect<NutLoadGauge, double>(nameof(GaugeHeight), owner => owner.GaugeHeight);

    private static readonly DirectProperty<NutLoadGauge, IBrush?> ValueBrushProperty =
        AvaloniaProperty.RegisterDirect<NutLoadGauge, IBrush?>(nameof(ValueBrush), owner => owner.ValueBrush);
}
