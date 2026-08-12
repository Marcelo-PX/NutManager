using NutManager.Core.Validation;

namespace NutManager.Core.Configuration.Semantic;

public sealed class NutUpsConfigurationDocumentValidationRule : INutConfigurationDocumentValidationRule
{
    private readonly NutDriverCatalog _catalog;
    private readonly bool _requireDetectedExecutable;

    public NutUpsConfigurationDocumentValidationRule(NutDriverCatalog catalog, bool requireDetectedExecutable = true)
    {
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        _requireDetectedExecutable = requireDetectedExecutable;
    }

    public IReadOnlyList<NutConfigurationSemanticIssue> Validate(
        NutConfigurationDocument document,
        NutConfigurationSemanticProjection projection)
    {
        _ = projection;
        if (document.FileKind != NutConfigurationFileKind.UpsConf) return [];
        var issues = new List<NutConfigurationSemanticIssue>();
        var duplicateSections = document.Sections.GroupBy(section => section.Name, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1);
        foreach (var duplicate in duplicateSections)
            issues.Add(Issue("Ups.Section.Duplicate", "Ups.Validation.Section.Duplicate", "Ups.Section", duplicate.Key));

        foreach (var section in document.Sections)
        {
            if (!IsValidSectionName(section.Name))
                issues.Add(Issue("Ups.Section.Invalid", "Ups.Validation.Section.Invalid", "Ups.Section", section.Name));
            if (string.Equals(section.Name, "default", StringComparison.OrdinalIgnoreCase))
                issues.Add(Issue("Ups.Section.Reserved", "Ups.Validation.Section.Reserved", "Ups.Section", section.Name));

            var assignments = document.Nodes.OfType<NutConfigurationAssignmentNode>()
                .Where(node => string.Equals(node.SectionName, section.Name, StringComparison.OrdinalIgnoreCase)).ToArray();
            var directives = document.Nodes.OfType<NutConfigurationDirectiveNode>()
                .Where(node => string.Equals(node.SectionName, section.Name, StringComparison.OrdinalIgnoreCase)).ToArray();
            var driver = Singleton(assignments, "driver", section.Name, issues, required: true);
            _ = Singleton(assignments, "port", section.Name, issues, required: true);
            if (driver is null) continue;
            if (!NutDriverCatalog.IsValidDriverName(driver))
            {
                issues.Add(Issue("Ups.Driver.Invalid", "Ups.Validation.Driver.Invalid", "Ups.Driver", section.Name));
                continue;
            }
            var catalogEntry = _catalog.Find(driver);
            if (catalogEntry is null)
            {
                issues.Add(new("Ups.Driver.Unverified", ValidationSeverity.Warning,
                    "Ups.Validation.Driver.Unverified", "Ups.Driver", section.Name));
                continue;
            }

            if (catalogEntry.Schema is null)
                issues.Add(new("Ups.Driver.LimitedSchema", ValidationSeverity.Warning, "Ups.Validation.Driver.LimitedSchema", "Ups.Driver", section.Name));
            if (_requireDetectedExecutable && !catalogEntry.IsInstalled)
                issues.Add(new("Ups.Driver.NotDetected", ValidationSeverity.Warning, "Ups.Validation.Driver.NotDetected", "Ups.Driver", section.Name));

            var protocol = Singleton(assignments, "protocol", section.Name, issues);
            if (protocol is not null && catalogEntry.Schema is { SupportedProtocols.Count: > 0 } schema &&
                !schema.SupportedProtocols.Contains(protocol, StringComparer.OrdinalIgnoreCase))
                issues.Add(Issue("Ups.Protocol.Unsupported", "Ups.Validation.Protocol.Unsupported", "Ups.Protocol", section.Name));

            var runtimecal = Singleton(assignments, "runtimecal", section.Name, issues);
            if (runtimecal is not null)
            {
                if (!string.Equals(driver, "nutdrv_qx", StringComparison.OrdinalIgnoreCase))
                    issues.Add(Issue("Ups.Runtimecal.Unsupported", "Ups.Validation.Runtimecal.Unsupported", "Ups.RuntimeCalibration", section.Name));
                else
                    foreach (var issue in NutRuntimeCalibration.Parse(runtimecal).Issues)
                        issues.Add(new(issue.Code, issue.Severity, issue.ResourceKey, issue.Field, section.Name));
            }

            if (directives.Any(node => string.Equals(node.Name, "ignorelb", StringComparison.OrdinalIgnoreCase)) &&
                !assignments.Any(node => string.Equals(node.Name, "override.battery.charge.low", StringComparison.OrdinalIgnoreCase) ||
                                         string.Equals(node.Name, "override.battery.runtime.low", StringComparison.OrdinalIgnoreCase)))
                issues.Add(Issue("Ups.IgnoreLb.ThresholdRequired", "Ups.Validation.IgnoreLb.ThresholdRequired", "Ups.IgnoreLowBattery", section.Name));

            if (string.Equals(driver, "snmp-ups", StringComparison.OrdinalIgnoreCase))
                ValidateSnmp(assignments, section.Name, issues);
        }

        return issues;
    }

    public static bool IsValidSectionName(string name) => !string.IsNullOrWhiteSpace(name) &&
        !string.Equals(name, "default", StringComparison.OrdinalIgnoreCase) &&
        name.All(character => char.IsLetterOrDigit(character) || character is '_' or '-' or '.');

    private static void ValidateSnmp(
        IReadOnlyList<NutConfigurationAssignmentNode> assignments,
        string section,
        ICollection<NutConfigurationSemanticIssue> issues)
    {
        var version = Value(assignments, "snmp_version");
        var level = Value(assignments, "secLevel");
        if (!string.Equals(version, "v3", StringComparison.OrdinalIgnoreCase)) return;
        if (string.IsNullOrWhiteSpace(Value(assignments, "secName")))
            issues.Add(Issue("Ups.Snmp.SecurityNameRequired", "Ups.Validation.Snmp.SecurityNameRequired", "Ups.SnmpSecurityName", section));
        if (string.Equals(level, "authNoPriv", StringComparison.OrdinalIgnoreCase) || string.Equals(level, "authPriv", StringComparison.OrdinalIgnoreCase))
            if (string.IsNullOrWhiteSpace(Value(assignments, "authPassword")))
                issues.Add(Issue("Ups.Snmp.AuthPasswordRequired", "Ups.Validation.Snmp.AuthPasswordRequired", "Ups.SnmpAuthPassword", section));
        if (string.Equals(level, "authPriv", StringComparison.OrdinalIgnoreCase) && string.IsNullOrWhiteSpace(Value(assignments, "privPassword")))
            issues.Add(Issue("Ups.Snmp.PrivPasswordRequired", "Ups.Validation.Snmp.PrivPasswordRequired", "Ups.SnmpPrivacyPassword", section));
    }

    private static string? Singleton(
        IReadOnlyList<NutConfigurationAssignmentNode> assignments,
        string name,
        string section,
        ICollection<NutConfigurationSemanticIssue> issues,
        bool required = false)
    {
        var matches = assignments.Where(node => string.Equals(node.Name, name, StringComparison.OrdinalIgnoreCase)).ToArray();
        if (matches.Length == 0)
        {
            if (required) issues.Add(Issue($"Ups.{name}.Required", "Semantic.Validation.Required", $"Ups.{name}", section));
            return null;
        }
        if (matches.Length > 1)
        {
            issues.Add(Issue($"Ups.{name}.Duplicate", "Semantic.Validation.DuplicateSingleton", $"Ups.{name}", section));
            return null;
        }
        return matches[0].Value;
    }

    private static string? Value(IEnumerable<NutConfigurationAssignmentNode> assignments, string name) =>
        assignments.FirstOrDefault(node => string.Equals(node.Name, name, StringComparison.OrdinalIgnoreCase))?.Value;

    private static NutConfigurationSemanticIssue Issue(string code, string resource, string target, string? section) =>
        new(code, ValidationSeverity.Error, resource, target, section);
}
