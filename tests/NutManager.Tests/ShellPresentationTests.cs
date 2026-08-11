using NutManager.App.Localization;
using NutManager.App.ViewModels;
using NutManager.Core.Models;
using Xunit;

namespace NutManager.Tests;

public sealed class ShellPresentationTests
{
    [Fact]
    public void BothOfficialCulturesContainAllRequiredKeys()
    {
        Assert.True(NutManagerLocalizer.HasRequiredKeys(UiLanguagePreference.PtBr));
        Assert.True(NutManagerLocalizer.HasRequiredKeys(UiLanguagePreference.EnUs));
    }

    [Fact]
    public void OfficialCulturesExposeExactlyTheSameSemanticKeys()
    {
        var portuguese = NutManagerLocalizer.GetAvailableKeys(UiLanguagePreference.PtBr);
        var english = NutManagerLocalizer.GetAvailableKeys(UiLanguagePreference.EnUs);

        Assert.Equal(portuguese.OrderBy(key => key), english.OrderBy(key => key));
        Assert.All(NutManagerLocalizer.RequiredKeys, key => Assert.Contains(key, portuguese));
    }

    [Fact]
    public void MissingResourceFallsBackDeterministicallyToItsKey() =>
        Assert.Equal("Missing.Key", new NutManagerLocalizer(UiLanguagePreference.EnUs).Get("Missing.Key"));

    [Fact]
    public void LocalizedNavigationUsesSemanticResources()
    {
        Assert.Equal("Visão geral", new NutManagerLocalizer(UiLanguagePreference.PtBr).Get("Nav.Overview"));
        Assert.Equal("Overview", new NutManagerLocalizer(UiLanguagePreference.EnUs).Get("Nav.Overview"));
    }

    [Fact]
    public void TechnicalNutTokensAreInvariant()
    {
        var portuguese = new NutManagerLocalizer(UiLanguagePreference.PtBr);
        var english = new NutManagerLocalizer(UiLanguagePreference.EnUs);

        Assert.Equal("ups.conf", "ups.conf");
        Assert.Equal("SFTP", "SFTP");
        Assert.Equal("MONITOR", "MONITOR");
        Assert.NotEqual(portuguese.Get("Nav.Settings"), english.Get("Nav.Settings"));
    }

    [Theory]
    [InlineData(1200, ShellLayoutState.Wide)]
    [InlineData(1199, ShellLayoutState.Medium)]
    [InlineData(860, ShellLayoutState.Medium)]
    [InlineData(859, ShellLayoutState.Compact)]
    public void LayoutBreakpointsAreDeterministic(double width, ShellLayoutState expected) =>
        Assert.Equal(expected, ShellPresentationMapper.LayoutFor(width));

    [Fact]
    public void OverlayDoesNotDestroySidebarPreference()
    {
        Assert.Equal(SidebarDisplayState.Overlay, ShellPresentationMapper.SidebarFor(ShellLayoutState.Compact, SidebarPreference.Expanded));
        Assert.Equal(SidebarDisplayState.Expanded, ShellPresentationMapper.SidebarFor(ShellLayoutState.Wide, SidebarPreference.Expanded));
    }

    [Theory]
    [InlineData(ConnectionState.Connected, DataFreshness.Fresh, ConnectionPresentationState.Healthy)]
    [InlineData(ConnectionState.Connecting, DataFreshness.Fresh, ConnectionPresentationState.Pending)]
    [InlineData(ConnectionState.Reconnecting, DataFreshness.Fresh, ConnectionPresentationState.Pending)]
    [InlineData(ConnectionState.Reconnecting, DataFreshness.Unavailable, ConnectionPresentationState.Pending)]
    [InlineData(ConnectionState.Connected, DataFreshness.Stale, ConnectionPresentationState.Warning)]
    [InlineData(ConnectionState.Disconnected, DataFreshness.Fresh, ConnectionPresentationState.Critical)]
    [InlineData(ConnectionState.ConnectionFailed, DataFreshness.Fresh, ConnectionPresentationState.Critical)]
    [InlineData(ConnectionState.ConnectionFailed, DataFreshness.Unavailable, ConnectionPresentationState.Critical)]
    [InlineData(ConnectionState.Disconnected, DataFreshness.Unavailable, ConnectionPresentationState.Critical)]
    public void ConnectionStateMapsToSemanticPresentation(ConnectionState state, DataFreshness freshness, ConnectionPresentationState expected) =>
        Assert.Equal(expected, ShellPresentationMapper.ConnectionFor(state, freshness, true));

    [Fact]
    public void MissingContextIsAlwaysUnavailable() =>
        Assert.Equal(ConnectionPresentationState.Unavailable, ShellPresentationMapper.ConnectionFor(ConnectionState.Connected, DataFreshness.Fresh, false));

    [Fact]
    public void ReviewDrawerIsHiddenWithoutContext() =>
        Assert.Equal(ReviewDrawerDisplayState.Hidden, ShellPresentationMapper.ReviewFor(ShellLayoutState.Wide, false, true));

    [Fact]
    public void ReviewDrawerUsesWideSpaceOrOverlayAccordingToLayout()
    {
        Assert.Equal(ReviewDrawerDisplayState.Expanded, ShellPresentationMapper.ReviewFor(ShellLayoutState.Wide, true, true));
        Assert.Equal(ReviewDrawerDisplayState.Collapsed, ShellPresentationMapper.ReviewFor(ShellLayoutState.Wide, true, false));
        Assert.Equal(ReviewDrawerDisplayState.Overlay, ShellPresentationMapper.ReviewFor(ShellLayoutState.Medium, true, false));
        Assert.Equal(ReviewDrawerDisplayState.Overlay, ShellPresentationMapper.ReviewFor(ShellLayoutState.Compact, true, true));
    }
}
