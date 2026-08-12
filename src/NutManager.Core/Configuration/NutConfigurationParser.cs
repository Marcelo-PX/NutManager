namespace NutManager.Core.Configuration;

public sealed class NutConfigurationParser
{
    public NutConfigurationDocument Parse(NutConfigurationFileKind fileKind, string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        var nodes = new List<NutConfigurationNode>();
        string? currentSection = null;
        var offset = 0;
        while (offset < text.Length)
        {
            var lineStart = offset;
            while (offset < text.Length && text[offset] is not '\r' and not '\n')
            {
                offset++;
            }

            var rawText = text[lineStart..offset];
            var lineEnding = ReadLineEnding(text, ref offset);
            var node = ParseLine(fileKind, rawText, lineEnding, currentSection);
            if (node is NutSectionNode section)
            {
                currentSection = section.Name;
            }

            nodes.Add(node);
        }

        return new NutConfigurationDocument(fileKind, text, nodes, Array.Empty<NutConfigurationParseDiagnostic>());
    }

    private static string ReadLineEnding(string text, ref int offset)
    {
        if (offset >= text.Length)
        {
            return string.Empty;
        }

        if (text[offset] == '\r' && offset + 1 < text.Length && text[offset + 1] == '\n')
        {
            offset += 2;
            return "\r\n";
        }

        return text[offset++].ToString();
    }

    private static NutConfigurationNode ParseLine(
        NutConfigurationFileKind fileKind,
        string rawText,
        string lineEnding,
        string? currentSection)
    {
        if (string.IsNullOrWhiteSpace(rawText) || rawText.TrimStart().StartsWith('#'))
        {
            return new NutRawNode(rawText, lineEnding);
        }

        if (AllowsSections(fileKind) && TryParseSection(rawText, out var sectionName))
        {
            return new NutSectionNode(rawText, lineEnding, sectionName);
        }

        if (AllowsAssignments(fileKind) && TryParseAssignment(rawText, lineEnding, currentSection, fileKind, out var assignment))
        {
            return assignment;
        }

        if (AllowsDirectives(fileKind) && TryParseDirective(rawText, lineEnding, currentSection, fileKind, out var directive))
        {
            return directive;
        }

        return new NutRawNode(rawText, lineEnding);
    }

    private static bool AllowsSections(NutConfigurationFileKind fileKind) =>
        fileKind is NutConfigurationFileKind.UpsConf or NutConfigurationFileKind.UpsdUsers;

    private static bool AllowsAssignments(NutConfigurationFileKind fileKind) =>
        fileKind is NutConfigurationFileKind.NutConf or NutConfigurationFileKind.UpsConf or NutConfigurationFileKind.UpsdUsers;

    private static bool AllowsDirectives(NutConfigurationFileKind fileKind) =>
        fileKind is NutConfigurationFileKind.UpsConf or NutConfigurationFileKind.UpsdConf or NutConfigurationFileKind.UpsdUsers or NutConfigurationFileKind.UpsmonConf;

    private static bool TryParseSection(string rawText, out string sectionName)
    {
        var trimmed = rawText.Trim();
        if (trimmed.Length >= 3 && trimmed[0] == '[' && trimmed[^1] == ']')
        {
            sectionName = trimmed[1..^1].Trim();
            return sectionName.Length > 0;
        }

        sectionName = string.Empty;
        return false;
    }

    private static bool TryParseAssignment(
        string rawText,
        string lineEnding,
        string? currentSection,
        NutConfigurationFileKind fileKind,
        out NutConfigurationAssignmentNode assignment)
    {
        var equalsIndex = rawText.IndexOf('=');
        if (equalsIndex <= 0)
        {
            assignment = null!;
            return false;
        }

        var name = rawText[..equalsIndex].Trim();
        if (name.Length == 0 || name.Any(char.IsWhiteSpace))
        {
            assignment = null!;
            return false;
        }

        if (fileKind == NutConfigurationFileKind.NutConf && rawText[..equalsIndex].Any(char.IsWhiteSpace))
        {
            assignment = null!;
            return false;
        }

        var afterEquals = rawText[(equalsIndex + 1)..];
        var leadingWhitespaceLength = afterEquals.Length - afterEquals.TrimStart().Length;
        var remaining = afterEquals[leadingWhitespaceLength..];
        var value = remaining.TrimEnd();
        var trailingWhitespace = remaining[value.Length..];
        char? quote = value.Length >= 2 && value[0] == value[^1] && value[0] is '\'' or '"' && !IsEscaped(value, value.Length - 1)
            ? value[0]
            : null;

        assignment = new NutConfigurationAssignmentNode(
            rawText,
            lineEnding,
            name,
            value,
            rawText[..(equalsIndex + 1)] + afterEquals[..leadingWhitespaceLength],
            trailingWhitespace,
            quote,
            currentSection,
            IsSensitiveAssignment(fileKind, name));
        return true;
    }

    private static bool IsSensitiveAssignment(NutConfigurationFileKind fileKind, string name) =>
        fileKind == NutConfigurationFileKind.UpsdUsers && string.Equals(name, "password", StringComparison.OrdinalIgnoreCase) ||
        fileKind == NutConfigurationFileKind.UpsConf && name is not null &&
        (string.Equals(name, "community", StringComparison.OrdinalIgnoreCase) ||
         string.Equals(name, "authPassword", StringComparison.OrdinalIgnoreCase) ||
         string.Equals(name, "privPassword", StringComparison.OrdinalIgnoreCase) ||
         string.Equals(name, "password", StringComparison.OrdinalIgnoreCase));

    private static bool TryParseDirective(
        string rawText,
        string lineEnding,
        string? currentSection,
        NutConfigurationFileKind fileKind,
        out NutConfigurationDirectiveNode directive)
    {
        var nameStart = 0;
        while (nameStart < rawText.Length && char.IsWhiteSpace(rawText[nameStart]))
        {
            nameStart++;
        }

        var nameEnd = nameStart;
        while (nameEnd < rawText.Length && !char.IsWhiteSpace(rawText[nameEnd]))
        {
            nameEnd++;
        }

        if (nameStart == nameEnd)
        {
            directive = null!;
            return false;
        }

        var argumentsStart = nameEnd;
        while (argumentsStart < rawText.Length && char.IsWhiteSpace(rawText[argumentsStart]))
        {
            argumentsStart++;
        }

        var remaining = rawText[argumentsStart..];
        var arguments = remaining.TrimEnd();
        var trailingWhitespace = remaining[arguments.Length..];
        var name = rawText[nameStart..nameEnd];
        if (fileKind == NutConfigurationFileKind.UpsConf && !IsKnownUpsConfPresenceFlag(name, arguments))
        {
            directive = null!;
            return false;
        }
        directive = new NutConfigurationDirectiveNode(
            rawText,
            lineEnding,
            name,
            arguments,
            rawText[..nameStart],
            rawText[nameEnd..argumentsStart],
            trailingWhitespace,
            currentSection,
            (fileKind == NutConfigurationFileKind.UpsmonConf && string.Equals(name, "MONITOR", StringComparison.OrdinalIgnoreCase)) ||
            (fileKind == NutConfigurationFileKind.UpsdConf && string.Equals(name, "CERTIDENT", StringComparison.OrdinalIgnoreCase)));
        return true;
    }

    private static bool IsKnownUpsConfPresenceFlag(string name, string arguments) =>
        arguments.Length == 0 && name.ToLowerInvariant() is
            "ignorelb" or "battery_voltage_reports_one_pack" or "pollonly" or "winhid";

    private static bool IsEscaped(string value, int index)
    {
        var backslashCount = 0;
        for (var cursor = index - 1; cursor >= 0 && value[cursor] == '\\'; cursor--)
        {
            backslashCount++;
        }

        return backslashCount % 2 != 0;
    }
}
