using Avalonia.Controls;
using NutManager.App.Presentation.Controls;

namespace NutManager.App.Views;

public partial class OverviewPageView : UserControl
{
    /// <summary>Below this content width the decorative illustration yields its space to the data rows.</summary>
    private const double IllustrationMinimumWidth = 1180d;
    private const double TwoColumnMinimumWidth = 760d;

    public OverviewPageView()
    {
        InitializeComponent();

        // One-shot fade-in for the decorative illustration. The opacity transition declared in XAML
        // does the easing; there is no looping animation and no timer.
        Loaded += (_, _) =>
        {
            UpdateActiveConfigurationLayout();
            StartPrimaryStatusHalo();
        };
        SizeChanged += (_, _) => UpdateActiveConfigurationLayout();
    }

    /// <summary>
    /// Breathes the halo behind the primary status token. It is the page's only continuous
    /// animation and runs on the compositor, so the dashboard reads as live without the UI thread
    /// doing anything per frame. The badge itself is untouched, so nothing on the row shifts.
    /// </summary>
    private void StartPrimaryStatusHalo()
    {
        if (PrimaryStatusHalo is null) return;
        NutIconMotion.Glow(PrimaryStatusHalo, 0.10, 0.30, TimeSpan.FromSeconds(2.6));
    }

    private void UpdateActiveConfigurationLayout()
    {
        if (ActiveConfigurationLayout is null ||
            ActiveProfileRows is null ||
            ActiveConnectivityRows is null ||
            ActiveConfigurationIllustration is null)
        {
            return;
        }

        var fitsIllustration = Bounds.Width >= IllustrationMinimumWidth;
        var fitsTwoColumns = Bounds.Width >= TwoColumnMinimumWidth;

        ActiveConfigurationLayout.ColumnDefinitions = new ColumnDefinitions(
            fitsIllustration ? "*,*,Auto" : fitsTwoColumns ? "*,*" : "*");
        ActiveConfigurationLayout.RowDefinitions = new RowDefinitions(fitsTwoColumns ? "Auto" : "Auto,Auto");
        Grid.SetColumn(ActiveConnectivityRows, fitsTwoColumns ? 1 : 0);
        Grid.SetRow(ActiveConnectivityRows, fitsTwoColumns ? 0 : 1);
        Grid.SetColumn(ActiveConfigurationIllustration, 2);
        Grid.SetRow(ActiveConfigurationIllustration, 0);
        ActiveConfigurationIllustration.IsVisible = fitsIllustration;
        ActiveConfigurationIllustration.Opacity = fitsIllustration ? 1d : 0d;
    }
}
