# Managed server profile validation architecture

## Purpose and boundary

T24A plans typed, reusable validation for managed-server profile drafts before persistence. A draft is presentation state, not a persisted profile. Validation never resolves DNS or opens a connection during ordinary editing; Test Connection is an explicit operational workflow.

## Validation levels

1. **Syntactic** — pure, deterministic checks for host, port, profile name, UNC, SSH port, and character/range formats.
2. **Semantic/cross-field** — profile consistency after syntax is valid: Remote/SFTP needs a management host; Remote/SMB needs a UNC share; explicit SMB credentials need a username; SSH private-key mode needs a key path; Local does not persist remote metadata; profile names are unique.
3. **Operational** — explicit I/O: host format, DNS when applicable, TCP connect, NUT protocol response, `LIST UPS`, then preferred-UPS presence. Operational failure does not make a syntactically valid host invalid. Diagnostics and logs exclude secrets.

## Host and port rules

A host represents only a network host. A pure Core validator accepts IPv4, IPv6, single-label hostnames, and DNS/FQDN names. It rejects `UPS@host`, `user@host`, schemes, `host:3493`, UNC paths, slash paths/URLs, whitespace, and control characters. IPv6 zone/scope syntax is admitted only if the chosen .NET/Windows implementation can support it deterministically with tests. The syntactic validator does not perform DNS.

Ports are `1..65535`. UI controls should be numeric, but Core retains the invariant.

The implementation may use concepts equivalent to `ValidationSeverity` (`Info`, `Warning`, `Error`), `FieldValidationIssue` (code, severity, resource key), and `FieldValidationResult<T>` (value, issues, validity). This is deliberately a small reusable model, not a value object for every string.

## UX and localization

Errors validate inline without blocking partial typing, but an Error disables Save. Warnings permit Save; Info provides help. Local/Remote and SFTP/SMB selection is reversible while drafting. Dirty state offers Save, Discard, or Continue editing rather than trapping a draft. The active-profile restart requirement is a first-class visible state.

Validation and option labels are localized in both official cultures with semantic keys such as `Validation.Host.Required`, `Validation.Host.Invalid`, `Validation.Host.NoScheme`, `Validation.Port.Range`, `Validation.Profile.NameDuplicate`, `Validation.Remote.ManagementHostRequired`, and `Validation.Smb.ShareRootRequired`. NUT technical tokens remain invariant.

## Source-of-truth and mock policy

The planned T24A migration separates application preferences (theme, language, sidebar/review state, polling, timeout, mock/demo preference) from managed profiles (monitoring endpoint, preferred UPS, and management metadata). Legacy endpoint fields remain migration compatibility only after schema evolution; no migration is implemented by this documentation task.

For new normal installations, target policy is mock mode disabled. Existing installations preserve their persisted choice during migration. Active mock/demo mode must be persistently and unambiguously labelled.

## Validation expectations

T24A tests host/port parsing, cross-field validation, Local↔Remote and SFTP↔SMB reversibility, dirty-draft decisions, settings migration, connection-tester fakes, and pt-BR/en-US resource completeness. No test uses real DNS, credentials, NUT, service, serial port, or remote transport.
