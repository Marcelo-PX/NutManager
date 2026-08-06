using System.Collections.ObjectModel;

namespace NutManager.Core.Status;

public static class UpsStatusParser
{
    public static IReadOnlyList<UpsStatusToken> Parse(string? status)
    {
        if (string.IsNullOrWhiteSpace(status))
        {
            return Array.Empty<UpsStatusToken>();
        }

        var tokens = status
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
            .Select(ParseToken)
            .ToArray();

        return new ReadOnlyCollection<UpsStatusToken>(tokens);
    }

    private static UpsStatusToken ParseToken(string token) => token.ToUpperInvariant() switch
    {
        "OL" => new UpsStatusToken(token, StatusSemanticState.Online, StatusSeverity.Normal, true),
        "OB" => new UpsStatusToken(token, StatusSemanticState.OnBattery, StatusSeverity.Warning, true),
        "LB" => new UpsStatusToken(token, StatusSemanticState.LowBattery, StatusSeverity.Critical, true),
        "RB" => new UpsStatusToken(token, StatusSemanticState.ReplaceBattery, StatusSeverity.Warning, true),
        "CHRG" => new UpsStatusToken(token, StatusSemanticState.Charging, StatusSeverity.Informational, true),
        "DISCHRG" => new UpsStatusToken(token, StatusSemanticState.Discharging, StatusSeverity.Warning, true),
        "BYPASS" => new UpsStatusToken(token, StatusSemanticState.Bypass, StatusSeverity.Warning, true),
        "OFF" => new UpsStatusToken(token, StatusSemanticState.OutputOff, StatusSeverity.Critical, true),
        "OVER" => new UpsStatusToken(token, StatusSemanticState.Overloaded, StatusSeverity.Critical, true),
        "CAL" => new UpsStatusToken(token, StatusSemanticState.Calibration, StatusSeverity.Informational, true),
        _ => new UpsStatusToken(token, StatusSemanticState.Unknown, StatusSeverity.Unknown, false)
    };
}
