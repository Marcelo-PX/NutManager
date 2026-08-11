# Managed server profile validation architecture

## Purpose and boundary

T24A implements typed, reusable validation for managed-server profile drafts before persistence. A draft is reversible App presentation state, not a persisted profile. Ordinary editing never resolves DNS, opens a connection, reads a private key, or acquires a secret. Test Connection is a separate explicit operational workflow.

## Implemented validation levels

1. **Syntactic** — `NutManager.Core.Validation` contains pure deterministic host, port, profile-name, UNC, optional-text, and range checks. `ValidationSeverity`, `FieldValidationIssue`, and `FieldValidationResult<T>` carry stable codes and localization resource keys; Core contains no human-language validation messages.
2. **Semantic/cross-field** — `ManagedNutServerProfileValidator` validates the applicable draft branch and materializes a domain profile only with no Error. Local drops remote metadata; SFTP drops SMB metadata; SMB drops SSH metadata. Warnings remain saveable.
3. **Operational** — `IManagedNutConnectionTester` and `ManagedNutConnectionTester` execute the existing read-only NUT `LIST UPS` client only after explicit user action. Results distinguish success, unreachable endpoint, timeout, protocol/server error, no UPS, missing preferred UPS, cancellation, and generic failure.

## Host, port, and UNC rules

A host represents only a network host. The Core validator accepts IPv4, IPv6 without a scope identifier, single-label hostnames, and DNS/FQDN names. It rejects `UPS@host`, `user@host`, schemes, `host:3493`, UNC paths, slash paths, filesystem paths, embedded whitespace, controls, and partially supported IPv6 scope syntax. It does not perform DNS.

TCP and SSH ports are parsed from draft text without throwing and must be `1..65535`. A partial or invalid value remains editable but produces an Error. SMB share roots must be exact `\\server\share` UNC roots; optional SMB configuration directories are checked by the existing share-containment boundary.

Profile names are trimmed, limited to 80 characters, reject controls, and are unique case-insensitively. The profile being edited is excluded from its own uniqueness check, and its stable ID is preserved.

## Draft and persistence boundary

Settings exposes one **New server** flow. Local/Remote, ReadOnly/Manage, and SFTP/SMB selections are reversible; inactive typed values remain in the in-memory draft for convenient switching. Materialization persists only the selected branch. Passwords and passphrases never enter the draft.

Replacing a dirty editor context creates a pending action and requires Save, Discard, or Continue editing. Save routes through `ManagedNutServerProfileUpdateService`, retaining its concurrency checks, trusted-host-key behavior, and protected-credential invalidation rules. A failed Save leaves the draft available.

The runtime profile ID captured at startup remains immutable for the session. The persisted active ID may change, and their difference produces the localized restart-required state; polling and the shell continue to represent the startup runtime profile.

## Settings source of truth

`ApplicationSettings` schema v3 stores application preferences only: polling, connection timeout, theme, mock mode, language, and sidebar preference. Current serialization does not write Host, Port, or Preferred UPS. `JsonApplicationSettingsStore` can still read schemas v1/v2 and exposes their endpoint through an explicit compatibility payload used only by `ManagedNutServerBootstrapper` when no managed-profile document exists. A valid managed-profile document always wins and is never rewritten by migration.

New installations default mock mode to disabled. Existing explicit legacy `true` and `false` values are preserved. Malformed settings and managed-profile files are reported and are not silently overwritten. No secret is migrated.

## UX, localization, and tests

Settings uses the T24 cards, product-owned selection, responsive list/editor layout, inline textual issues, localized presentation options, active-profile badge, restart banner, and explicit Test Connection result. Official `pt-BR` and `en-US` resources have exact key parity; NUT technical tokens remain invariant.

Tests are deterministic and use pure validators, stores, credential fakes, and connection-tester fakes. No test performs DNS, external network access, credential mutation, configuration write, service/ACL/UAC operation, or hardware access.
