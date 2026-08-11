# Semantic Graphical NUT Configuration Architecture

## Implemented authoritative flow

T25 moves from T15's existing-entry editor to complete graphical configuration without creating another writer:

```text
Graphical Form
    → Semantic Draft
    → Semantic Schema and Validation
    → Syntax-Preserving Document (T13)
    → Semantic Review and Generated Preview
    → Existing Safe Write Pipeline (T14)
    → Local / SFTP / SMB
```

Views never write `.conf` files. T14 remains responsible for fingerprints, candidate preparation, preview, backup, temporary write, validation, safe replacement, verification, rollback, and recovery paths. T19 and T19B remain the remote transport and remote safe-write boundaries.

## Core schema and projection

Core owns `NutConfigurationSchemaRegistry`, file/section/field descriptors, invariant value codecs, semantic context, layered validation contracts, and `NutDriverConfigurationSchema`. The registry is immutable after construction and rejects duplicate file kinds, semantic IDs, conflicting entry descriptors, and duplicate driver IDs. Core remains independent of Avalonia and I/O.

Each descriptor declares file kind; global, section, or repeated-row scope; directive/key; stable semantic ID; label/help resource keys; control kind; parser/serializer; optional/repeated/sensitive flags; Automatic policy; applicability; validation; preferred insertion order; and known activation/restart metadata.

Projection reports `Explicit`, `AutomaticByOmission`, `ExplicitAutoToken`, `MissingRequired`, `Unsupported`, or `CustomUnknown`. This prevents conflating an absent directive, a textual `auto`, a required missing value, and an inapplicable setting.

Automatic is per-setting: `OmitDirective`, `ExplicitAutoToken`, `DetectedAndPersisted`, or `NotSupported`. It is never universally "remove the line." `driver` is required in `ups.conf`; driver detection recommends a concrete driver that the user confirms and persists. Port and protocol automatic behavior appears only where official driver documentation supports it.

## T13 extension and mutations

T25 adds the intentional `NutConfigurationDocumentMutator` boundary for set/insert/remove assignment, set/insert/remove directive, add/remove/rename section, and add/edit/remove repeated rows. Callers never receive a mutable node list. Existing values retain indentation, separator spacing, quote style, trailing whitespace, and line endings. Structural insertion uses local/dominant line endings and descriptor order without moving existing unknown nodes; section removal uses the exact section range. Duplicates are retained and ambiguous singleton operations fail closed.

`NutConfigurationSemanticDraft` stores the immutable original text plus an intentional mutation plan. Every attempted operation is replayed on a fresh T13 parse before it is committed, providing atomic failure and deterministic materialization without shared mutable original/candidate documents. Semantic mutations are Set, Automatic, Remove/Unset, add/edit/remove repeated row, add/edit/remove custom parameter, and add/remove/rename section. Field, cross-field, and document validation contracts are separate and reuse the existing typed `ValidationSeverity`; pipeline validation remains T14.

Projection emits exactly `Explicit`, `AutomaticByOmission`, `ExplicitAutoToken`, `MissingRequired`, `Unsupported`, and `CustomUnknown`. Setting-specific policies are `OmitDirective`, `ExplicitAutoToken`, `DetectedAndPersisted`, and `NotSupported`. A context change can mark a value Unsupported but never removes it. Syntactically recognized unknown assignments/directives become limited-validation custom rows; comments, blank lines, and other raw nodes remain structural and untouched.

## Driver-aware and sensitive configuration

`ups.conf` schemas vary by selected driver and model connection type, port behavior, protocols, polling, battery options, documented flags, and contextual help. Built-in schemas derive only from primary NUT manpages or official driver help, require no runtime internet, and never invent defaults.

Existing secrets are never pre-filled into normal ViewModel state. Projection reports only change-only status (`NotConfigured`, `Configured`, `ReplacementPending`, or `RemovalPending`). Replacement enters through a disposable transient wrapper, is copied only into the private draft mutation payload, and is revealed only while materializing the internal candidate. Review values are null/redacted, `ToString()` is redacted, and the generated presentation consumes the established T14 redacted preview rather than candidate text. Unsupported content remains through graphical Advanced → Custom parameters rows (key/directive, value/arguments, scope, section) with lexical validation and a localized limited-semantic-validation warning.

`NutConfigurationGeneratedPreviewFactory` materializes a fresh T13 candidate snapshot and invokes `Prepare` on the supplied `INutConfigurationFilePipeline`. The App review presentation exposes only semantic items, issues, custom rows, and redacted preview lines; it has no write command and no editable generated text. The same call works with the local T14 pipeline and the existing T19 SFTP/T19B SMB pipeline. Applying a later form draft never restarts a service silently; activation metadata is informational and a service action remains separate, explicit, confirmed, and subject to the existing UAC boundary.

## Task boundaries

T25 supplies representative production descriptors for established entries only and framework fixtures for policy behavior. T26 owns complete driver-aware `ups.conf` forms and `runtimecal`; T27 owns final `nut.conf`/`upsd.conf` forms; T28 owns final `upsd.users`/`upsmon.conf` and credential UX; T29 owns final graphical-configuration hardening. The T15 existing-entry editor remains available until those forms replace its presentation coverage.
