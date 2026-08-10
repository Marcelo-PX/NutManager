# NutManager Product Specification

## 1. Product vision

NutManager is a Windows-first desktop interface for Network UPS Tools (NUT). It makes NUT monitoring, configuration, diagnostics, and explicitly confirmed administration understandable without replacing NUT drivers, protocols, or configuration formats.

Monitoring and management are distinct concerns. Monitoring uses standard NUT TCP access; management uses either local Windows capabilities or an explicit SSH/SFTP or SMB remote configuration transport.

## 2. Initial monitoring milestone

The initial MVP milestone established a non-administrative, read-only monitoring application. Its requirements remain the baseline for the package acceptance checklist:

- Avalonia desktop shell with Overview, Devices, Diagnostics, and Settings;
- light, dark, and system themes;
- NUT endpoint, timeout, polling, mock-mode, and preferred-UPS settings;
- bounded NUT TCP connection, discovery, telemetry, reconnect, and stale-data handling;
- deterministic mock data for development and automated tests;
- read-only diagnostics and per-user, non-secret settings.

Missing NUT variables remain unavailable rather than estimated. Unknown NUT status tokens and variable names are preserved. The T11 acceptance workflow remains read-only even though later work added separately confirmed administration.

## 3. Current implementation status

The monitoring base from T01–T10 is implemented. T11 is **DONE**; the distributed Windows package completed its manual live-NUT acceptance checklist.

The current product also implements:

- T12 local Windows NUT installation detection;
- T13 syntax-preserving NUT configuration documents;
- T14 previewed, recoverable configuration writes with backup and rollback;
- T15 graphical editing of existing configuration entries;
- T16 Windows service, privilege, ACL, process, and Event Log administration;
- T17 passive COM and controlled NUT-driver diagnostics;
- T18 managed local and remote server profiles.
- T19 SSH/SFTP remote configuration management.
- T19B SMB remote configuration transport.
- T20 opt-in protected SSH and SMB credential storage in Windows Credential Manager.

## 4. Platform and quality requirements

### NFR-001 — Windows-first platform strategy

Windows x64 is the only active and officially supported platform for development, CI, current validation, packaging, distribution, and local administration. Linux compatibility is deferred for possible future evaluation; it has no active CI gate, package, or administration-support commitment.

The shared architecture must avoid unnecessary Windows dependencies, and platform APIs must remain behind the Windows adapter boundary.

### Reliability, security, and testability

- External I/O requires cancellation, bounded timeouts, and controlled errors.
- Core behavior must be testable without Avalonia, real hardware, elevation, or network access.
- Secrets must not appear in logs or UI state.
- Platform-specific actions must be explicit and isolated.
- Accessibility must not rely on color alone.

## 5. Administration safety

The normal NutManager process does not require Administrator privileges. Privileged Windows actions are explicitly prepared, reviewed, confirmed, and routed through a limited UAC helper boundary when required.

Configuration changes use a syntax-preserving model and a recoverable write pipeline: review and diff, backup, temporary-file validation, safe replacement, verification, and rollback. Administration is never automatic, and applying configuration does not automatically restart a NUT service. Monitoring remains independent of management actions.

## 6. Managed profiles and remote boundary

Managed profiles separate monitoring from management metadata:

- monitoring stores the NUT TCP host, port, and optional preferred UPS;
- management is Local or Remote and has an explicit access mode.

A remote profile can monitor through NUT TCP and manage configuration only through an explicit SSH/SFTP or SMB file session; it never falls back to local management. The user manually browses and validates the remote directory, with no server/share autodiscovery. SSH host keys use explicit SHA-256 fingerprint pinning; an unknown key requires review and a mismatch is rejected. SMB uses an explicit UNC share and current-identity or session-only explicit authentication. A successful explicit connection may opt in to save only its secret in Windows Credential Manager; the profile JSON stores no secret.

Remote ReadOnly profiles can inspect configuration. Remote Manage profiles can write only after an explicit safe-write capability probe: Windows/OpenSSH for SSH/SFTP or verified `File.Replace` behavior for SMB. Remote service control, ACL, COM-port, and driver administration remain unavailable.

## 7. Post-MVP capability status

### Implemented

- managed server profiles;
- syntax-preserving configuration parsing and graphical editing;
- preview, backup, safe write, recovery, and rollback;
- Windows local service, UAC, ACL, process, Event Log, COM, and driver diagnostics.
- SSH/SFTP and SMB remote configuration management with manual directory validation; SSH uses pinned host keys and SMB uses a share-root boundary.

### Next

- T21 full local and remote Windows validation.

### Later

- T22 deferred Linux compatibility evaluation;
- T23 upstream NUT improvement evaluation;
- multi-server simultaneous runtime, history, notifications, and other future product capabilities as separately scoped.

## 8. MVP package acceptance

The MVP package was accepted after the Windows x64 archive was manually validated against a real NUT server using the read-only checklist in [MVP-VALIDATION.md](MVP-VALIDATION.md). T11 is done; the checklist remains as a regression record.

## 9. Upstream strategy

NutManager documents and reproduces limitations before proposing upstream NUT work. Approved upstream work uses the official `networkupstools/nut` repository and focused branches in `Marcelo-PX/nut`; the upstream source tree is not embedded in the normal NutManager workspace.
