namespace NutManager.Core.Configuration;

/// <summary>Builds the existing read-only preview for both local and remote pipelines.</summary>
public static class NutConfigurationPreviewBuilder
{
    private const string RedactedText = "<redacted>";

    public static NutConfigurationChangePreview Build(
        NutConfigurationFileSnapshot snapshot,
        string candidateText,
        string candidateFingerprint)
    {
        var original = Lines(snapshot.Document.OriginalText);
        var candidate = Lines(candidateText);
        var originalSensitive = SensitiveLines(snapshot.FileKind, snapshot.Document.OriginalText);
        var candidateSensitive = SensitiveLines(snapshot.FileKind, candidateText);
        var common = LongestCommonSubsequence(original, candidate);
        var preview = new List<NutConfigurationPreviewLine>();
        var oldIndex = 0;
        var newIndex = 0;
        foreach (var (oldMatch, newMatch) in common.Append((original.Count, candidate.Count)))
        {
            var oldCount = oldMatch - oldIndex;
            var newCount = newMatch - newIndex;
            var count = Math.Max(oldCount, newCount);
            for (var offset = 0; offset < count; offset++)
            {
                var oldLineIndex = offset < oldCount ? oldIndex + offset : -1;
                var newLineIndex = offset < newCount ? newIndex + offset : -1;
                var sensitive = oldLineIndex >= 0 && originalSensitive.Contains(oldLineIndex) ||
                    newLineIndex >= 0 && candidateSensitive.Contains(newLineIndex);
                preview.Add(new(
                    newLineIndex >= 0 ? newLineIndex + 1 : oldLineIndex + 1,
                    sensitive ? RedactedText : oldLineIndex >= 0 ? original[oldLineIndex] : string.Empty,
                    sensitive ? RedactedText : newLineIndex >= 0 ? candidate[newLineIndex] : string.Empty,
                    sensitive));
            }
            oldIndex = oldMatch + 1;
            newIndex = newMatch + 1;
        }

        return new(snapshot.TargetPath, candidateFingerprint, preview);
    }

    private static HashSet<int> SensitiveLines(NutConfigurationFileKind fileKind, string text)
    {
        var document = new NutConfigurationParser().Parse(fileKind, text);
        return document.Nodes.Select((node, index) => (node, index)).Where(item => item.node switch
        {
            NutConfigurationAssignmentNode assignment => assignment.IsSensitive,
            NutConfigurationDirectiveNode directive => directive.IsSensitive,
            _ => false
        }).Select(item => item.index).ToHashSet();
    }

    private static IReadOnlyList<(int Old, int New)> LongestCommonSubsequence(IReadOnlyList<string> left, IReadOnlyList<string> right)
    {
        var lengths = new int[left.Count + 1, right.Count + 1];
        for (var i = left.Count - 1; i >= 0; i--)
            for (var j = right.Count - 1; j >= 0; j--)
                lengths[i, j] = string.Equals(left[i], right[j], StringComparison.Ordinal)
                    ? lengths[i + 1, j + 1] + 1
                    : Math.Max(lengths[i + 1, j], lengths[i, j + 1]);
        var matches = new List<(int, int)>();
        var x = 0;
        var y = 0;
        while (x < left.Count && y < right.Count)
        {
            if (string.Equals(left[x], right[y], StringComparison.Ordinal)) { matches.Add((x++, y++)); }
            else if (lengths[x + 1, y] >= lengths[x, y + 1]) x++;
            else y++;
        }
        return matches;
    }

    private static IReadOnlyList<string> Lines(string text)
    {
        var lines = new List<string>();
        var offset = 0;
        while (offset < text.Length)
        {
            var start = offset;
            while (offset < text.Length && text[offset] is not '\r' and not '\n') offset++;
            lines.Add(text[start..offset]);
            if (offset < text.Length && text[offset] == '\r' && offset + 1 < text.Length && text[offset + 1] == '\n') offset += 2;
            else if (offset < text.Length) offset++;
        }
        return lines;
    }
}
