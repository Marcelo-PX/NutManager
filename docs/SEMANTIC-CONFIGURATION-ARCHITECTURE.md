# Semantic Graphical NUT Configuration Architecture

## Planned authoritative flow

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

Core will own concepts equivalent to `NutConfigurationSchemaRegistry`, `NutConfigurationFileSchema`, `NutConfigurationSectionSchema`, `NutConfigurationFieldDescriptor`, `NutConfigurationFieldKind`, `NutConfigurationValuePolicy`, `NutConfigurationValidationRule`, and `NutDriverConfigurationSchema`. Core remains independent of Avalonia.

Each descriptor declares file kind; global, section, or repeated-row scope; directive/key; stable semantic ID; label/help resource keys; control kind; parser/serializer; optional/repeated/sensitive flags; Automatic policy; applicability; validation; preferred insertion order; and known activation/restart metadata.

Projection reports `Explicit`, `AutomaticByOmission`, `ExplicitAutoToken`, `MissingRequired`, `Unsupported`, or `CustomUnknown`. This prevents conflating an absent directive, a textual `auto`, a required missing value, and an inapplicable setting.

Automatic is per-setting: `OmitDirective`, `ExplicitAutoToken`, `DetectedAndPersisted`, or `NotSupported`. It is never universally "remove the line." `driver` is required in `ups.conf`; driver detection recommends a concrete driver that the user confirms and persists. Port and protocol automatic behavior appears only where official driver documentation supports it.

## T13 extension and mutations

T25 explicitly adds deterministic insert-assignment/directive, remove-managed-assignment/directive, add/remove section, safe section rename, repeated entries, and deterministic insertion position. It preserves comments, ordering, unknown directives, raw nodes, quoting, line endings, encoding, duplicates, and unrelated content. A managed edit must not reformat the entire file.

Semantic mutations are Set, Add, Remove/Unset, Add/Remove repeated row, Add/Remove section, and Rename section. Field, cross-field, document, and pipeline validation are separate. Cross-field examples include driver/port, driver/protocol, authentication requirements, and `runtimecal` ordering.

## Driver-aware and sensitive configuration

`ups.conf` schemas vary by selected driver and model connection type, port behavior, protocols, polling, battery options, documented flags, and contextual help. Built-in schemas derive only from primary NUT manpages or official driver help, require no runtime internet, and never invent defaults.

Existing secrets are never pre-filled into normal ViewModel state. Password replacement is change-only; status is Configured/Not configured; reviews show `<redacted>`; logs contain no secrets. Unsupported content remains through graphical Advanced → Custom parameters rows (key, value, scope) with a limited-semantic-validation warning.

The generated file is read-only. Applying a draft never restarts a service silently; a later service action remains separate, explicit, confirmed, and subject to the existing UAC boundary.
