using Avalonia;
using Avalonia.Controls;
using NutManager.App.ViewModels;

namespace NutManager.App.Presentation.Controls;

/// <summary>
/// Renders the layered glyph for one navigation destination. The kind is bound from the navigation
/// item, so the shell never repeats the per-page icon composition.
/// </summary>
public partial class NutNavigationIcon : UserControl
{
    public static readonly StyledProperty<AppPage> KindProperty =
        AvaloniaProperty.Register<NutNavigationIcon, AppPage>(nameof(Kind));

    /// <summary>Set by a style when the row is hovered or selected. Drives the looping motion.</summary>
    public static readonly StyledProperty<bool> IsActiveProperty =
        AvaloniaProperty.Register<NutNavigationIcon, bool>(nameof(IsActive));

    public NutNavigationIcon() => InitializeComponent();

    public AppPage Kind { get => GetValue(KindProperty); set => SetValue(KindProperty, value); }

    public bool IsActive { get => GetValue(IsActiveProperty); set => SetValue(IsActiveProperty, value); }

    public bool IsOverview => Kind == AppPage.Overview;
    public bool IsDevices => Kind == AppPage.Devices;
    public bool IsAdministration => Kind == AppPage.Administration;
    public bool IsDiagnostics => Kind == AppPage.Diagnostics;
    public bool IsSettings => Kind == AppPage.Settings;

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        ApplyMotion();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == IsActiveProperty)
        {
            ApplyMotion();
            return;
        }

        if (change.Property != KindProperty) return;
        RaisePropertyChanged(IsOverviewProperty, default, default);
        RaisePropertyChanged(IsDevicesProperty, default, default);
        RaisePropertyChanged(IsAdministrationProperty, default, default);
        RaisePropertyChanged(IsDiagnosticsProperty, default, default);
        RaisePropertyChanged(IsSettingsProperty, default, default);
        ApplyMotion();
    }

    /// <summary>
    /// Each destination moves in a way that describes what it opens, rather than the same wiggle
    /// applied five times. Only the layer that carries the meaning moves; the Fluent silhouette
    /// underneath is never displaced, so the icon stays readable throughout.
    /// </summary>
    private void ApplyMotion()
    {
        if (!IsActive)
        {
            foreach (var layer in new Visual[] { OverviewDetail, DevicesLedTop, DevicesLedBottom, AdministrationBase, AdministrationBadge, AdministrationCheck, GearBase, DiagnosticsDot })
            {
                NutIconMotion.Reset(layer, restingOpacity: 1);
            }

            return;
        }

        switch (Kind)
        {
            case AppPage.Overview:
                // The dashboard bar reads as a live figure.
                NutIconMotion.Breathe(OverviewDetail, new Size(24, 24), 1.18, TimeSpan.FromSeconds(1.9));
                break;

            case AppPage.Devices:
                // Two rack lights, out of step, the way real hardware blinks.
                NutIconMotion.Blink(DevicesLedTop, TimeSpan.FromSeconds(1.4), TimeSpan.Zero);
                NutIconMotion.Blink(DevicesLedBottom, TimeSpan.FromSeconds(1.4), TimeSpan.FromSeconds(0.7));
                break;

            case AppPage.Administration:
                // A short lift and verified-badge pop reinforce authorization without looping.
                NutIconMotion.NudgeOnce(AdministrationBase, -1.25, TimeSpan.FromMilliseconds(220));
                NutIconMotion.PopOnce(AdministrationBadge, new Size(24, 24), 1.16, TimeSpan.FromMilliseconds(220));
                NutIconMotion.PopOnce(AdministrationCheck, new Size(24, 24), 1.2, TimeSpan.FromMilliseconds(220));
                break;

            case AppPage.Diagnostics:
                // The reading travels the trace, left to right, like a monitor sweep.
                NutIconMotion.Sweep(DiagnosticsDot, -13, 0, TimeSpan.FromSeconds(1.8));
                break;

            case AppPage.Settings:
                NutIconMotion.Spin(GearBase, new Size(24, 24), TimeSpan.FromSeconds(7));
                break;
        }
    }

    private static readonly DirectProperty<NutNavigationIcon, bool> IsOverviewProperty =
        AvaloniaProperty.RegisterDirect<NutNavigationIcon, bool>(nameof(IsOverview), owner => owner.IsOverview);
    private static readonly DirectProperty<NutNavigationIcon, bool> IsDevicesProperty =
        AvaloniaProperty.RegisterDirect<NutNavigationIcon, bool>(nameof(IsDevices), owner => owner.IsDevices);
    private static readonly DirectProperty<NutNavigationIcon, bool> IsAdministrationProperty =
        AvaloniaProperty.RegisterDirect<NutNavigationIcon, bool>(nameof(IsAdministration), owner => owner.IsAdministration);
    private static readonly DirectProperty<NutNavigationIcon, bool> IsDiagnosticsProperty =
        AvaloniaProperty.RegisterDirect<NutNavigationIcon, bool>(nameof(IsDiagnostics), owner => owner.IsDiagnostics);
    private static readonly DirectProperty<NutNavigationIcon, bool> IsSettingsProperty =
        AvaloniaProperty.RegisterDirect<NutNavigationIcon, bool>(nameof(IsSettings), owner => owner.IsSettings);
}
