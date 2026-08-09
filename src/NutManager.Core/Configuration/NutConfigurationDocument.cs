namespace NutManager.Core.Configuration;

public sealed class NutConfigurationDocument
{
    internal NutConfigurationDocument(
        NutConfigurationFileKind fileKind,
        string originalText,
        IReadOnlyList<NutConfigurationNode> nodes,
        IReadOnlyList<NutConfigurationParseDiagnostic> diagnostics)
    {
        FileKind = fileKind;
        OriginalText = originalText;
        Nodes = nodes;
        Diagnostics = diagnostics;
    }

    public NutConfigurationFileKind FileKind { get; }

    public string OriginalText { get; }

    public IReadOnlyList<NutConfigurationNode> Nodes { get; }

    public IReadOnlyList<NutConfigurationParseDiagnostic> Diagnostics { get; }

    public bool IsModified => Nodes.Any(node => node.IsModified);

    public IEnumerable<NutSectionNode> Sections => Nodes.OfType<NutSectionNode>();

    /// <summary>
    /// Name lookups use ordinal case-insensitive comparison by default. Passing a comparison
    /// explicitly lets callers opt into a different policy without changing stored text.
    /// </summary>
    public IEnumerable<NutSectionNode> FindSections(string name, StringComparison comparison = StringComparison.OrdinalIgnoreCase)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        return Sections.Where(section => string.Equals(section.Name, name, comparison));
    }

    public IEnumerable<NutConfigurationAssignmentNode> FindAssignments(
        string name,
        string? sectionName = null,
        StringComparison comparison = StringComparison.OrdinalIgnoreCase)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        return Nodes.OfType<NutConfigurationAssignmentNode>().Where(node =>
            string.Equals(node.Name, name, comparison) &&
            (sectionName is null || string.Equals(node.SectionName, sectionName, comparison)));
    }

    public IEnumerable<NutConfigurationDirectiveNode> FindDirectives(
        string name,
        string? sectionName = null,
        StringComparison comparison = StringComparison.OrdinalIgnoreCase)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        return Nodes.OfType<NutConfigurationDirectiveNode>().Where(node =>
            string.Equals(node.Name, name, comparison) &&
            (sectionName is null || string.Equals(node.SectionName, sectionName, comparison)));
    }

    public string Serialize() => string.Concat(Nodes.Select(node => node.Serialize()));
}

public sealed record NutConfigurationParseDiagnostic(int LineNumber, string Message);
