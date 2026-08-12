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

    public NutNavigationIcon() => InitializeComponent();

    public AppPage Kind { get => GetValue(KindProperty); set => SetValue(KindProperty, value); }

    public bool IsOverview => Kind == AppPage.Overview;
    public bool IsDevices => Kind == AppPage.Devices;
    public bool IsAdministration => Kind == AppPage.Administration;
    public bool IsDiagnostics => Kind == AppPage.Diagnostics;
    public bool IsSettings => Kind == AppPage.Settings;

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property != KindProperty) return;
        RaisePropertyChanged(IsOverviewProperty, default, default);
        RaisePropertyChanged(IsDevicesProperty, default, default);
        RaisePropertyChanged(IsAdministrationProperty, default, default);
        RaisePropertyChanged(IsDiagnosticsProperty, default, default);
        RaisePropertyChanged(IsSettingsProperty, default, default);
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
