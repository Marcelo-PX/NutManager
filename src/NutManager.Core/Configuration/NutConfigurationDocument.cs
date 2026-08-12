namespace NutManager.Core.Configuration;

public sealed class NutConfigurationDocument
{
    private readonly List<NutConfigurationNode> _nodes;
    private bool _hasStructuralChanges;

    internal NutConfigurationDocument(
        NutConfigurationFileKind fileKind,
        string originalText,
        IReadOnlyList<NutConfigurationNode> nodes,
        IReadOnlyList<NutConfigurationParseDiagnostic> diagnostics)
    {
        FileKind = fileKind;
        OriginalText = originalText;
        _nodes = nodes.ToList();
        Diagnostics = diagnostics;
    }

    public NutConfigurationFileKind FileKind { get; }

    public string OriginalText { get; }

    public IReadOnlyList<NutConfigurationNode> Nodes => _nodes;

    public IReadOnlyList<NutConfigurationParseDiagnostic> Diagnostics { get; }

    public bool IsModified => _hasStructuralChanges || Nodes.Any(node => node.IsModified);

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

    internal int IndexOf(NutConfigurationNode node) => _nodes.IndexOf(node);

    internal void Insert(int index, NutConfigurationNode node)
    {
        _nodes.Insert(index, node);
        node.MarkInserted();
        _hasStructuralChanges = true;
    }

    internal void RemoveAt(int index)
    {
        _nodes.RemoveAt(index);
        _hasStructuralChanges = true;
    }

    internal void RemoveRange(int index, int count)
    {
        _nodes.RemoveRange(index, count);
        _hasStructuralChanges = true;
    }

    internal void MarkStructuralChange() => _hasStructuralChanges = true;
}

public sealed record NutConfigurationParseDiagnostic(int LineNumber, string Message);
