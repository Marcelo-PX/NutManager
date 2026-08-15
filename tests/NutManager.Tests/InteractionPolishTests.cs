using System.Text.RegularExpressions;
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
    [InlineData(NutLedState.Healthy, "NutLedHealthyBrush")]
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

    [Fact]
    public void TheHealthyLedHasItsOwnGreenSoTheSharedBadgeTokenIsUntouched()
    {
        var colors = Themes("NutColors.axaml");

        // Two separate tokens: the ball is lifted, badge text and borders are not.
        Assert.Contains("x:Key=\"NutLedHealthyBrush\" Color=\"#3BEF88\"", colors, StringComparison.Ordinal);
        Assert.Contains("x:Key=\"NutHealthyBrush\" Color=\"#4ADE80\"", colors, StringComparison.Ordinal);

        // The glow is a shadow, and a shadow colour cannot be bound to a resource, so the literal
        // has to be kept in step with the token by hand. A mismatch would light the ball in one
        // green and its halo in another.
        Assert.Contains("#F23BEF88", Controls("NutStatusLed.axaml"), StringComparison.Ordinal);
    }

    [Fact]
    public void TheLedCoreIsSmallerThanItsHaloConstraintAllowsAtThePeakOfAPulse()
    {
        var source = Controls("NutStatusLed.axaml");

        var core = LayerSize(source, "Core");
        var halo = LayerSize(source, "Halo");

        // The ball was reduced to read as a discreet indicator rather than a lamp.
        Assert.Equal(8, core);
        Assert.Equal(4, halo);

        // The invariant that keeps a dark rim from appearing: the shadow's source must still sit
        // inside the core at the widest point of the breath, not just at rest.
        const double peakScale = 1.45;
        Assert.True(halo * peakScale < core, $"halo {halo} scaled by {peakScale} must stay under core {core}");

        // Whole-pixel centring: an odd size straddles a half pixel and the ball looks crooked.
        Assert.All(new[] { core, halo }, size => Assert.Equal(0, size % 2));
    }

    private static int LayerSize(string axaml, string layer)
    {
        var index = axaml.IndexOf($"x:Name=\"{layer}\"", StringComparison.Ordinal);
        Assert.True(index >= 0, $"layer {layer} not found");
        var match = System.Text.RegularExpressions.Regex.Match(axaml[index..], @"Width=""(\d+)""");
        Assert.True(match.Success, $"layer {layer} has no literal Width");
        return int.Parse(match.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
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
    public void TheLedPulseRunsOnTheCompositorAndNoControlStyleLoops()
    {
        var led = Controls("NutStatusLed.axaml.cs");

        Assert.Contains("AnimationIterationBehavior.Forever", led, StringComparison.Ordinal);
        Assert.Contains("ElementComposition.GetElementVisual", led, StringComparison.Ordinal);
        // No timer drives it.
        Assert.DoesNotContain("DispatcherTimer", led, StringComparison.Ordinal);

        // Two things in the application loop on purpose: this light, and the window's acrylic pane.
        // Neither is a control style — a looping style would apply to every instance of a control
        // and is the thing worth forbidding.
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

        // Five destinations, five different gestures — not one wiggle applied five times. Each one
        // now moves the whole glyph, because the library gives one shape per icon and there are no
        // detail layers left to move on their own.
        Assert.Contains("NutIconMotion.Breathe(OverviewGlyph", icon, StringComparison.Ordinal);
        Assert.Contains("NutIconMotion.Glow(DevicesGlyph", icon, StringComparison.Ordinal);
        Assert.Contains("NutIconMotion.PopOnce(AdministrationGlyph", icon, StringComparison.Ordinal);
        Assert.Contains("NutIconMotion.Breathe(DiagnosticsGlyph", icon, StringComparison.Ordinal);
        Assert.Contains("case AppPage.Settings:", icon, StringComparison.Ordinal);
        Assert.Contains("NutIconMotion.Spin(SettingsGlyph", icon, StringComparison.Ordinal);

        // Overview and Diagnostics share a helper, so the distinction is the cadence: a dashboard
        // breathes slowly, a pulse trace beats. Equal periods would be the same gesture twice.
        Assert.Contains("Breathe(OverviewGlyph, GlyphSize, 1.08, TimeSpan.FromSeconds(1.9))", icon, StringComparison.Ordinal);
        Assert.Contains("Breathe(DiagnosticsGlyph, GlyphSize, 1.12, TimeSpan.FromSeconds(1.15))", icon, StringComparison.Ordinal);

        // Whole-glyph motion has to stay small; the amplitudes that suited a detail inside a
        // stationary silhouette read as the sidebar row twitching when applied to the icon itself.
        foreach (var overshoot in new[] { "1.2", "1.3", "1.4", "1.5" })
        {
            Assert.DoesNotContain($"GlyphSize, {overshoot}", icon, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void NoNavigationGlyphIsStillAssembledFromParts()
    {
        var control = Controls("NutNavigationIcon.axaml");
        var shell = Themes("NutShellStyles.axaml");

        // One Path per destination and nothing layered on top of it. A second shape inside a
        // destination's Grid is how the old per-part animation worked, and reintroducing one would
        // quietly take that destination back off the icon library.
        Assert.Equal(5, Regex.Matches(control, @"<shapes:Path\b").Count);
        Assert.Equal(5, Regex.Matches(control, @"Classes=""nut-nav-glyph""").Count);
        Assert.DoesNotContain("nut-nav-base", control, StringComparison.Ordinal);
        Assert.DoesNotContain("nut-nav-detail", control, StringComparison.Ordinal);

        // And the styles that lit those parts up are gone rather than left dangling.
        foreach (var deadClass in new[]
        {
            "nut-nav-detail", "nut-nav-overview-detail", "nut-nav-led-top", "nut-nav-led-bottom",
            "nut-nav-admin-badge", "nut-nav-admin-check", "nut-nav-pulse-dot", "nut-nav-hub",
            "nut-sun-rays", "nut-sun-core"
        })
        {
            Assert.DoesNotContain(deadClass, shell, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void GlassSurfacesBrightenOnHoverWithoutMovingOrReflowing()
    {
        var controls = Themes("NutControlStyles.axaml");
        var shell = Themes("NutShellStyles.axaml");

        // One hover response, shared by the surfaces that are actually made of glass: the pane
        // lightens and its edge comes up. Both halves are required — a surface that brightens with
        // no edge change reads as a colour bug rather than as light landing on a pane.
        foreach (var (source, selector) in new[]
        {
            (controls, "Border.nut-card:pointerover"),
            (shell, "Border.nut-file-rail:pointerover")
        })
        {
            var rule = source[source.IndexOf(selector, StringComparison.Ordinal)..];
            rule = rule[..rule.IndexOf("</Style>", StringComparison.Ordinal)];
            Assert.Contains("NutGlassSurfaceHoverBrush", rule, StringComparison.Ordinal);
            Assert.Contains("NutGlassBorderHoverBrush", rule, StringComparison.Ordinal);

            // Nothing that participates in layout, and no transform: a pane reacting under the
            // pointer must not shift the page or clip its own corner against a scroll viewer.
            foreach (var forbidden in new[] { "RenderTransform", "\"Width\"", "\"Height\"", "\"Padding\"", "\"Margin\"" })
            {
                Assert.DoesNotContain(forbidden, rule, StringComparison.Ordinal);
            }
        }

        // Short and eased, on both halves, so the change arrives rather than snapping on.
        foreach (var source in new[] { controls, shell })
        {
            Assert.Contains("<BrushTransition Property=\"Background\" Duration=\"0:0:0.18\" Easing=\"CubicEaseOut\" />", source, StringComparison.Ordinal);
            Assert.Contains("<BrushTransition Property=\"BorderBrush\" Duration=\"0:0:0.18\" Easing=\"CubicEaseOut\" />", source, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void RowHoverNeverOverridesSelectedPressedOrDisabled()
    {
        var shell = Themes("NutShellStyles.axaml");

        // Avalonia resolves equally matching setters by declaration order, so the only thing
        // keeping a hovered row from washing out the state of the row you are already on is that
        // the state rules come later in the file. Nothing about that is visible at the call site.
        foreach (var row in new[] { "nut-navigation-item", "nut-file-rail-item" })
        {
            var hover = shell.IndexOf($"Button.{row}:pointerover /template/ ContentPresenter", StringComparison.Ordinal);
            var pressed = shell.IndexOf($"Button.{row}:pressed /template/ ContentPresenter", StringComparison.Ordinal);
            var selected = shell.IndexOf($"Button.{row}.selected /template/ ContentPresenter", StringComparison.Ordinal);

            Assert.True(hover > 0, $"{row} has no hover rule.");
            Assert.True(pressed > hover, $"{row}: pressed must be declared after hover or hover wins.");
            Assert.True(selected > hover, $"{row}: selected must be declared after hover or hover wins.");
        }

        // Rows lighten with the glass family, one rung above the pane they sit on.
        Assert.Contains("NutGlassRowHoverBrush", shell, StringComparison.Ordinal);
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
        // Every glyph that can be animated is also reset when the pointer leaves.
        Assert.Contains("NutIconMotion.Reset(glyph", icon, StringComparison.Ordinal);
    }

    [Fact]
    public void AdministrationAndSettingsUseDistinctSemanticIconFamilies()
    {
        var icons = Themes("NutIcons.axaml");
        var control = Controls("NutNavigationIcon.axaml");

        // The two destinations a user is most likely to confuse must not resolve to the same
        // drawing. Administration is a person under a shield; Settings is a cog.
        Assert.Contains("NutIconAdministration", icons, StringComparison.Ordinal);
        Assert.Contains("NutIconAdministration", control, StringComparison.Ordinal);
        Assert.Contains("NutIconSettings", control, StringComparison.Ordinal);

        var library = Repository.Read(Path.Combine("src", "NutManager.App", "Presentation", "Themes", "NutIconLibrary.cs"));
        var administration = Regex.Match(library, @"\(""NutIconAdministration"", MaterialIconKind\.(\w+)\)").Groups[1].Value;
        var settings = Regex.Match(library, @"\(""NutIconSettings"", MaterialIconKind\.(\w+)\)").Groups[1].Value;
        Assert.NotEmpty(administration);
        Assert.NotEmpty(settings);
        Assert.NotEqual(administration, settings);
    }

    [Fact]
    public void ProfileFlyoutMatchesExpandedSidebarAndUsesSmoothEntrance()
    {
        var window = Repository.Read(Path.Combine("src", "NutManager.App", "MainWindow.axaml"));
        var metrics = Themes("NutMetrics.axaml");

        Assert.Contains("Width=\"{DynamicResource NutSidebarExpandedWidth}\"", window, StringComparison.Ordinal);
        Assert.Contains("NutSidebarExpandedWidth\">220", metrics, StringComparison.Ordinal);
        Assert.Contains("DoubleTransition Property=\"Opacity\" Duration=\"0:0:0.18\"", window, StringComparison.Ordinal);
        Assert.Contains("ThicknessTransition Property=\"Margin\" Duration=\"0:0:0.22\"", window, StringComparison.Ordinal);
        Assert.Contains("CubicEaseOut", window, StringComparison.Ordinal);
        Assert.DoesNotContain("<Border Width=\"300\"", window, StringComparison.Ordinal);
    }

    [Fact]
    public void ShellMotionUsesStateDrivenTransitionsWithoutFrameTimers()
    {
        var shell = Repository.Read(Path.Combine("src", "NutManager.App", "MainWindow.axaml"));

        Assert.Contains("DoubleTransition Property=\"Width\" Duration=\"0:0:0.22\" Easing=\"CubicEaseOut\"", shell, StringComparison.Ordinal);
        Assert.Contains("ThicknessTransition Property=\"Margin\" Duration=\"0:0:0.22\" Easing=\"CubicEaseOut\"", shell, StringComparison.Ordinal);
        Assert.Contains("DoubleTransition Property=\"Opacity\" Duration=\"0:0:0.18\" Easing=\"CubicEaseOut\"", shell, StringComparison.Ordinal);
        Assert.DoesNotContain("DispatcherTimer", shell, StringComparison.Ordinal);
    }

    [Fact]
    public void PageHeadersMatchTheNavigationIconSemantics()
    {
        var administration = Repository.Read(Path.Combine("src", "NutManager.App", "Views", "AdministrationPageView.axaml"));
        var settings = Repository.Read(Path.Combine("src", "NutManager.App", "Views", "SettingsPageView.axaml"));

        // Each page header shows the same icon its sidebar row does, so arriving on a page confirms
        // the row that was clicked.
        Assert.Contains("NutIconAdministration", administration, StringComparison.Ordinal);
        Assert.Contains("NutIconSettings", settings, StringComparison.Ordinal);
        Assert.DoesNotContain("NutIconGearBase", settings, StringComparison.Ordinal);
    }

    [Fact]
    public void ProfileQuickMenuBindsOnlyNonSecretPresentationMetadata()
    {
        var shell = Repository.Read(Path.Combine("src", "NutManager.App", "MainWindow.axaml"));
        var menu = shell[shell.IndexOf("x:Name=\"ProfileQuickMenuButton\"", StringComparison.Ordinal)..];
        menu = menu[..menu.IndexOf("<!-- ==================== Footer", StringComparison.Ordinal)];

        Assert.Contains("{Binding Name}", menu, StringComparison.Ordinal);
        Assert.Contains("{Binding Endpoint}", menu, StringComparison.Ordinal);
        Assert.Contains("{Binding ManagementMode}", menu, StringComparison.Ordinal);
        Assert.Contains("{Binding AccessMode}", menu, StringComparison.Ordinal);
        Assert.Contains("{Binding Transport}", menu, StringComparison.Ordinal);
        Assert.DoesNotContain("Password", menu, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Passphrase", menu, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Credential", menu, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("PrivateKey", menu, StringComparison.OrdinalIgnoreCase);
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
    /// <summary>
    /// The repository root, found by walking up until a known landmark appears. Needed by anything
    /// that has to enumerate the source tree rather than read one known file.
    /// </summary>
    public static string Root
    {
        get
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory is not null)
            {
                if (File.Exists(Path.Combine(directory.FullName, "NutManager.sln"))) return directory.FullName;
                directory = directory.Parent;
            }

            throw new DirectoryNotFoundException($"Could not locate the repository root from {AppContext.BaseDirectory}.");
        }
    }

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
