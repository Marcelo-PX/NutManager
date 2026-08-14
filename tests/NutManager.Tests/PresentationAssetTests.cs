using System.Reflection;
using System.Text;
using NutManager.App.ViewModels;
using Xunit;

namespace NutManager.Tests;

/// <summary>
/// T27A presentation assets. These pin the two things that fail silently at runtime: a decorative
/// asset that was never packed into the assembly, and an icon key that a view references while the
/// shared dictionary no longer defines it. Both checks are offline and need no Avalonia instance.
/// </summary>
public sealed class PresentationAssetTests
{
    [Fact]
    public void ServerIllustrationIsPackedIntoTheApplicationAssembly()
    {
        Assert.Contains("assets/illustrations/server-security.png", PackedResources, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void PackedIllustrationCarriesRealPngContent()
    {
        var bytes = PackedResourceBytes;
        var magic = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };

        // The packed blob must contain an actual PNG stream, not just the file name.
        Assert.True(IndexOf(bytes, magic) >= 0, "No PNG signature found in the packed Avalonia resources.");
        Assert.True(bytes.Length > 20_000, "The packed resource blob is too small to contain the illustration.");
    }

    [Theory]
    // Navigation and shell glyphs.
    [InlineData("NutIconOverviewBase")]
    [InlineData("NutIconDevicesBase")]
    [InlineData("NutIconAdministrationBase")]
    [InlineData("NutIconAdministrationBadge")]
    [InlineData("NutIconAdministrationCheck")]
    [InlineData("NutIconGearBase")]
    [InlineData("NutIconDiagnosticsBase")]
    [InlineData("NutIconSettingsBase")]
    [InlineData("NutIconProfile")]
    [InlineData("NutIconSun")]
    [InlineData("NutIconMoon")]
    [InlineData("NutIconWindowMinimize")]
    [InlineData("NutIconWindowMaximize")]
    [InlineData("NutIconWindowRestore")]
    [InlineData("NutIconClose")]
    [InlineData("NutIconMenu")]
    // Metric and status glyphs used by the dashboard.
    [InlineData("NutIconBattery")]
    [InlineData("NutIconGauge")]
    [InlineData("NutIconRuntime")]
    [InlineData("NutIconInput")]
    [InlineData("NutIconOutput")]
    [InlineData("NutIconTemperature")]
    [InlineData("NutIconDriver")]
    [InlineData("NutIconShield")]
    [InlineData("NutIconConnection")]
    [InlineData("NutIconServer")]
    [InlineData("NutIconNetwork")]
    // Action glyphs referenced by the configuration and administration surfaces.
    [InlineData("NutIconRefresh")]
    [InlineData("NutIconAdd")]
    [InlineData("NutIconEdit")]
    [InlineData("NutIconDelete")]
    [InlineData("NutIconCheck")]
    [InlineData("NutIconReview")]
    [InlineData("NutIconPreview")]
    [InlineData("NutIconWarning")]
    [InlineData("NutIconInfo")]
    [InlineData("NutIconSuccess")]
    [InlineData("NutIconStart")]
    [InlineData("NutIconStop")]
    [InlineData("NutIconRestart")]
    [InlineData("NutIconService")]
    [InlineData("NutIconRemote")]
    [InlineData("NutIconFolder")]
    [InlineData("NutIconFile")]
    [InlineData("NutIconLogs")]
    [InlineData("NutIconTls")]
    [InlineData("NutIconPort")]
    [InlineData("NutIconGeneral")]
    [InlineData("NutIconUps")]
    [InlineData("NutIconChevronRight")]
    [InlineData("NutIconChevronLeft")]
    [InlineData("NutIconChevronDown")]
    [InlineData("NutIconForward")]
    [InlineData("NutIconCopy")]
    public void RequiredIconGeometryIsDefined(string key) =>
        Assert.Contains($"x:Key=\"{key}\"", IconDictionarySource, StringComparison.Ordinal);

    [Fact]
    public void IconDictionaryStaysPureVector()
    {
        // The shared icon dictionary must contain geometry only: no bitmaps, no icon fonts and no
        // pictographic characters standing in for a glyph.
        Assert.DoesNotContain(".png", IconDictionarySource, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("<Image", IconDictionarySource, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Segoe MDL2", IconDictionarySource, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Wingdings", IconDictionarySource, StringComparison.OrdinalIgnoreCase);
        Assert.All(IconDictionarySource, character => Assert.True(character < 0x2190,
            "Icon geometries must not embed pictographic or emoji characters."));
    }

    [Fact]
    public void OverviewShortcutGlyphsAreMutuallyExclusive()
    {
        // Each shortcut kind maps to exactly one flag so the view renders one glyph per row.
        foreach (var glyph in Enum.GetValues<OverviewShortcutGlyph>())
        {
            var shortcut = new OverviewShortcutViewModel("title", "description", glyph, new InertCommand());
            var flags = new[] { shortcut.IsConfiguration, shortcut.IsService, shortcut.IsDevices, shortcut.IsDiagnostics };
            Assert.Single(flags, flag => flag);
        }
    }

    private static string PackedResources => Encoding.UTF8.GetString(PackedResourceBytes);

    private static byte[] PackedResourceBytes => _packed ??= ReadPackedResources();
    private static byte[]? _packed;

    private static byte[] ReadPackedResources()
    {
        var assembly = typeof(OverviewShortcutViewModel).GetTypeInfo().Assembly;
        using var stream = assembly.GetManifestResourceStream("!AvaloniaResources")
            ?? throw new InvalidOperationException("The application assembly has no packed Avalonia resources.");
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        return buffer.ToArray();
    }

    private static string IconDictionarySource => _iconSource ??= ReadRepositoryFile(
        Path.Combine("src", "NutManager.App", "Presentation", "Themes", "NutIcons.axaml"));
    private static string? _iconSource;

    /// <summary>Reads a repository source file by walking up from the test assembly location.</summary>
    private static string ReadRepositoryFile(string relativePath)
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

    private static int IndexOf(byte[] haystack, byte[] needle)
    {
        for (var index = 0; index <= haystack.Length - needle.Length; index++)
        {
            var found = true;
            for (var offset = 0; offset < needle.Length && found; offset++)
                if (haystack[index + offset] != needle[offset]) found = false;
            if (found) return index;
        }

        return -1;
    }

    private sealed class InertCommand : System.Windows.Input.ICommand
    {
        public event EventHandler? CanExecuteChanged { add { } remove { } }
        public bool CanExecute(object? parameter) => true;
        public void Execute(object? parameter) { }
    }
}
