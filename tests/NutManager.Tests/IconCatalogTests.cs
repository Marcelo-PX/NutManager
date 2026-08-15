using System.Text.RegularExpressions;
using Xunit;

namespace NutManager.Tests;

/// <summary>
/// The icon catalog as an architectural boundary. What these defend is not any particular drawing
/// but the property that lets the drawing change: views ask for a semantic NutManager name, and one
/// file decides what that name looks like. A library may supply a glyph in future, but it has to
/// enter through the catalog rather than through the interface.
/// </summary>
public sealed class IconCatalogTests
{
    private static string Catalog() =>
        Repository.Read(Path.Combine("src", "NutManager.App", "Presentation", "Themes", "NutIcons.axaml"));

    private static IEnumerable<string> ProductionFiles()
    {
        var root = Path.Combine(Repository.Root, "src", "NutManager.App");
        return Directory.EnumerateFiles(root, "*.axaml", SearchOption.AllDirectories)
            .Concat(Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories))
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                && !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal));
    }

    private static HashSet<string> DefinedKeys() =>
        [.. Regex.Matches(Catalog(), @"x:Key=""(NutIcon\w+)""").Select(match => match.Groups[1].Value)];

    [Fact]
    public void EverySemanticNameUsedInTheApplicationIsDefinedInTheCatalog()
    {
        var defined = DefinedKeys();
        var missing = new SortedSet<string>();

        foreach (var file in ProductionFiles())
        {
            if (file.EndsWith("NutIcons.axaml", StringComparison.Ordinal)) continue;
            // Matched as a resource reference, which is how a view asks for an icon. A bare word
            // would also catch the motion helper and the icon-size metrics, which are not icons.
            foreach (Match match in Regex.Matches(File.ReadAllText(file), @"(?:Dynamic|Static)Resource\s+(NutIcon\w+)"))
            {
                if (!defined.Contains(match.Groups[1].Value))
                {
                    missing.Add(match.Groups[1].Value);
                }
            }
        }

        // A name that resolves to nothing renders as an empty box, which is easy to ship and hard
        // to notice.
        Assert.Empty(missing);
    }

    [Fact]
    public void NoSemanticNameIsDefinedTwice()
    {
        var keys = Regex.Matches(Catalog(), @"x:Key=""(NutIcon\w+)""").Select(match => match.Groups[1].Value).ToArray();

        // Avalonia takes the last definition, so a duplicate silently wins and the earlier drawing
        // becomes dead weight nobody notices.
        Assert.Equal(keys.Length, keys.Distinct().Count());
    }

    [Fact]
    public void EveryGlyphIsVectorGeometryRatherThanAFontOrAnImage()
    {
        var catalog = Catalog();

        Assert.Contains("StreamGeometry", catalog, StringComparison.Ordinal);
        foreach (var forbidden in new[] { "FontFamily", "FontIcon", ".ttf", ".otf", ".woff", "BitmapImage", ".png", ".ico" })
        {
            Assert.DoesNotContain(forbidden, catalog, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void NoIconIsFetchedWhileTheApplicationIsRunning()
    {
        var catalog = Catalog();

        // Geometry is packaged, so there is nothing to resolve over a network at start-up or later.
        // The only URLs in the file are the XAML namespaces and the attribution comment naming the
        // source the drawings were copied from, so the check is for a fetch, not for a string.
        foreach (var forbidden in new[] { "HttpClient", "WebRequest", "DownloadAsync", "fonts.googleapis", "cdn." })
        {
            Assert.DoesNotContain(forbidden, catalog, StringComparison.OrdinalIgnoreCase);
        }

        // And nothing in the application resolves an icon over the wire either.
        foreach (var file in ProductionFiles())
        {
            var text = File.ReadAllText(file);
            Assert.DoesNotContain("avares://http", text, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void NoViewReachesPastTheCatalogToAnIconLibrary()
    {
        // Exactly one file may know an icon library exists: the adapter that fills the catalog from
        // it. Everything else asks for a semantic name, which is what lets the drawing behind that
        // name be swapped again — or taken back in-house — without editing a single surface.
        const string adapter = "NutIconLibrary.cs";
        var offenders = new SortedSet<string>();
        var adapterFound = false;

        foreach (var file in ProductionFiles())
        {
            var text = File.ReadAllText(file);
            var referencesLibrary = new[] { "FluentIcons", "Material.Icons", "MaterialIcon", "Projektanker" }
                .Any(marker => text.Contains(marker, StringComparison.Ordinal));
            if (!referencesLibrary) continue;

            if (Path.GetFileName(file) == adapter)
            {
                adapterFound = true;
                continue;
            }

            offenders.Add(Path.GetFileName(file));
        }

        Assert.Empty(offenders);
        // And the adapter really is where it lives, so this test cannot pass by the library having
        // quietly disappeared.
        Assert.True(adapterFound, $"{adapter} should be the one place that references the icon library.");
    }

    [Fact]
    public void TheIconsWhosePartsAnimateAreStillSeparateShapes()
    {
        var defined = DefinedKeys();

        // These exist as separate geometries precisely so one piece can move while another does not:
        // the LEDs blink out of phase, the gear turns around a stationary hub, the dot sweeps across
        // its base. Collapsing any pair into one glyph — which is all a font can offer — would take
        // the animation with it.
        foreach (var pair in new[]
        {
            ("NutIconDevicesLedTop", "NutIconDevicesLedBottom"),
            ("NutIconGearBase", "NutIconGearHub"),
            ("NutIconDiagnosticsBase", "NutIconDiagnosticsDot"),
            ("NutIconOverviewBase", "NutIconOverviewDetail"),
            ("NutIconAdministrationBase", "NutIconAdministrationBadge"),
            ("NutIconSunCore", "NutIconSunRays")
        })
        {
            Assert.Contains(pair.Item1, defined);
            Assert.Contains(pair.Item2, defined);
        }
    }

    [Fact]
    public void TheVendoredGeometryIsCreditedAndItsReasonRecorded()
    {
        var notices = Repository.Read(Path.Combine("docs", "THIRD-PARTY-NOTICES.md"));

        Assert.Contains("Fluent UI System Icons", notices, StringComparison.Ordinal);
        Assert.Contains("MIT", notices, StringComparison.Ordinal);
        Assert.Contains("microsoft/fluentui-system-icons", notices, StringComparison.Ordinal);

        // The design system carries the decision, so a future reader finds why rather than guessing.
        var design = Repository.Read(Path.Combine("docs", "UI-DESIGN-SYSTEM.md"));
        Assert.Contains("Icon system policy", design, StringComparison.Ordinal);
        // And it no longer asserts a rule the project has replaced.
        Assert.DoesNotContain("no icon package is referenced", design, StringComparison.Ordinal);
    }
}
