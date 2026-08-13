using Avalonia.Controls;
using NutManager.App.Presentation.Controls;

namespace NutManager.App.Views;

public partial class OverviewPageView : UserControl
{
    /// <summary>Below this content width the decorative illustration yields its space to the data rows.</summary>
    private const double IllustrationMinimumWidth = 900d;

    public OverviewPageView()
    {
        InitializeComponent();

        // One-shot fade-in for the decorative illustration. The opacity transition declared in XAML
        // does the easing; there is no looping animation and no timer.
        Loaded += (_, _) =>
        {
            UpdateIllustration();
            StartPrimaryStatusHalo();
        };
        SizeChanged += (_, _) => UpdateIllustration();
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

    private void UpdateIllustration()
    {
        if (ActiveConfigurationIllustration is null) return;
        var fits = Bounds.Width >= IllustrationMinimumWidth;
        ActiveConfigurationIllustration.IsVisible = fits;
        ActiveConfigurationIllustration.Opacity = fits ? 1d : 0d;
    }
}
