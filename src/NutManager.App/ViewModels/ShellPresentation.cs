using NutManager.Core.Models;

namespace NutManager.App.ViewModels;

public enum ShellLayoutState
{
    Wide,
    Medium,
    Compact
}

public enum SidebarDisplayState
{
    Expanded,
    Collapsed,
    Overlay
}

public enum ReviewDrawerDisplayState
{
    Hidden,
    Collapsed,
    Expanded,
    Overlay
}

public enum ConnectionPresentationState
{
    Healthy,
    Pending,
    Warning,
    Critical,
    Unavailable
}

public static class ShellPresentationMapper
{
    public static ShellLayoutState LayoutFor(double width) => width switch
    {
        >= 1200 => ShellLayoutState.Wide,
        >= 860 => ShellLayoutState.Medium,
        _ => ShellLayoutState.Compact
    };

    public static SidebarDisplayState SidebarFor(ShellLayoutState layout, SidebarPreference preference) =>
        layout == ShellLayoutState.Compact ? SidebarDisplayState.Overlay :
        layout == ShellLayoutState.Medium ? SidebarDisplayState.Collapsed :
        preference == SidebarPreference.Collapsed ? SidebarDisplayState.Collapsed : SidebarDisplayState.Expanded;

    public static ReviewDrawerDisplayState ReviewFor(ShellLayoutState layout, bool hasContext, bool isExpanded) =>
        !hasContext ? ReviewDrawerDisplayState.Hidden :
        layout == ShellLayoutState.Wide && isExpanded ? ReviewDrawerDisplayState.Expanded :
        layout == ShellLayoutState.Wide ? ReviewDrawerDisplayState.Collapsed : ReviewDrawerDisplayState.Overlay;

    public static ConnectionPresentationState ConnectionFor(ConnectionState state, DataFreshness freshness, bool hasContext) =>
        !hasContext || freshness == DataFreshness.Unavailable ? ConnectionPresentationState.Unavailable :
        state is ConnectionState.Connecting or ConnectionState.Reconnecting ? ConnectionPresentationState.Pending :
        state is ConnectionState.Disconnected or ConnectionState.ConnectionFailed ? ConnectionPresentationState.Critical :
        freshness == DataFreshness.Stale ? ConnectionPresentationState.Warning :
        state == ConnectionState.Connected && freshness == DataFreshness.Fresh ? ConnectionPresentationState.Healthy :
        ConnectionPresentationState.Unavailable;
}
