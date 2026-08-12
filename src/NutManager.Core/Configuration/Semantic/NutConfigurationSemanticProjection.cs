using NutManager.Core.Validation;

namespace NutManager.Core.Configuration.Semantic;

public sealed record NutConfigurationSemanticIssue(
    string Code,
    ValidationSeverity Severity,
    string ResourceKey,
    string SemanticTarget,
    string? Section = null,
    string? RowId = null);

public sealed record NutConfigurationSemanticField(
    NutConfigurationFieldDescriptor Descriptor,
    NutConfigurationSemanticState State,
    object? Value,
    string? Section,
    string? RowId,
    int? Occurrence,
    NutSensitiveFieldState? SensitiveState = null)
{
    /// <summary>Draft-lifetime identity that survives occurrence shifts after repeated-row mutations.</summary>
    public string? StableRowId { get; init; }
}

public sealed record NutConfigurationCustomParameter(
    string RowId,
    NutConfigurationEntryKind EntryKind,
    string Name,
    string? SafeValue,
    string? Section,
    int Occurrence,
    bool Sensitive,
    NutConfigurationSemanticState State = NutConfigurationSemanticState.CustomUnknown);

public sealed record NutConfigurationSemanticProjection(
    NutConfigurationFileKind FileKind,
    IReadOnlyList<NutConfigurationSemanticField> Fields,
    IReadOnlyList<NutConfigurationCustomParameter> CustomParameters,
    IReadOnlyList<NutConfigurationSemanticIssue> Issues);

public sealed class NutConfigurationSemanticProjector
{
    public NutConfigurationSemanticProjection Project(
        NutConfigurationDocument document,
        NutConfigurationFileSchema schema,
        NutConfigurationSemanticContext? context = null)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(schema);
        if (document.FileKind != schema.FileKind) throw new ArgumentException("The schema does not match the document file kind.", nameof(schema));
        context ??= new NutConfigurationSemanticContext();
        var fields = new List<NutConfigurationSemanticField>();
        var issues = new List<NutConfigurationSemanticIssue>();
        var knownNodes = new HashSet<NutConfigurationNode>(ReferenceEqualityComparer.Instance);

        foreach (var descriptor in schema.Fields)
        {
            if (descriptor.Scope == NutConfigurationFieldScope.Repeated)
            {
                var repeatedNodes = MatchingNodes(document, descriptor, null).ToArray();
                foreach (var node in repeatedNodes) knownNodes.Add(node);
                if (descriptor.Applicability(context) == NutConfigurationApplicability.Unsupported)
                {
                    fields.Add(new(descriptor, NutConfigurationSemanticState.Unsupported, null, null, null, null,
                        descriptor.Sensitive ? repeatedNodes.Length == 0 ? NutSensitiveFieldState.NotConfigured : NutSensitiveFieldState.Configured : null));
                    continue;
                }
                if (repeatedNodes.Length == 0)
                {
                    fields.Add(new(descriptor, MissingState(descriptor), null, null, null, null,
                        descriptor.Sensitive ? NutSensitiveFieldState.NotConfigured : null));
                    if (descriptor.Required)
                        issues.Add(new("Semantic.Required", ValidationSeverity.Error, "Semantic.Validation.Required", descriptor.SemanticId));
                    continue;
                }
                var sectionOccurrences = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                for (var index = 0; index < repeatedNodes.Length; index++)
                {
                    var nodeSection = repeatedNodes[index] switch
                    {
                        NutConfigurationAssignmentNode assignment => assignment.SectionName,
                        NutConfigurationDirectiveNode directive => directive.SectionName,
                        _ => null
                    };
                    var sectionKey = nodeSection ?? string.Empty;
                    sectionOccurrences.TryGetValue(sectionKey, out var occurrence);
                    sectionOccurrences[sectionKey] = occurrence + 1;
                    fields.Add(ProjectNode(descriptor, repeatedNodes[index], nodeSection, occurrence, issues));
                }
                continue;
            }

            var sections = descriptor.Scope == NutConfigurationFieldScope.Section
                ? document.Sections.Select(section => (string?)section.Name).ToArray()
                : new string?[] { null };
            foreach (var section in sections)
            {
                var nodes = MatchingNodes(document, descriptor, section).ToArray();
                foreach (var node in nodes) knownNodes.Add(node);
                var applicability = descriptor.Applicability(context);
                if (applicability == NutConfigurationApplicability.Unsupported)
                {
                    fields.Add(new(descriptor, NutConfigurationSemanticState.Unsupported, null, section, null, null,
                        descriptor.Sensitive ? nodes.Length == 0 ? NutSensitiveFieldState.NotConfigured : NutSensitiveFieldState.Configured : null));
                    continue;
                }

                if (descriptor.Scope != NutConfigurationFieldScope.Repeated && nodes.Length > 1)
                {
                    issues.Add(new("Semantic.DuplicateSingleton", ValidationSeverity.Error, "Semantic.Validation.DuplicateSingleton", descriptor.SemanticId, section));
                    for (var index = 0; index < nodes.Length; index++) fields.Add(ProjectNode(descriptor, nodes[index], section, index, issues));
                    continue;
                }

                if (nodes.Length == 0)
                {
                    fields.Add(new(descriptor, MissingState(descriptor), null, section, null, null,
                        descriptor.Sensitive ? NutSensitiveFieldState.NotConfigured : null));
                    if (descriptor.Required)
                        issues.Add(new("Semantic.Required", ValidationSeverity.Error, "Semantic.Validation.Required", descriptor.SemanticId, section));
                    continue;
                }

                for (var index = 0; index < nodes.Length; index++) fields.Add(ProjectNode(descriptor, nodes[index], section, index, issues));
            }
        }

        var custom = new List<NutConfigurationCustomParameter>();
        var occurrences = new Dictionary<(NutConfigurationEntryKind, string, string?), int>();
        for (var nodeIndex = 0; nodeIndex < document.Nodes.Count; nodeIndex++)
        {
            var node = document.Nodes[nodeIndex];
            if (knownNodes.Contains(node)) continue;
            var parameter = node switch
            {
                NutConfigurationAssignmentNode assignment => CreateCustom(
                    NutConfigurationEntryKind.Assignment, assignment.Name, assignment.Value, assignment.SectionName, assignment.IsSensitive, nodeIndex, occurrences),
                NutConfigurationDirectiveNode directive => CreateCustom(
                    NutConfigurationEntryKind.Directive, directive.Name, directive.Arguments, directive.SectionName, directive.IsSensitive, nodeIndex, occurrences),
                _ => null
            };
            if (parameter is not null) custom.Add(parameter);
        }

        return new(document.FileKind, fields.AsReadOnly(), custom.AsReadOnly(), issues.AsReadOnly());
    }

    private static NutConfigurationSemanticField ProjectNode(
        NutConfigurationFieldDescriptor descriptor,
        NutConfigurationNode node,
        string? section,
        int occurrence,
        ICollection<NutConfigurationSemanticIssue> issues)
    {
        var raw = node switch
        {
            NutConfigurationAssignmentNode assignment => assignment.Value,
            NutConfigurationDirectiveNode directive => directive.Arguments,
            _ => string.Empty
        };
        var state = descriptor.AutomaticPolicy == NutConfigurationAutomaticPolicy.ExplicitAutoToken &&
            string.Equals(raw, descriptor.ExplicitAutoToken, StringComparison.OrdinalIgnoreCase)
                ? NutConfigurationSemanticState.ExplicitAutoToken
                : NutConfigurationSemanticState.Explicit;
        if (descriptor.Sensitive)
            return new(descriptor, state, null, section, RowId(descriptor.SemanticId, section, occurrence), occurrence, NutSensitiveFieldState.Configured)
            { StableRowId = StableRowId(descriptor.SemanticId, section, node, occurrence) };
        var parsed = descriptor.Codec.Parse(raw, descriptor.SemanticId);
        foreach (var issue in parsed.Issues)
            issues.Add(new(issue.Code, issue.Severity, issue.ResourceKey, descriptor.SemanticId, section, RowId(descriptor.SemanticId, section, occurrence)));
        return new(descriptor, state, parsed.IsValid ? parsed.Value : raw, section, RowId(descriptor.SemanticId, section, occurrence), occurrence)
        { StableRowId = StableRowId(descriptor.SemanticId, section, node, occurrence) };
    }

    private static NutConfigurationSemanticState MissingState(NutConfigurationFieldDescriptor descriptor) => descriptor.Required
        ? NutConfigurationSemanticState.MissingRequired
        : descriptor.AutomaticPolicy == NutConfigurationAutomaticPolicy.OmitDirective
            ? NutConfigurationSemanticState.AutomaticByOmission
            : NutConfigurationSemanticState.Explicit;

    private static IEnumerable<NutConfigurationNode> MatchingNodes(
        NutConfigurationDocument document,
        NutConfigurationFieldDescriptor descriptor,
        string? section) => descriptor.EntryKind switch
        {
            NutConfigurationEntryKind.Assignment => document.Nodes.OfType<NutConfigurationAssignmentNode>()
                .Where(node => Equal(node.Name, descriptor.Name) && MatchesScope(node.SectionName, descriptor.Scope, section))
                .Cast<NutConfigurationNode>(),
            _ => document.Nodes.OfType<NutConfigurationDirectiveNode>()
                .Where(node => Equal(node.Name, descriptor.Name) && MatchesScope(node.SectionName, descriptor.Scope, section))
                .Cast<NutConfigurationNode>()
        };

    private static NutConfigurationCustomParameter CreateCustom(
        NutConfigurationEntryKind kind,
        string name,
        string value,
        string? section,
        bool sensitive,
        int nodeIndex,
        IDictionary<(NutConfigurationEntryKind, string, string?), int> occurrences)
    {
        var key = (kind, name.ToUpperInvariant(), section?.ToUpperInvariant());
        occurrences.TryGetValue(key, out var occurrence);
        occurrences[key] = occurrence + 1;
        return new($"custom:{nodeIndex}", kind, name, sensitive ? null : value, section, occurrence, sensitive);
    }

    private static string RowId(string semanticId, string? section, int occurrence) => $"{semanticId}:{section ?? "global"}:{occurrence}";
    private static string StableRowId(string semanticId, string? section, NutConfigurationNode node, int occurrence) =>
        $"{semanticId}:{section ?? "global"}:{node.SemanticIdentity ?? occurrence.ToString(System.Globalization.CultureInfo.InvariantCulture)}";
    private static bool MatchesScope(string? actualSection, NutConfigurationFieldScope scope, string? requestedSection) => scope switch
    {
        NutConfigurationFieldScope.Global => actualSection is null,
        NutConfigurationFieldScope.Section => actualSection is not null && requestedSection is not null && Equal(actualSection, requestedSection),
        _ => requestedSection is null || actualSection is not null && Equal(actualSection, requestedSection)
    };
    private static bool Equal(string left, string right) => string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
}
