namespace NutManager.App.ViewModels;

/// <summary>
/// Presentation-only cleanup for Windows event messages.
/// <para>
/// NUT registers its event source without a message DLL, so Windows cannot resolve the event text
/// and wraps the real payload in a long "The description for Event ID ... cannot be found" notice.
/// The useful content is the part after "part of the event:". This extracts it for display while
/// the original message is left untouched in the model.
/// </para>
/// </summary>
public static class NutEventMessagePresentation
{
    private const string PayloadMarker = "part of the event:";

    public static string Friendly(string? message)
    {
        if (string.IsNullOrWhiteSpace(message)) return string.Empty;

        var marker = message.IndexOf(PayloadMarker, StringComparison.OrdinalIgnoreCase);
        if (marker < 0) return message.Trim();

        var payload = message[(marker + PayloadMarker.Length)..].Trim();
        return Unquote(payload) is { Length: > 0 } cleaned ? cleaned : message.Trim();
    }

    private static string Unquote(string value)
    {
        var trimmed = value.Trim();
        if (trimmed.Length >= 2 && trimmed[0] == '\'' && trimmed[^1] == '\'') return trimmed[1..^1].Trim();
        if (trimmed.Length >= 2 && trimmed[0] == '"' && trimmed[^1] == '"') return trimmed[1..^1].Trim();
        return trimmed;
    }
}

/// <summary>Display row for one Windows event entry.</summary>
public sealed record WindowsEventRowViewModel(string Timestamp, string Level, string Provider, string Message);
