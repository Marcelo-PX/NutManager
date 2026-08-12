using System.Globalization;

namespace NutManager.App.ViewModels;

/// <summary>
/// Shared formatting for the reading timestamps surfaced by the UI.
/// <para>
/// Snapshots carry an unambiguous <see cref="DateTimeOffset"/>, which NUT readings produce in UTC.
/// Formatting one directly prints the wall-clock of its own offset, so a UTC instant was being
/// shown as if it were local time. Converting through <see cref="DateTimeOffset.ToLocalTime"/>
/// re-expresses the same instant in the machine's timezone; it is idempotent, so a value that is
/// already local is not shifted a second time. No timezone is hard-coded.
/// </para>
/// </summary>
public static class NutTimestampPresentation
{
    /// <summary>Formats an instant in the machine's local timezone using the current UI culture.</summary>
    public static string Local(DateTimeOffset instant, string format) =>
        instant.ToLocalTime().ToString(format, CultureInfo.CurrentCulture);

    /// <summary>Timezone-explicit overload used by tests so expectations never depend on the host.</summary>
    public static string In(DateTimeOffset instant, TimeZoneInfo timeZone, string format) =>
        TimeZoneInfo.ConvertTime(instant, timeZone).ToString(format, CultureInfo.CurrentCulture);
}
