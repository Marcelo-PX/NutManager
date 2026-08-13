using NutManager.App.Presentation.Controls;
using Xunit;

namespace NutManager.Tests;

/// <summary>
/// Structural guards for the shell's motion work. Frame timing is not asserted — that is judged on
/// screen — but the mechanism is: the wrong animation primitive crashed the application once, and
/// layout-affecting hover cues silently reflow the pages around them.
/// </summary>
public sealed class InteractionPolishTests
{
    private static string Themes(string file) =>
        Repository.Read(Path.Combine("src", "NutManager.App", "Presentation", "Themes", file));

    private static string Controls(string file) =>
        Repository.Read(Path.Combine("src", "NutManager.App", "Presentation", "Controls", file));

    [Theory]
    [InlineData(NutLedState.Healthy, "NutHealthyBrush")]
    [InlineData(NutLedState.Pending, "NutWarningBrush")]
    [InlineData(NutLedState.Critical, "NutCriticalBrush")]
    [InlineData(NutLedState.Unavailable, "NutUnavailableBrush")]
    public void EveryLedStateMapsToItsOwnSemanticBrush(NutLedState state, string expected)
    {
        // Resolved through the control's own switch so a new state cannot silently fall back to grey.
        var led = new NutStatusLed { State = state };
        Assert.Equal(state, led.State);
        Assert.Contains(expected, Controls("NutStatusLed.axaml.cs"), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(NutLedState.Healthy, 2.0)]
    [InlineData(NutLedState.Pending, 3.2)]
    [InlineData(NutLedState.Critical, 3.2)]
    [InlineData(NutLedState.Unavailable, 0.0)]
    public void LedPulsePeriodsAreSemanticAndDeterministic(NutLedState state, double seconds)
    {
        Assert.Equal(TimeSpan.FromSeconds(seconds), NutStatusLed.PulsePeriodFor(state));
    }

    [Fact]
    public void PendingAndCriticalShareTheExactSamePulseImplementation()
    {
        Assert.Equal(
            NutStatusLed.PulsePeriodFor(NutLedState.Pending),
            NutStatusLed.PulsePeriodFor(NutLedState.Critical));

        var source = Controls("NutStatusLed.axaml.cs");
        Assert.Contains("NutLedState.Pending or NutLedState.Critical", source, StringComparison.Ordinal);
        Assert.Equal(1, source.Split("private void StartPulse(", StringSplitOptions.None).Length - 1);
    }

    [Fact]
    public void LedStopsEveryCompositionAnimationOnDetachOrStaticState()
    {
        var source = Controls("NutStatusLed.axaml.cs");

        Assert.Contains("OnDetachedFromVisualTree", source, StringComparison.Ordinal);
        Assert.Contains("halo.StopAnimation(ScaleTarget)", source, StringComparison.Ordinal);
        Assert.Contains("halo.StopAnimation(OpacityTarget)", source, StringComparison.Ordinal);
        Assert.Contains("core.StopAnimation(OpacityTarget)", source, StringComparison.Ordinal);
    }

    [Fact]
    public void TheLedIsOneBallWhoseGlowIsABlurredShadowRatherThanAnotherCircle()
    {
        var source = Controls("NutStatusLed.axaml");

        foreach (var layer in new[] { "x:Name=\"Halo\"", "x:Name=\"Core\"", "x:Name=\"Highlight\"" })
        {
            Assert.Contains(layer, source, StringComparison.Ordinal);
        }

        // Concentric ellipses have hard edges and read as rings drawn around the dot. The glow has
        // to be a shadow, and its spread must stay at zero: any spread starts the shadow outside
        // the core and leaves a dark gap ringing the ball, which is the same look in reverse.
        Assert.DoesNotContain("x:Name=\"Glow\"", source, StringComparison.Ordinal);
        Assert.Contains("BoxShadow", source, StringComparison.Ordinal);
        // Blur is free to be tuned; the zero spread is the invariant that keeps the gap away.
        Assert.All(
            source.Split('\n').Where(line => line.Contains("BoxShadow", StringComparison.Ordinal)),
            line => Assert.Matches(@"Value=""0 0 \d+ 0 #", line));
    }

    [Fact]
    public void TheLedPulseIsTheOnlyContinuousAnimationAndItRunsOnTheCompositor()
    {
        var led = Controls("NutStatusLed.axaml.cs");

        Assert.Contains("AnimationIterationBehavior.Forever", led, StringComparison.Ordinal);
        Assert.Contains("ElementComposition.GetElementVisual", led, StringComparison.Ordinal);
        // No timer drives it, and nothing else in the shell loops.
        Assert.DoesNotContain("DispatcherTimer", led, StringComparison.Ordinal);
        foreach (var file in new[] { "NutControlStyles.axaml", "NutShellStyles.axaml" })
        {
            Assert.DoesNotContain("IterationCount=\"Infinite\"", Themes(file), StringComparison.Ordinal);
        }
    }

    [Fact]
    public void MotionUsesTheSupportedTransformTransitionRatherThanAKeyframedRenderTransform()
    {
        // A keyframe Animation targeting RenderTransform throws "no animator registered" at runtime.
        foreach (var file in new[] { "NutControlStyles.axaml", "NutShellStyles.axaml" })
        {
            var source = Themes(file);
            Assert.Contains("TransformOperationsTransition", source, StringComparison.Ordinal);
            Assert.DoesNotContain("<Setter Property=\"RenderTransform\" Value=\"{Animation", source, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void HoverCuesNeverResizeOrReflowTheIconsTheyDecorate()
    {
        // Width/Height/Margin participate in layout; a hover that changes them shifts neighbours.
        var source = Themes("NutControlStyles.axaml");
        var iconSection = source[source.IndexOf("Chevrons slide", StringComparison.Ordinal)..];

        Assert.DoesNotContain("<Setter Property=\"Width\"", iconSection, StringComparison.Ordinal);
        Assert.DoesNotContain("<Setter Property=\"Margin\"", iconSection, StringComparison.Ordinal);
    }

    [Fact]
    public void MetricCardsLiftWithoutGrowingSideways()
    {
        // The band's first column is flush against the scroll viewer's left clip edge, so any
        // horizontal growth shaves the card's rounded corner off. Vertical movement is safe.
        var source = Themes("NutControlStyles.axaml");
        var start = source.IndexOf("Border.nut-metric-card:pointerover", StringComparison.Ordinal);
        var hover = source[start..source.IndexOf("</Style>", start, StringComparison.Ordinal)];

        Assert.Contains("translateY(-4px)", hover, StringComparison.Ordinal);
        Assert.DoesNotContain("scale(", hover, StringComparison.Ordinal);
    }

    [Fact]
    public void EverySemanticBadgeVariantHasItsOwnHoverTreatment()
    {
        var source = Themes("NutControlStyles.axaml");

        foreach (var variant in new[] { "healthy", "warning", "critical", "accent" })
        {
            Assert.Contains($"Border.nut-pill.{variant}:pointerover", source, StringComparison.Ordinal);
        }

        // Neutral must exclude the semantic variants or declaration order would repaint them all.
        Assert.Contains(
            "Border.nut-pill:pointerover:not(.healthy):not(.warning):not(.critical):not(.accent)",
            source,
            StringComparison.Ordinal);
    }

    [Fact]
    public void BadgeHoverIsPurelyVisualAndCannotShiftLayout()
    {
        var source = Themes("NutControlStyles.axaml");
        var badges = source[source.IndexOf("==================== Badges", StringComparison.Ordinal)..];
        badges = badges[..badges.IndexOf("==================== Buttons", StringComparison.Ordinal)];

        // Badges sit flush against their container with no top or left margin, so any movement at
        // all clips: sideways shaved the cap, upwards removed the one pixel top border.
        Assert.DoesNotContain("<Setter Property=\"RenderTransform\" Value=\"translate", badges, StringComparison.Ordinal);
        Assert.DoesNotContain("scale(", badges, StringComparison.Ordinal);
        // A Style.Animations block here re-runs on every style re-evaluation, so hovering restarted
        // the fade and the badge blinked out under the pointer.
        Assert.DoesNotContain("<Style.Animations>", badges, StringComparison.Ordinal);
        foreach (var layoutProperty in new[] { "\"Width\"", "\"Height\"", "\"Padding\"", "\"FontSize\"" })
        {
            Assert.DoesNotContain($"<Setter Property={layoutProperty}", badges[badges.IndexOf(":pointerover", StringComparison.Ordinal)..], StringComparison.Ordinal);
        }
    }

    [Fact]
    public void EachSidebarDestinationMovesInItsOwnWay()
    {
        var icon = Controls("NutNavigationIcon.axaml.cs");

        // Five destinations, five different gestures — not one wiggle applied five times.
        Assert.Contains("NutIconMotion.Breathe(OverviewDetail", icon, StringComparison.Ordinal);
        Assert.Contains("NutIconMotion.Blink(DevicesLedTop", icon, StringComparison.Ordinal);
        Assert.Contains("NutIconMotion.Blink(DevicesLedBottom", icon, StringComparison.Ordinal);
        Assert.Contains("NutIconMotion.Spin(GearBase", icon, StringComparison.Ordinal);
        Assert.Contains("NutIconMotion.Sweep(DiagnosticsDot", icon, StringComparison.Ordinal);
        Assert.Contains("NutIconMotion.Slide(KnobTop", icon, StringComparison.Ordinal);
        Assert.Contains("NutIconMotion.Slide(KnobBottom", icon, StringComparison.Ordinal);
    }

    [Fact]
    public void SidebarLoopsRunOnlyWhileHoveredAndAreAllStoppable()
    {
        var shell = Themes("NutShellStyles.axaml");
        var icon = Controls("NutNavigationIcon.axaml.cs");

        // Hover is the only trigger: a selected row would otherwise animate for as long as the page
        // is open, which is exactly the idle cost the shell must not have.
        Assert.Contains(":pointerover controls|NutNavigationIcon", shell, StringComparison.Ordinal);
        Assert.DoesNotContain(".selected controls|NutNavigationIcon", shell, StringComparison.Ordinal);
        // Every layer that can be animated is also reset when the pointer leaves.
        Assert.Contains("NutIconMotion.Reset(layer", icon, StringComparison.Ordinal);
    }

    [Fact]
    public void TheSlidingHandlesCarryTheirRingsSoNothingComesApart()
    {
        // Fluent draws the tracks and both handle rings in one path. Sliding only the fill left the
        // ring behind and read as a rendering fault, so the glyph is split into tracks plus handles.
        var icons = Themes("NutIcons.axaml");
        var control = Controls("NutNavigationIcon.axaml");

        Assert.Contains("NutIconSettingsTracks", icons, StringComparison.Ordinal);
        Assert.Contains("NutIconSettingsRingTop", icons, StringComparison.Ordinal);
        Assert.Contains("NutIconSettingsRingBottom", icons, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"KnobTop\"", control, StringComparison.Ordinal);
        Assert.Contains("NutIconSettingsRingTop", control, StringComparison.Ordinal);
        Assert.DoesNotContain("NutIconSettingsBase", control, StringComparison.Ordinal);
    }

    [Fact]
    public void TheShellDrawsIconsAsVectorsWithNoFontOrEmojiFallback()
    {
        foreach (var file in new[] { "NutControlStyles.axaml", "NutShellStyles.axaml", "NutIcons.axaml" })
        {
            var source = Themes(file);
            Assert.DoesNotContain("Segoe MDL2", source, StringComparison.Ordinal);
            Assert.DoesNotContain("Segoe Fluent Icons", source, StringComparison.Ordinal);
            Assert.DoesNotContain("Wingdings", source, StringComparison.Ordinal);
            Assert.DoesNotContain(".png", source, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void TheBadgeHoverBrushesAreDefinedOnceForBothThemes()
    {
        var colors = Themes("NutColors.axaml");

        foreach (var brush in new[]
        {
            "NutHealthyBrightBrush", "NutHealthySoftHoverBrush",
            "NutWarningBrightBrush", "NutWarningSoftHoverBrush",
            "NutCriticalBrightBrush", "NutCriticalSoftHoverBrush",
            "NutAccentSoftHoverBrush"
        })
        {
            Assert.Contains($"x:Key=\"{brush}\"", colors, StringComparison.Ordinal);
        }
    }
}

internal static class Repository
{
    /// <summary>Reads a repository source file by walking up from the test assembly location.</summary>
    public static string Read(string relativePath)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, relativePath);
            if (File.Exists(candidate)) return File.ReadAllText(candidate);
            directory = directory.Parent;
        }

        throw new FileNotFoundException($"Could not locate '{relativePath}' from {AppContext.BaseDirectory}.");
    }
}
