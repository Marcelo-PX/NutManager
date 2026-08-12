namespace NutManager.App.ViewModels;

/// <summary>
/// Presentation-only formatting for serial port values. Configuration keeps whatever NUT stores;
/// this only affects how the value is displayed, so <c>ups.conf</c> and the semantic model are
/// never rewritten for cosmetic reasons.
/// </summary>
public static class NutPortPresentation
{
    private const string DevicePrefix = @"\\.\";

    /// <summary>
    /// Strips the Windows device-namespace prefix from a COM port so the UI reads <c>COM4</c>
    /// instead of <c>\\.\COM4</c>. Any value that is not a recognised COM device path is returned
    /// unchanged, so USB, HID and other transports keep their exact text.
    /// </summary>
    public static string Friendly(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return value ?? string.Empty;
        var trimmed = value.Trim();
        if (!trimmed.StartsWith(DevicePrefix, StringComparison.Ordinal)) return trimmed;

        var remainder = trimmed[DevicePrefix.Length..];
        return IsComPort(remainder) ? remainder : trimmed;
    }

    private static bool IsComPort(string candidate) =>
        candidate.Length > 3 &&
        candidate.StartsWith("COM", StringComparison.OrdinalIgnoreCase) &&
        candidate[3..].All(char.IsAsciiDigit);
}
