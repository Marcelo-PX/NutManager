namespace NutManager.Core.Configuration;

public enum NutConfigurationMutationStatus
{
    Success,
    ValidationFailed,
    AmbiguousTarget,
    TargetNotFound,
    UnsupportedOperation,
    Conflict
}

public sealed record NutConfigurationMutationResult(NutConfigurationMutationStatus Status, string? Code = null)
{
    public bool Succeeded => Status == NutConfigurationMutationStatus.Success;
    public static NutConfigurationMutationResult Success() => new(NutConfigurationMutationStatus.Success);
}

/// <summary>
/// Intentional structural mutation boundary for the syntax-preserving T13 document.
/// It never exposes the backing node list for arbitrary editing.
/// </summary>
public sealed class NutConfigurationDocumentMutator
{
    private readonly NutConfigurationDocument _document;

    public NutConfigurationDocumentMutator(NutConfigurationDocument document) =>
        _document = document ?? throw new ArgumentNullException(nameof(document));

    public NutConfigurationMutationResult SetAssignment(string name, string value, string? section = null, int? occurrence = null) =>
        !ValidToken(name) || !ValidValue(value) || !ValidSectionReference(section)
            ? Invalid()
            : SetEntry(Assignments(name, section), occurrence, node => node.SetValue(value));

    public NutConfigurationMutationResult InsertAssignment(
        string name,
        string value,
        string? section = null,
        int insertionOrder = 0,
        IReadOnlyDictionary<string, int>? preferredOrder = null)
    {
        if (!AllowsAssignments() || !ValidToken(name) || !ValidValue(value) || !ValidSectionReference(section)) return Invalid();
        var lineEnding = PreferredLineEnding(section);
        var insertionIndex = FindInsertionIndex(section, name, insertionOrder, preferredOrder);
        var insertedLineEnding = insertionIndex == _document.Nodes.Count && !HasFinalNewline() ? string.Empty : lineEnding;
        var node = new NutConfigurationAssignmentNode(
            $"{name} = {RenderInsertedValue(value)}", insertedLineEnding, name, RenderInsertedValue(value), $"{name} = ", string.Empty,
            NeedsQuotes(value) ? '"' : null, section, IsSensitiveAssignment(name));
        InsertAt(insertionIndex, node, lineEnding);
        return NutConfigurationMutationResult.Success();
    }

    public NutConfigurationMutationResult RemoveAssignment(string name, string? section = null, int? occurrence = null) =>
        !ValidToken(name) || !ValidSectionReference(section) ? Invalid() : RemoveEntry(Assignments(name, section), occurrence);

    public NutConfigurationMutationResult SetDirectiveArguments(string name, string arguments, string? section = null, int? occurrence = null) =>
        !ValidToken(name) || !ValidValue(arguments) || !ValidSectionReference(section)
            ? Invalid()
            : SetEntry(Directives(name, section), occurrence, node => node.SetArguments(arguments));

    public NutConfigurationMutationResult InsertDirective(
        string name,
        string arguments,
        string? section = null,
        int insertionOrder = 0,
        IReadOnlyDictionary<string, int>? preferredOrder = null)
    {
        if (!AllowsDirectives() || !ValidToken(name) || !ValidValue(arguments) || !ValidSectionReference(section)) return Invalid();
        var lineEnding = PreferredLineEnding(section);
        var insertionIndex = FindInsertionIndex(section, name, insertionOrder, preferredOrder);
        var insertedLineEnding = insertionIndex == _document.Nodes.Count && !HasFinalNewline() ? string.Empty : lineEnding;
        var raw = arguments.Length == 0 ? name : $"{name} {arguments}";
        var node = new NutConfigurationDirectiveNode(raw, insertedLineEnding, name, arguments, string.Empty,
            arguments.Length == 0 ? string.Empty : " ", string.Empty, section, IsSensitiveDirective(name));
        InsertAt(insertionIndex, node, lineEnding);
        return NutConfigurationMutationResult.Success();
    }

    public NutConfigurationMutationResult RemoveDirective(string name, string? section = null, int? occurrence = null) =>
        !ValidToken(name) || !ValidSectionReference(section) ? Invalid() : RemoveEntry(Directives(name, section), occurrence);

    public NutConfigurationMutationResult AddSection(string name)
    {
        if (!AllowsSections() || !ValidSectionName(name)) return Invalid();
        if (_document.Sections.Any(section => Equal(section.Name, name))) return new(NutConfigurationMutationStatus.Conflict, "Section.Duplicate");
        var lineEnding = PreferredLineEnding(null);
        var node = new NutSectionNode($"[{name}]", HasFinalNewline() ? lineEnding : string.Empty, name);
        InsertAt(_document.Nodes.Count, node, lineEnding);
        return NutConfigurationMutationResult.Success();
    }

    public NutConfigurationMutationResult RemoveSection(string name)
    {
        var target = UniqueSection(name);
        if (target.Result is not null) return target.Result;
        var start = _document.IndexOf(target.Section!);
        var end = _document.Nodes.Count;
        for (var index = start + 1; index < _document.Nodes.Count; index++)
        {
            if (_document.Nodes[index] is NutSectionNode) { end = index; break; }
        }

        _document.RemoveRange(start, end - start);
        return NutConfigurationMutationResult.Success();
    }

    public NutConfigurationMutationResult RenameSection(string name, string newName)
    {
        if (!ValidSectionName(newName)) return Invalid();
        var target = UniqueSection(name);
        if (target.Result is not null) return target.Result;
        if (_document.Sections.Any(section => section != target.Section && Equal(section.Name, newName)))
            return new(NutConfigurationMutationStatus.Conflict, "Section.Duplicate");

        target.Section!.Rename(newName);
        var start = _document.IndexOf(target.Section);
        for (var index = start + 1; index < _document.Nodes.Count && _document.Nodes[index] is not NutSectionNode; index++)
        {
            if (_document.Nodes[index] is NutConfigurationAssignmentNode assignment) assignment.ReassignSection(newName);
            if (_document.Nodes[index] is NutConfigurationDirectiveNode directive) directive.ReassignSection(newName);
        }

        _document.MarkStructuralChange();
        return NutConfigurationMutationResult.Success();
    }

    public NutConfigurationMutationResult AddRepeatedAssignment(string name, string value, string? section = null, int insertionOrder = 0) =>
        InsertAssignment(name, value, section, insertionOrder);

    public NutConfigurationMutationResult EditRepeatedAssignment(string name, int occurrence, string value, string? section = null) =>
        SetAssignment(name, value, section, occurrence);

    public NutConfigurationMutationResult RemoveRepeatedAssignment(string name, int occurrence, string? section = null) =>
        RemoveAssignment(name, section, occurrence);

    public NutConfigurationMutationResult AddRepeatedDirective(string name, string arguments, string? section = null, int insertionOrder = 0) =>
        InsertDirective(name, arguments, section, insertionOrder);

    public NutConfigurationMutationResult EditRepeatedDirective(string name, int occurrence, string arguments, string? section = null) =>
        SetDirectiveArguments(name, arguments, section, occurrence);

    public NutConfigurationMutationResult RemoveRepeatedDirective(string name, int occurrence, string? section = null) =>
        RemoveDirective(name, section, occurrence);

    private NutConfigurationMutationResult SetEntry<T>(IReadOnlyList<T> nodes, int? occurrence, Action<T> apply) where T : NutConfigurationNode
    {
        var selected = Select(nodes, occurrence);
        if (selected.Result is not null) return selected.Result;
        apply(selected.Node!);
        return NutConfigurationMutationResult.Success();
    }

    private NutConfigurationMutationResult RemoveEntry<T>(IReadOnlyList<T> nodes, int? occurrence) where T : NutConfigurationNode
    {
        var selected = Select(nodes, occurrence);
        if (selected.Result is not null) return selected.Result;
        _document.RemoveAt(_document.IndexOf(selected.Node!));
        return NutConfigurationMutationResult.Success();
    }

    private static (T? Node, NutConfigurationMutationResult? Result) Select<T>(IReadOnlyList<T> nodes, int? occurrence) where T : NutConfigurationNode
    {
        if (nodes.Count == 0) return (null, new(NutConfigurationMutationStatus.TargetNotFound, "Target.NotFound"));
        if (occurrence is null && nodes.Count != 1) return (null, new(NutConfigurationMutationStatus.AmbiguousTarget, "Target.Ambiguous"));
        var index = occurrence ?? 0;
        return index < 0 || index >= nodes.Count
            ? (null, new(NutConfigurationMutationStatus.TargetNotFound, "Occurrence.NotFound"))
            : (nodes[index], null);
    }

    private (NutSectionNode? Section, NutConfigurationMutationResult? Result) UniqueSection(string name)
    {
        if (!ValidSectionName(name)) return (null, Invalid());
        var matches = _document.Sections.Where(section => Equal(section.Name, name)).ToArray();
        return matches.Length switch
        {
            0 => (null, new(NutConfigurationMutationStatus.TargetNotFound, "Section.NotFound")),
            1 => (matches[0], null),
            _ => (null, new(NutConfigurationMutationStatus.AmbiguousTarget, "Section.Ambiguous"))
        };
    }

    private IReadOnlyList<NutConfigurationAssignmentNode> Assignments(string name, string? section) =>
        _document.Nodes.OfType<NutConfigurationAssignmentNode>()
            .Where(node => Equal(node.Name, name) && EqualSection(node.SectionName, section)).ToArray();

    private IReadOnlyList<NutConfigurationDirectiveNode> Directives(string name, string? section) =>
        _document.Nodes.OfType<NutConfigurationDirectiveNode>()
            .Where(node => Equal(node.Name, name) && EqualSection(node.SectionName, section)).ToArray();

    private int FindInsertionIndex(
        string? section,
        string entryName,
        int insertionOrder,
        IReadOnlyDictionary<string, int>? preferredOrder)
    {
        var start = 0;
        var end = _document.Nodes.Count;
        if (section is null)
        {
            var firstSection = _document.Nodes.ToList().FindIndex(node => node is NutSectionNode);
            end = firstSection < 0 ? _document.Nodes.Count : firstSection;
        }
        else
        {
            var header = _document.Sections.Single(item => Equal(item.Name, section));
            start = _document.IndexOf(header) + 1;
            for (var index = start; index < _document.Nodes.Count; index++) if (_document.Nodes[index] is NutSectionNode) { end = index; break; }
        }

        if (preferredOrder is null || !preferredOrder.ContainsKey(entryName)) return end;
        var lastBefore = -1;
        for (var index = start; index < end; index++)
        {
            var name = _document.Nodes[index] switch
            {
                NutConfigurationAssignmentNode assignment => assignment.Name,
                NutConfigurationDirectiveNode directive => directive.Name,
                _ => null
            };
            if (name is null || !preferredOrder.TryGetValue(name, out var currentOrder)) continue;
            if (currentOrder > insertionOrder) return index;
            lastBefore = index;
        }
        if (lastBefore >= 0) return lastBefore + 1;
        return end;
    }

    private void InsertAt(int index, NutConfigurationNode node, string lineEnding)
    {
        if (index > 0 && _document.Nodes[index - 1].LineEnding.Length == 0)
            _document.Nodes[index - 1].SetLineEnding(lineEnding);
        _document.Insert(index, node);
    }

    private string PreferredLineEnding(string? section)
    {
        var scoped = _document.Nodes.Where(node => section is null || node switch
        {
            NutConfigurationAssignmentNode assignment => EqualSection(assignment.SectionName, section),
            NutConfigurationDirectiveNode directive => EqualSection(directive.SectionName, section),
            _ => false
        }).Select(node => node.LineEnding).FirstOrDefault(value => value.Length > 0);
        if (scoped is not null) return scoped;
        return _document.Nodes.Select(node => node.LineEnding).Where(value => value.Length > 0)
            .GroupBy(value => value, StringComparer.Ordinal).OrderByDescending(group => group.Count()).ThenBy(group => group.Key, StringComparer.Ordinal)
            .Select(group => group.Key).FirstOrDefault() ?? "\r\n";
    }

    private bool ValidSectionReference(string? section) => section is null ||
        _document.Sections.Count(item => Equal(item.Name, section)) == 1;
    private bool HasFinalNewline() => _document.Nodes.Count > 0 && _document.Nodes[^1].LineEnding.Length > 0;
    private bool AllowsSections() => _document.FileKind is NutConfigurationFileKind.UpsConf or NutConfigurationFileKind.UpsdUsers;
    private bool AllowsAssignments() => _document.FileKind is NutConfigurationFileKind.NutConf or NutConfigurationFileKind.UpsConf or NutConfigurationFileKind.UpsdUsers;
    private bool AllowsDirectives() => _document.FileKind is NutConfigurationFileKind.UpsConf or NutConfigurationFileKind.UpsdConf or NutConfigurationFileKind.UpsdUsers or NutConfigurationFileKind.UpsmonConf;
    private bool IsSensitiveAssignment(string name) =>
        _document.FileKind == NutConfigurationFileKind.UpsdUsers && Equal(name, "password") ||
        _document.FileKind == NutConfigurationFileKind.UpsConf &&
        (Equal(name, "community") || Equal(name, "authPassword") || Equal(name, "privPassword") || Equal(name, "password"));
    private bool IsSensitiveDirective(string name) => _document.FileKind == NutConfigurationFileKind.UpsmonConf && Equal(name, "MONITOR") ||
        _document.FileKind == NutConfigurationFileKind.UpsdConf && Equal(name, "CERTIDENT");
    private static bool ValidToken(string value) => !string.IsNullOrWhiteSpace(value) &&
        !value.Any(character => char.IsControl(character) || char.IsWhiteSpace(character) || character is '=' or '[' or ']');
    private static bool ValidValue(string value) => value is not null && !value.Any(character => character is '\r' or '\n' || char.IsControl(character));
    private static bool ValidSectionName(string value) => !string.IsNullOrWhiteSpace(value) &&
        !value.Any(character => character is '[' or ']' or '\r' or '\n' || char.IsControl(character));
    private static bool NeedsQuotes(string value) => value.Any(char.IsWhiteSpace) || value.Contains('#') || value.Contains('"') || value.Contains('\\');
    private static string RenderInsertedValue(string value) => !NeedsQuotes(value) ? value :
        $"\"{value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal)}\"";
    private static bool Equal(string left, string right) => string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
    private static bool EqualSection(string? left, string? right) => left is null && right is null || left is not null && right is not null && Equal(left, right);
    private static NutConfigurationMutationResult Invalid() => new(NutConfigurationMutationStatus.ValidationFailed, "Input.Invalid");
}
