using Avalonia.Controls;

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
        Loaded += (_, _) => UpdateIllustration();
        SizeChanged += (_, _) => UpdateIllustration();
    }

    private void UpdateIllustration()
    {
        if (ActiveConfigurationIllustration is null) return;
        var fits = Bounds.Width >= IllustrationMinimumWidth;
        ActiveConfigurationIllustration.IsVisible = fits;
        ActiveConfigurationIllustration.Opacity = fits ? 1d : 0d;
    }
}
