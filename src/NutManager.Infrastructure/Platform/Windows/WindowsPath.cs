namespace NutManager.Infrastructure.Platform.Windows;

internal static class WindowsPath
{
    public static bool TryCanonicalize(string? path, out string canonical)
    {
        canonical = string.Empty;
        if (string.IsNullOrWhiteSpace(path)) return false;
        var value = path.Replace('/', '\\').Trim();
        if (value.Length < 3 || !char.IsAsciiLetter(value[0]) || value[1] != ':' || value[2] != '\\') return false;
        var segments = value[3..].Split('\\', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Any(segment => segment is "." or "..")) return false;
        canonical = char.ToUpperInvariant(value[0]) + ":\\" + string.Join('\\', segments);
        return true;
    }

    public static bool IsInside(string candidate, string directory)
    {
        if (!TryCanonicalize(candidate, out var path) || !TryCanonicalize(directory, out var root)) return false;
        return path.StartsWith(root.TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsSameOrInside(string candidate, string directory)
    {
        if (!TryCanonicalize(candidate, out var path) || !TryCanonicalize(directory, out var root)) return false;
        return string.Equals(path, root, StringComparison.OrdinalIgnoreCase) || IsInside(path, root);
    }
}
