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

Assignment insertion is file-aware. `nut.conf` emits `KEY=value`, as required by the release grammar; assignment-oriented files retain their documented spaced form. Existing assignment nodes always preserve their original separator. Repeated nodes receive internal draft-lifetime identities that are never serialized, allowing add/edit/remove operations to target the same logical row after earlier rows shift occurrence indexes.

`NutConfigurationSemanticDraft` stores the immutable original text plus an intentional mutation plan. Every attempted operation is replayed on a fresh T13 parse before it is committed, providing atomic failure and deterministic materialization without shared mutable original/candidate documents. Semantic mutations are Set, Automatic, Remove/Unset, add/edit/remove repeated row, add/edit/remove custom parameter, and add/remove/rename section. Field, cross-field, and document validation contracts are separate and reuse the existing typed `ValidationSeverity`; pipeline validation remains T14.

Projection emits exactly `Explicit`, `AutomaticByOmission`, `ExplicitAutoToken`, `MissingRequired`, `Unsupported`, and `CustomUnknown`. Setting-specific policies are `OmitDirective`, `ExplicitAutoToken`, `DetectedAndPersisted`, and `NotSupported`. A context change can mark a value Unsupported but never removes it. Syntactically recognized unknown assignments/directives become limited-validation custom rows; comments, blank lines, and other raw nodes remain structural and untouched.

## Driver-aware and sensitive configuration

`ups.conf` schemas vary by selected driver and model connection type, port behavior, protocols, polling, battery options, documented flags, and contextual help. Built-in schemas derive only from primary NUT manpages or official driver help, require no runtime internet, and never invent defaults.

Existing secrets are never pre-filled into normal ViewModel state. Projection reports only change-only status (`NotConfigured`, `Configured`, `ReplacementPending`, or `RemovalPending`). Replacement enters through a disposable transient wrapper, is copied only into the private draft mutation payload, and is revealed only while materializing the internal candidate. Review values are null/redacted, `ToString()` is redacted, and the generated presentation consumes the established T14 redacted preview rather than candidate text. Unsupported content remains through graphical Advanced → Custom parameters rows (key/directive, value/arguments, scope, section) with lexical validation and a localized limited-semantic-validation warning.

### Two shapes of secret

**Whole-value secrets.** The descriptor is marked `Sensitive` and its entire value is the credential: an SNMP community, the password component of `CERTIDENT`, a `password = …` assignment in `upsd.users`. Projection replaces the value with `null` and reports only its state.

**Embedded positional secrets.** T28 added the case where a credential is one token inside a line whose other tokens are ordinary editable fields:

```text
MONITOR system powervalue username password role
```

Marking the whole value sensitive would make the row uneditable; leaving it non-sensitive would publish the password. `SecretTokenIndex` names the position instead. The projector blanks that token before the codec parses the line, so the record handed to Presentation has no field capable of holding it — `NutMonitorEntry` is `System`, `PowerValue`, `Username`, `Role`. The two flags are mutually exclusive: a descriptor cannot be both `Sensitive` and carry a `SecretTokenIndex`, and the schema rejects that combination at construction.

The governing principle is the same for both shapes: the sensitive token is never projected, the Core keeps it only for the duration of the operation that needs it, Presentation knows presence and state only, and replacement is change-only.

### Repeated-row mutations that preserve a credential

Editing a repeated row that carries an embedded secret would otherwise force the caller to supply the password again just to change a neighbouring value. Three mutations avoid that:

| Mutation | Purpose |
| --- | --- |
| `EditRepeatedPreservingSecret` | rewrites the visible tokens and carries the stored credential across unchanged |
| `ReplaceRepeatedSecret` | changes only the credential, leaving every other token as written |
| `AddRepeatedWithSecret` | appends a new row, which must supply a credential because there is none to preserve |

The architectural property is that editing the non-sensitive fields of a repeated row never requires revealing the existing credential. `NutEmbeddedSecret` is internal to Core and has no public surface; the document is threaded through the replay path rather than exposing the mutator's node list.

### Token lists versus text

A serializer must distinguish a text value that contains spaces from a semantic list of tokens. `SHUTDOWNCMD` is text and needs quoting when it contains spaces; `actions = SET FSD` is two permission tokens and quoting it would make NUT read one unknown permission named `SET FSD`. `ValueIsTokenList` marks the descriptors whose values are lists, and the mutator applies whitespace quoting only where the value is genuinely single text. Codecs that read a single argument accept both the quoted form found in a file and the bare value returned by an edit box, so a round trip through the interface does not corrupt a command.

`NutConfigurationGeneratedPreviewFactory` materializes a fresh T13 candidate snapshot and invokes `Prepare` on the supplied `INutConfigurationFilePipeline`. The App review presentation exposes only semantic items, issues, custom rows, and redacted preview lines; it has no write command and no editable generated text. The same call works with the local T14 pipeline and the existing T19 SFTP/T19B SMB pipeline. Applying a later form draft never restarts a service silently; activation metadata is informational and a service action remains separate, explicit, confirmed, and subject to the existing UAC boundary.

## Task boundaries

| Task | Scope | Status |
| --- | --- | --- |
| T25 | shared framework and representative descriptors | implemented |
| T26 | production driver-aware `ups.conf` form and documented `runtimecal` assistant | implemented |
| T27 | NUT 2.8.5 `nut.conf`/`upsd.conf` schemas and forms: `MODE`, repeated `LISTEN`, server behavior, TLS metadata, change-only `CERTIDENT` | implemented |
| T28 | `upsd.users`/`upsmon.conf` forms, embedded positional secrets, token lists | implemented |
| T29 | final graphical-configuration UX hardening | remaining |

Every descriptor uses invariant codecs, omission-specific defaults, activation metadata, and pure validation; no runtime or network probes occur. All five supported files now have a dedicated form, so the T15 entry model is a fallback rather than the normal path, and every graphical candidate converges on the same T14/T19/T19B pipeline.
