namespace NutManager.Core.Configuration.Semantic;

public sealed class NutConfigurationSemanticDraft : IDisposable
{
    private readonly string _originalText;
    private readonly NutConfigurationFileSchema _schema;
    private NutConfigurationSemanticContext _context;
    private readonly NutConfigurationParser _parser = new();
    private readonly NutConfigurationSemanticProjector _projector = new();
    private readonly NutConfigurationSemanticValidator _validator;
    private readonly List<DraftMutation> _mutations = [];
    private readonly List<NutConfigurationSemanticReviewItem> _review = [];
    private int _nextRepeatedRowIdentity;
    private bool _disposed;

    public NutConfigurationSemanticDraft(
        NutConfigurationDocument originalDocument,
        NutConfigurationFileSchema schema,
        NutConfigurationSemanticContext? context = null,
        NutConfigurationSemanticValidator? validator = null)
    {
        ArgumentNullException.ThrowIfNull(originalDocument);
        ArgumentNullException.ThrowIfNull(schema);
        if (originalDocument.FileKind != schema.FileKind) throw new ArgumentException("The schema does not match the document.", nameof(schema));
        FileKind = originalDocument.FileKind;
        _originalText = originalDocument.Serialize();
        _schema = schema;
        _context = context ?? new();
        _validator = validator ?? new();
        RefreshState();
    }

    public NutConfigurationFileKind FileKind { get; }
    public bool IsModified => _mutations.Count > 0;
    public NutConfigurationSemanticProjection Projection { get; private set; } = null!;
    public NutConfigurationSemanticValidationResult Validation { get; private set; } = null!;
    public NutConfigurationSemanticReview Review => new(_review.ToArray());

    public void UpdateContext(NutConfigurationSemanticContext context)
    {
        ThrowIfDisposed();
        _context = context ?? throw new ArgumentNullException(nameof(context));
        RefreshState();
    }

    public NutConfigurationMutationResult Set(string semanticId, object value, string? section = null, int? occurrence = null)
    {
        var descriptor = GetDescriptor(semanticId);
        if (descriptor is null) return NotFound();
        if (descriptor.Sensitive) return new(NutConfigurationMutationStatus.UnsupportedOperation, "Sensitive.ChangeOnly");
        var serialized = descriptor.Codec.Serialize(value, descriptor.SemanticId);
        if (!serialized.IsValid || serialized.Value is null) return Invalid();
        return Commit(new(DraftMutationKind.Set, descriptor, section, occurrence, serialized.Value),
            ReviewFor(descriptor, NutConfigurationSemanticChangeOperation.Set, section, occurrence, SafeCurrentValue(descriptor, section, occurrence), serialized.Value));
    }

    public NutConfigurationMutationResult ReplaceSensitive(string semanticId, NutSensitiveValue replacement, string? section = null, int? occurrence = null)
    {
        ArgumentNullException.ThrowIfNull(replacement);
        var descriptor = GetDescriptor(semanticId);
        if (descriptor is null) return NotFound();
        if (!descriptor.Sensitive) return new(NutConfigurationMutationStatus.UnsupportedOperation, "Sensitive.NotSensitive");
        var payload = new SensitivePayload(replacement.CopyValue());
        var result = Commit(new(DraftMutationKind.Set, descriptor, section, occurrence, Sensitive: payload),
            ReviewFor(descriptor, NutConfigurationSemanticChangeOperation.Set, section, occurrence,
                SafeCurrentValue(descriptor, section, occurrence), "Semantic.Sensitive.ReplacementPending", sensitive: true));
        if (!result.Succeeded) payload.Dispose();
        return result;
    }

    public NutConfigurationMutationResult SetAutomatic(string semanticId, object? detectedValue = null, string? section = null, int? occurrence = null)
    {
        var descriptor = GetDescriptor(semanticId);
        if (descriptor is null) return NotFound();
        return descriptor.AutomaticPolicy switch
        {
            NutConfigurationAutomaticPolicy.OmitDirective => Commit(
                new(DraftMutationKind.Automatic, descriptor, section, occurrence),
                ReviewFor(descriptor, NutConfigurationSemanticChangeOperation.SetAutomatic, section, occurrence,
                    SafeCurrentValue(descriptor, section, occurrence), "Semantic.State.AutomaticByOmission")),
            NutConfigurationAutomaticPolicy.ExplicitAutoToken => Commit(
                new(DraftMutationKind.Automatic, descriptor, section, occurrence, descriptor.ExplicitAutoToken),
                ReviewFor(descriptor, NutConfigurationSemanticChangeOperation.SetAutomatic, section, occurrence,
                    SafeCurrentValue(descriptor, section, occurrence), descriptor.ExplicitAutoToken)),
            NutConfigurationAutomaticPolicy.DetectedAndPersisted when detectedValue is not null => Set(semanticId, detectedValue, section, occurrence),
            NutConfigurationAutomaticPolicy.DetectedAndPersisted => new(NutConfigurationMutationStatus.ValidationFailed, "Automatic.DetectedValueRequired"),
            _ => new(NutConfigurationMutationStatus.UnsupportedOperation, "Automatic.NotSupported")
        };
    }

    public NutConfigurationMutationResult Remove(string semanticId, string? section = null, int? occurrence = null)
    {
        var descriptor = GetDescriptor(semanticId);
        if (descriptor is null) return NotFound();
        return Commit(new(DraftMutationKind.Remove, descriptor, section, occurrence),
            ReviewFor(descriptor, NutConfigurationSemanticChangeOperation.Remove, section, occurrence,
                descriptor.Sensitive ? "Semantic.Sensitive.Configured" : SafeCurrentValue(descriptor, section, occurrence), null, descriptor.Sensitive));
    }

    public NutConfigurationMutationResult AddRepeated(string semanticId, object value, string? section = null)
    {
        var descriptor = GetDescriptor(semanticId);
        if (descriptor is null) return NotFound();
        if (descriptor.Scope != NutConfigurationFieldScope.Repeated || descriptor.Sensitive) return new(NutConfigurationMutationStatus.UnsupportedOperation, "Repeated.NotSupported");
        var serialized = descriptor.Codec.Serialize(value, descriptor.SemanticId);
        if (!serialized.IsValid || serialized.Value is null) return Invalid();
        var identity = $"added:{_nextRepeatedRowIdentity}";
        var result = Commit(new(DraftMutationKind.AddRepeated, descriptor, section, null, serialized.Value, RowIdentity: identity),
            ReviewFor(descriptor, NutConfigurationSemanticChangeOperation.AddRepeatedRow, section, null, null, serialized.Value));
        if (result.Succeeded) _nextRepeatedRowIdentity++;
        return result;
    }

    public NutConfigurationMutationResult RemoveRepeated(string semanticId, int occurrence, string? section = null)
    {
        var descriptor = GetDescriptor(semanticId);
        if (descriptor is null) return NotFound();
        if (descriptor.Scope != NutConfigurationFieldScope.Repeated) return new(NutConfigurationMutationStatus.UnsupportedOperation, "Repeated.NotSupported");
        return Commit(new(DraftMutationKind.Remove, descriptor, section, occurrence),
            ReviewFor(descriptor, NutConfigurationSemanticChangeOperation.RemoveRepeatedRow, section, occurrence,
                descriptor.Sensitive ? "Semantic.Sensitive.Configured" : SafeCurrentValue(descriptor, section, occurrence), null, descriptor.Sensitive));
    }

    public NutConfigurationMutationResult EditRepeated(string semanticId, int occurrence, object value, string? section = null)
    {
        var descriptor = GetDescriptor(semanticId);
        if (descriptor is null) return NotFound();
        if (descriptor.Scope != NutConfigurationFieldScope.Repeated || descriptor.Sensitive)
            return new(NutConfigurationMutationStatus.UnsupportedOperation, "Repeated.NotSupported");
        var serialized = descriptor.Codec.Serialize(value, descriptor.SemanticId);
        if (!serialized.IsValid || serialized.Value is null) return Invalid();
        return Commit(new(DraftMutationKind.Set, descriptor, section, occurrence, serialized.Value),
            ReviewFor(descriptor, NutConfigurationSemanticChangeOperation.EditRepeatedRow, section, occurrence,
                SafeCurrentValue(descriptor, section, occurrence), serialized.Value));
    }

    public NutConfigurationMutationResult EditRepeated(string semanticId, string rowId, object value)
    {
        var target = Projection.Fields.SingleOrDefault(field => field.Descriptor.SemanticId == semanticId && field.StableRowId == rowId);
        return target?.Occurrence is { } occurrence
            ? EditRepeated(semanticId, occurrence, value, target.Section)
            : new(NutConfigurationMutationStatus.TargetNotFound, "Repeated.RowNotFound");
    }

    public NutConfigurationMutationResult RemoveRepeated(string semanticId, string rowId)
    {
        var target = Projection.Fields.SingleOrDefault(field => field.Descriptor.SemanticId == semanticId && field.StableRowId == rowId);
        return target?.Occurrence is { } occurrence
            ? RemoveRepeated(semanticId, occurrence, target.Section)
            : new(NutConfigurationMutationStatus.TargetNotFound, "Repeated.RowNotFound");
    }

    /// <summary>
    /// Edits a composite row while carrying its existing credential across untouched. The caller
    /// supplies the row with a placeholder where the secret goes; the stored token is spliced in
    /// during replay, so an administrator can change a monitor's power value without the password
    /// ever being read, displayed or re-typed.
    /// </summary>
    public NutConfigurationMutationResult EditRepeatedPreservingSecret(string semanticId, string rowId, object value)
    {
        var descriptor = GetDescriptor(semanticId);
        if (descriptor is null) return NotFound();
        if (!descriptor.HasEmbeddedSecret) return new(NutConfigurationMutationStatus.UnsupportedOperation, "Secret.NotEmbedded");
        var target = Projection.Fields.SingleOrDefault(field => field.Descriptor.SemanticId == semanticId && field.StableRowId == rowId);
        if (target?.Occurrence is not { } occurrence) return new(NutConfigurationMutationStatus.TargetNotFound, "Repeated.RowNotFound");
        var serialized = descriptor.Codec.Serialize(value, descriptor.SemanticId);
        if (!serialized.IsValid || serialized.Value is null) return Invalid();

        return Commit(
            new(DraftMutationKind.SetPreservingSecret, descriptor, target.Section, occurrence, serialized.Value),
            ReviewFor(descriptor, NutConfigurationSemanticChangeOperation.EditRepeatedRow, target.Section, occurrence,
                null, null, sensitive: true));
    }

    /// <summary>Replaces only the credential inside a composite row, leaving its other values alone.</summary>
    public NutConfigurationMutationResult ReplaceRepeatedSecret(string semanticId, string rowId, NutSensitiveValue replacement)
    {
        ArgumentNullException.ThrowIfNull(replacement);
        var descriptor = GetDescriptor(semanticId);
        if (descriptor is null) return NotFound();
        if (!descriptor.HasEmbeddedSecret) return new(NutConfigurationMutationStatus.UnsupportedOperation, "Secret.NotEmbedded");
        var target = Projection.Fields.SingleOrDefault(field => field.Descriptor.SemanticId == semanticId && field.StableRowId == rowId);
        if (target?.Occurrence is not { } occurrence) return new(NutConfigurationMutationStatus.TargetNotFound, "Repeated.RowNotFound");

        var payload = new SensitivePayload(replacement.CopyValue());
        var result = Commit(
            new(DraftMutationKind.ReplaceEmbeddedSecret, descriptor, target.Section, occurrence, Sensitive: payload),
            ReviewFor(descriptor, NutConfigurationSemanticChangeOperation.EditRepeatedRow, target.Section, occurrence,
                "Semantic.Sensitive.Configured", "Semantic.Sensitive.ReplacementPending", sensitive: true));
        if (!result.Succeeded) payload.Dispose();
        return result;
    }

    /// <summary>
    /// Appends a composite row together with its credential. A new row has no stored secret to
    /// preserve, so the credential is required here rather than optional.
    /// </summary>
    public NutConfigurationMutationResult AddRepeatedWithSecret(string semanticId, object value, NutSensitiveValue secret)
    {
        ArgumentNullException.ThrowIfNull(secret);
        var descriptor = GetDescriptor(semanticId);
        if (descriptor is null) return NotFound();
        if (!descriptor.HasEmbeddedSecret || descriptor.Scope != NutConfigurationFieldScope.Repeated)
            return new(NutConfigurationMutationStatus.UnsupportedOperation, "Secret.NotEmbedded");
        var serialized = descriptor.Codec.Serialize(value, descriptor.SemanticId);
        if (!serialized.IsValid || serialized.Value is null) return Invalid();

        var payload = new SensitivePayload(secret.CopyValue());
        var identity = $"added:{_nextRepeatedRowIdentity}";
        var result = Commit(
            new(DraftMutationKind.AddRepeatedWithSecret, descriptor, null, null, serialized.Value, Sensitive: payload, RowIdentity: identity),
            ReviewFor(descriptor, NutConfigurationSemanticChangeOperation.AddRepeatedRow, null, null, null, null, sensitive: true));
        if (result.Succeeded) _nextRepeatedRowIdentity++;
        else payload.Dispose();
        return result;
    }

    public NutConfigurationMutationResult AddSection(string name) => Commit(
        new(DraftMutationKind.AddSection, Section: name),
        GenericReview("Semantic.Section", NutConfigurationSemanticChangeOperation.AddSection, name, null, name));

    public NutConfigurationMutationResult RemoveSection(string name) => Commit(
        new(DraftMutationKind.RemoveSection, Section: name),
        GenericReview("Semantic.Section", NutConfigurationSemanticChangeOperation.RemoveSection, name, name, null));

    public NutConfigurationMutationResult RenameSection(string name, string newName) => Commit(
        new(DraftMutationKind.RenameSection, Section: name, Value: newName),
        GenericReview("Semantic.Section", NutConfigurationSemanticChangeOperation.RenameSection, name, name, newName));

    public NutConfigurationMutationResult AddCustomParameter(
        NutConfigurationEntryKind entryKind,
        string name,
        string value,
        string? section = null)
    {
        var issues = NutConfigurationSemanticValidator.ValidateCustomParameter(entryKind, name, value, section);
        if (issues.Any(issue => issue.Severity == Core.Validation.ValidationSeverity.Error)) return Invalid();
        return Commit(new(DraftMutationKind.AddCustom, Section: section, Value: value, CustomName: name, CustomEntryKind: entryKind),
            GenericReview("Semantic.CustomParameter", NutConfigurationSemanticChangeOperation.AddCustomParameter, section, null, value));
    }

    public NutConfigurationMutationResult EditCustomParameter(
        NutConfigurationEntryKind entryKind,
        string name,
        int occurrence,
        string value,
        string? section = null)
    {
        var issues = NutConfigurationSemanticValidator.ValidateCustomParameter(entryKind, name, value, section);
        if (issues.Any(issue => issue.Severity == Core.Validation.ValidationSeverity.Error)) return Invalid();
        return Commit(new(DraftMutationKind.EditCustom, Section: section, Occurrence: occurrence, Value: value, CustomName: name, CustomEntryKind: entryKind),
            GenericReview("Semantic.CustomParameter", NutConfigurationSemanticChangeOperation.EditCustomParameter, section, null, value));
    }

    public NutConfigurationMutationResult RemoveCustomParameter(
        NutConfigurationEntryKind entryKind,
        string name,
        int occurrence,
        string? section = null) => Commit(
            new(DraftMutationKind.RemoveCustom, Section: section, Occurrence: occurrence, CustomName: name, CustomEntryKind: entryKind),
            GenericReview("Semantic.CustomParameter", NutConfigurationSemanticChangeOperation.RemoveCustomParameter, section, name, null));

    public NutConfigurationDocument Materialize()
    {
        ThrowIfDisposed();
        var result = Replay(_mutations);
        if (result.Result is not null) throw new InvalidOperationException("A committed semantic mutation could not be replayed.");
        return result.Document!;
    }

    public void Reset()
    {
        ThrowIfDisposed();
        foreach (var mutation in _mutations) mutation.Sensitive?.Dispose();
        _mutations.Clear();
        _review.Clear();
        RefreshState();
    }

    public void Dispose()
    {
        if (_disposed) return;
        foreach (var mutation in _mutations) mutation.Sensitive?.Dispose();
        _mutations.Clear();
        _review.Clear();
        _disposed = true;
    }

    private NutConfigurationMutationResult Commit(DraftMutation mutation, NutConfigurationSemanticReviewItem review)
    {
        ThrowIfDisposed();
        var attempted = _mutations.Append(mutation).ToArray();
        var replay = Replay(attempted);
        if (replay.Result is not null) return replay.Result;
        _mutations.Add(mutation);
        _review.Add(review);
        RefreshState(replay.Document);
        return NutConfigurationMutationResult.Success();
    }

    private (NutConfigurationDocument? Document, NutConfigurationMutationResult? Result) Replay(IEnumerable<DraftMutation> mutations)
    {
        var document = _parser.Parse(FileKind, _originalText);
        var mutator = new NutConfigurationDocumentMutator(document);
        foreach (var mutation in mutations)
        {
            var result = Apply(mutator, document, mutation);
            if (!result.Succeeded) return (null, result);
        }
        return (document, null);
    }

    private NutConfigurationMutationResult Apply(NutConfigurationDocumentMutator mutator, NutConfigurationDocument document, DraftMutation mutation)
    {
        if (mutation.Kind == DraftMutationKind.AddSection) return mutator.AddSection(mutation.Section!);
        if (mutation.Kind == DraftMutationKind.RemoveSection) return mutator.RemoveSection(mutation.Section!);
        if (mutation.Kind == DraftMutationKind.RenameSection) return mutator.RenameSection(mutation.Section!, mutation.Value!);
        if (mutation.Kind is DraftMutationKind.AddCustom or DraftMutationKind.EditCustom or DraftMutationKind.RemoveCustom)
            return ApplyCustom(mutator, mutation);
        var descriptor = mutation.Descriptor!;
        if (mutation.Kind is DraftMutationKind.SetPreservingSecret
            or DraftMutationKind.ReplaceEmbeddedSecret
            or DraftMutationKind.AddRepeatedWithSecret)
            return ApplyEmbeddedSecret(mutator, document, mutation, descriptor);
        var value = mutation.Sensitive is null ? mutation.Value : mutation.Sensitive.Reveal();
        return mutation.Kind switch
        {
            DraftMutationKind.Set => SetOrInsert(mutator, descriptor, value!, mutation.Section, mutation.Occurrence),
            DraftMutationKind.Automatic when descriptor.AutomaticPolicy == NutConfigurationAutomaticPolicy.OmitDirective =>
                RemoveAllowMissing(mutator, descriptor, mutation.Section, mutation.Occurrence),
            DraftMutationKind.Automatic => SetOrInsert(mutator, descriptor, mutation.Value!, mutation.Section, mutation.Occurrence),
            DraftMutationKind.Remove => Remove(mutator, descriptor, mutation.Section, mutation.Occurrence),
            DraftMutationKind.AddRepeated => Insert(mutator, descriptor, mutation.Value!, mutation.Section, mutation.RowIdentity),
            _ => new(NutConfigurationMutationStatus.UnsupportedOperation, "Mutation.Unsupported")
        };
    }

    /// <summary>
    /// Rebuilds a composite row's arguments. The stored credential is read from the document being
    /// replayed and written straight back into the new line; it is never returned, logged, or put
    /// anywhere a caller could reach.
    /// </summary>
    private NutConfigurationMutationResult ApplyEmbeddedSecret(
        NutConfigurationDocumentMutator mutator,
        NutConfigurationDocument document,
        DraftMutation mutation,
        NutConfigurationFieldDescriptor descriptor)
    {
        var secretIndex = descriptor.SecretTokenIndex!.Value;
        if (mutation.Kind == DraftMutationKind.AddRepeatedWithSecret)
        {
            var line = NutEmbeddedSecret.Replace(mutation.Value!, secretIndex, mutation.Sensitive!.RevealSpan());
            return mutator.InsertDirective(descriptor.Name, line, mutation.Section, descriptor.InsertionOrder,
                PreferredOrder(descriptor), mutation.RowIdentity);
        }

        var current = CurrentArguments(document, descriptor, mutation.Section, mutation.Occurrence);
        if (current is null) return new(NutConfigurationMutationStatus.TargetNotFound, "Repeated.RowNotFound");
        var arguments = mutation.Kind == DraftMutationKind.SetPreservingSecret
            ? NutEmbeddedSecret.Preserve(current, mutation.Value!, secretIndex)
            : NutEmbeddedSecret.Replace(current, secretIndex, mutation.Sensitive!.RevealSpan());
        return mutator.SetDirectiveArguments(descriptor.Name, arguments, mutation.Section, mutation.Occurrence);
    }

    private static string? CurrentArguments(
        NutConfigurationDocument document,
        NutConfigurationFieldDescriptor descriptor,
        string? section,
        int? occurrence)
    {
        var matches = document.Nodes.OfType<NutConfigurationDirectiveNode>()
            .Where(node => Equal(node.Name, descriptor.Name) && EqualSection(node.SectionName, section))
            .ToArray();
        var index = occurrence ?? (matches.Length == 1 ? 0 : -1);
        return index >= 0 && index < matches.Length ? matches[index].Arguments : null;
    }

    private static NutConfigurationMutationResult ApplyCustom(NutConfigurationDocumentMutator mutator, DraftMutation mutation) =>
        (mutation.Kind, mutation.CustomEntryKind) switch
        {
            (DraftMutationKind.AddCustom, NutConfigurationEntryKind.Assignment) => mutator.InsertAssignment(mutation.CustomName!, mutation.Value!, mutation.Section),
            (DraftMutationKind.AddCustom, _) => mutator.InsertDirective(mutation.CustomName!, mutation.Value!, mutation.Section),
            (DraftMutationKind.EditCustom, NutConfigurationEntryKind.Assignment) => mutator.SetAssignment(mutation.CustomName!, mutation.Value!, mutation.Section, mutation.Occurrence),
            (DraftMutationKind.EditCustom, _) => mutator.SetDirectiveArguments(mutation.CustomName!, mutation.Value!, mutation.Section, mutation.Occurrence),
            (DraftMutationKind.RemoveCustom, NutConfigurationEntryKind.Assignment) => mutator.RemoveAssignment(mutation.CustomName!, mutation.Section, mutation.Occurrence),
            _ => mutator.RemoveDirective(mutation.CustomName!, mutation.Section, mutation.Occurrence)
        };

    private NutConfigurationMutationResult SetOrInsert(
        NutConfigurationDocumentMutator mutator,
        NutConfigurationFieldDescriptor descriptor,
        string value,
        string? section,
        int? occurrence)
    {
        var set = descriptor.EntryKind == NutConfigurationEntryKind.Assignment
            ? mutator.SetAssignment(descriptor.Name, value, section, occurrence, !descriptor.ValueIsTokenList)
            : mutator.SetDirectiveArguments(descriptor.Name, value, section, occurrence);
        return set.Status == NutConfigurationMutationStatus.TargetNotFound && occurrence is null
            ? Insert(mutator, descriptor, value, section)
            : set;
    }

    private NutConfigurationMutationResult Insert(
        NutConfigurationDocumentMutator mutator,
        NutConfigurationFieldDescriptor descriptor,
        string value,
        string? section,
        string? semanticIdentity = null) =>
        descriptor.EntryKind == NutConfigurationEntryKind.Assignment
            ? mutator.InsertAssignment(descriptor.Name, value, section, descriptor.InsertionOrder, PreferredOrder(descriptor), semanticIdentity, !descriptor.ValueIsTokenList)
            : mutator.InsertDirective(descriptor.Name, value, section, descriptor.InsertionOrder, PreferredOrder(descriptor), semanticIdentity);

    private IReadOnlyDictionary<string, int> PreferredOrder(NutConfigurationFieldDescriptor descriptor) =>
        _schema.Fields.Where(field => field.EntryKind == descriptor.EntryKind && field.Scope == descriptor.Scope)
            .ToDictionary(field => field.Name, field => field.InsertionOrder, StringComparer.OrdinalIgnoreCase);

    private static NutConfigurationMutationResult Remove(NutConfigurationDocumentMutator mutator, NutConfigurationFieldDescriptor descriptor, string? section, int? occurrence) =>
        descriptor.EntryKind == NutConfigurationEntryKind.Assignment
            ? mutator.RemoveAssignment(descriptor.Name, section, occurrence)
            : mutator.RemoveDirective(descriptor.Name, section, occurrence);

    private static NutConfigurationMutationResult RemoveAllowMissing(NutConfigurationDocumentMutator mutator, NutConfigurationFieldDescriptor descriptor, string? section, int? occurrence)
    {
        var result = Remove(mutator, descriptor, section, occurrence);
        return result.Status == NutConfigurationMutationStatus.TargetNotFound ? NutConfigurationMutationResult.Success() : result;
    }

    private NutConfigurationFieldDescriptor? GetDescriptor(string semanticId)
    {
        ThrowIfDisposed();
        return _schema.FindField(semanticId);
    }

    private string? SafeCurrentValue(NutConfigurationFieldDescriptor descriptor, string? section, int? occurrence)
    {
        var document = Materialize();
        var matches = descriptor.EntryKind == NutConfigurationEntryKind.Assignment
            ? document.Nodes.OfType<NutConfigurationAssignmentNode>().Where(node => Equal(node.Name, descriptor.Name) && EqualSection(node.SectionName, section)).Select(node => node.Value).ToArray()
            : document.Nodes.OfType<NutConfigurationDirectiveNode>().Where(node => Equal(node.Name, descriptor.Name) && EqualSection(node.SectionName, section)).Select(node => node.Arguments).ToArray();
        if (descriptor.Sensitive) return matches.Length > 0 ? "Semantic.Sensitive.Configured" : null;
        var index = occurrence ?? (matches.Length == 1 ? 0 : -1);
        return index >= 0 && index < matches.Length ? matches[index] : null;
    }

    private NutConfigurationSemanticReviewItem ReviewFor(
        NutConfigurationFieldDescriptor descriptor,
        NutConfigurationSemanticChangeOperation operation,
        string? section,
        int? occurrence,
        string? oldValue,
        string? newValue,
        bool sensitive = false) =>
        new(FileKind, descriptor.SemanticId, descriptor.LabelResourceKey, operation, section,
            occurrence is null ? null : $"{descriptor.SemanticId}:{section ?? "global"}:{occurrence}",
            oldValue is null ? MissingState(descriptor) : NutConfigurationSemanticState.Explicit,
            NewState(descriptor, operation, newValue),
            sensitive ? null : oldValue, sensitive ? null : newValue, sensitive, descriptor.Activation);

    private NutConfigurationSemanticReviewItem GenericReview(
        string semanticId,
        NutConfigurationSemanticChangeOperation operation,
        string? section,
        string? oldValue,
        string? newValue) =>
        new(FileKind, semanticId, $"{semanticId}.Label", operation, section, null, null, null, oldValue, newValue, false, NutConfigurationActivation.None);

    private void RefreshState(NutConfigurationDocument? document = null)
    {
        document ??= MaterializeOrOriginal();
        Projection = _projector.Project(document, _schema, _context);
        if (_mutations.Any(mutation => mutation.Descriptor?.Sensitive == true))
        {
            var fields = Projection.Fields.Select(field =>
            {
                var mutation = _mutations.LastOrDefault(item => item.Descriptor?.SemanticId == field.Descriptor.SemanticId &&
                    EqualSection(item.Section, field.Section));
                return mutation?.Descriptor?.Sensitive == true
                    ? field with { SensitiveState = mutation.Kind == DraftMutationKind.Remove ? NutSensitiveFieldState.RemovalPending : NutSensitiveFieldState.ReplacementPending }
                    : field;
            }).ToArray();
            Projection = Projection with { Fields = fields };
        }
        Validation = _validator.Validate(document, Projection);
    }

    private NutConfigurationDocument MaterializeOrOriginal() => _mutations.Count == 0 ? _parser.Parse(FileKind, _originalText) : Materialize();
    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);
    private static NutConfigurationMutationResult NotFound() => new(NutConfigurationMutationStatus.TargetNotFound, "Semantic.Field.NotFound");
    private static NutConfigurationMutationResult Invalid() => new(NutConfigurationMutationStatus.ValidationFailed, "Semantic.Value.Invalid");
    private static NutConfigurationSemanticState MissingState(NutConfigurationFieldDescriptor descriptor) => descriptor.Required
        ? NutConfigurationSemanticState.MissingRequired
        : descriptor.AutomaticPolicy == NutConfigurationAutomaticPolicy.OmitDirective
            ? NutConfigurationSemanticState.AutomaticByOmission
            : NutConfigurationSemanticState.Explicit;
    private static NutConfigurationSemanticState? NewState(
        NutConfigurationFieldDescriptor descriptor,
        NutConfigurationSemanticChangeOperation operation,
        string? value) => operation switch
        {
            NutConfigurationSemanticChangeOperation.Remove => MissingState(descriptor),
            NutConfigurationSemanticChangeOperation.SetAutomatic when descriptor.AutomaticPolicy == NutConfigurationAutomaticPolicy.OmitDirective => NutConfigurationSemanticState.AutomaticByOmission,
            NutConfigurationSemanticChangeOperation.SetAutomatic when descriptor.AutomaticPolicy == NutConfigurationAutomaticPolicy.ExplicitAutoToken => NutConfigurationSemanticState.ExplicitAutoToken,
            NutConfigurationSemanticChangeOperation.Set when descriptor.AutomaticPolicy == NutConfigurationAutomaticPolicy.ExplicitAutoToken &&
                string.Equals(value, descriptor.ExplicitAutoToken, StringComparison.OrdinalIgnoreCase) => NutConfigurationSemanticState.ExplicitAutoToken,
            _ => NutConfigurationSemanticState.Explicit
        };
    private static bool Equal(string left, string right) => string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
    private static bool EqualSection(string? left, string? right) => left is null && right is null || left is not null && right is not null && Equal(left, right);

    private enum DraftMutationKind
    {
        Set, Automatic, Remove, AddRepeated, AddSection, RemoveSection, RenameSection, AddCustom, EditCustom, RemoveCustom,
        SetPreservingSecret, ReplaceEmbeddedSecret, AddRepeatedWithSecret
    }

    private sealed record DraftMutation(
        DraftMutationKind Kind,
        NutConfigurationFieldDescriptor? Descriptor = null,
        string? Section = null,
        int? Occurrence = null,
        string? Value = null,
        string? CustomName = null,
        NutConfigurationEntryKind CustomEntryKind = NutConfigurationEntryKind.Assignment,
        SensitivePayload? Sensitive = null,
        string? RowIdentity = null);

    private sealed class SensitivePayload(char[] value) : IDisposable
    {
        private char[]? _value = value;
        public string Reveal() => _value is null ? throw new ObjectDisposedException(nameof(SensitivePayload)) : new string(_value);
        public ReadOnlySpan<char> RevealSpan() => _value ?? throw new ObjectDisposedException(nameof(SensitivePayload));
        public void Dispose() { if (_value is null) return; Array.Clear(_value); _value = null; }
        public override string ToString() => "<sensitive>";
    }
}
