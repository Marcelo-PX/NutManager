# NutManager Architecture

## 1. Architectural goals

- Windows x64 desktop product as the only active and supported target; Linux compatibility is deferred;
- safe monitoring, configuration, and local administration boundaries;
- clear separation of domain, UI, protocol, persistence, and operating-system concerns;
- deterministic tests without a real UPS, NUT server, service, serial port, or elevation;
- minimal dependencies and focused platform adapters.

## 2. Selected stack and solution structure

NutManager uses C#, .NET 10, Avalonia with AXAML, CommunityToolkit.Mvvm, xUnit, and `System.Text.Json` for its own persistence. Package versions are centrally managed through `Directory.Packages.props`.

```text
NutManager/
├── src/
│   ├── NutManager.App/
│   ├── NutManager.Core/
│   └── NutManager.Infrastructure/
├── tests/NutManager.Tests/
└── docs/
```

## 3. Dependency direction

```text
NutManager.App
    ├──> NutManager.Core
    └──> NutManager.Infrastructure

NutManager.Infrastructure
    └──> NutManager.Core

NutManager.Core
    └──> no UI or platform project
```

Core contains deterministic models, contracts, validation, status, and operation results. It must not reference Avalonia, Windows APIs, file-system APIs, sockets, serial ports, or service-control APIs. Infrastructure implements I/O and platform boundaries. App contains Avalonia startup, composition, views, and view models; views and view models do not directly execute NUT commands or operating-system actions.

## 4. Product capability split and managed profiles

```text
NutManager
├── Monitoring
│   └── NUT TCP protocol
└── Management
    ├── Local Windows adapter
    └── Remote configuration transports: SSH/SFTP or SMB
```

The managed-profile model is implemented through `ManagedNutServerProfile`, `NutMonitoringProfile`, `NutManagementProfile`, `ManagedNutServerProfiles`, `ManagedServerCapabilities`, and `ManagedNutServerRuntimeContext`.

Each profile separates:

```text
Profile
├── Monitoring: host, port, preferred UPS
└── Management: Local or Remote, ReadOnly or Manage
```

The active profile is resolved during bootstrap into an immutable runtime context. Changing the active profile persists the selection and requires restart; polling is not silently redirected during a live session.

## 5. Persistence

`settings.json` is per-user UTF-8 JSON for polling, timeout, theme, mock-mode, and legacy monitoring compatibility fields. It uses temporary-file, atomic persistence and has no secrets in its current model.

`managed-servers.json` is schema-versioned, per-user metadata for managed profiles and the active profile. Schema v4 retains SSH/SMB metadata and adds non-secret SSH authentication mode and optional private-key path. It uses temporary-file, atomic persistence and never contains passwords, passphrases, or private-key material. Those values are session-only by default and, only after an explicit successful connection, may be saved in app-owned `CRED_TYPE_GENERIC` Windows Credential Manager entries with local-machine persistence.

These stores do not use backup or rollback semantics. Backup, recoverable replacement, and rollback belong to the T14 configuration-file pipeline.

## 6. Monitoring

Monitoring uses the read-only NUT TCP protocol, normally on port `3493`, rather than launching `upsc` for polling. The protocol layer supports UPS discovery, variable snapshots, bounded timeout and cancellation behavior, and controlled protocol errors. Polling permits one active operation per selected UPS, preserves the last successful snapshot on failures, and marks it stale rather than fabricating values.

Mock data is deterministic and visibly simulated. Protocol and polling tests use fakes or an in-process server rather than a real NUT server or UPS.

## 7. Configuration architecture

Configuration management is implemented for `nut.conf`, `ups.conf`, `upsd.conf`, `upsd.users`, and `upsmon.conf`. The syntax-preserving document model retains comments, order, unknown directives, unmanaged sections, quoting, and relevant formatting.

The write pipeline is:

```text
read → parse → requested in-memory change → preview/diff → backup
→ temporary write → validation → safe replacement → verification → rollback on failure
```

The graphical editor changes existing entries one file at a time and sends writes exclusively through the pipeline. It does not automatically activate, reload, or restart services after an apply.

### Planned semantic graphical configuration (T24–T29)

T15 is the current editor for existing entries. T25 will add a Core semantic schema, projection, validation, and mutation layer above the T13 syntax-preserving document so graphical forms can add, remove, rename, and reorder managed configuration deterministically without disturbing unmanaged syntax. The planned flow is graphical form → semantic draft → semantic schema/validation → T13 document → semantic review/generated preview → the existing T14 safe-write pipeline → Local/SFTP/SMB.

Descriptors will be platform-neutral Core data: stable semantic IDs, file/scope/directive identity, localized label/help keys, parser/serializer, control kind, validation, sensitive/repeated/optional metadata, Automatic policy, applicability, insertion order, and known activation metadata. Driver-aware `ups.conf` schemas use official NUT manpages and driver help only; no runtime internet dependency or guessed default is permitted. See [Semantic configuration architecture](SEMANTIC-CONFIGURATION-ARCHITECTURE.md) and [Graphical NUT configuration](GRAPHICAL-NUT-CONFIGURATION.md).

## 7.1 Planned presentation and localization architecture

T24 is a presentation foundation, not a change to management boundaries. The Windows-first Fluent-inspired shell gains reusable design tokens, a textual connection indicator, Wide/Medium/Compact layouts, Expanded/Collapsed/Overlay navigation, and Hidden/Collapsed/Expanded/Overlay review drawer states. Current hard-coded presentation is not retroactively described as localized.

`pt-BR` is the default culture and `en-US` is an official culture. New T24–T29 user-facing strings use semantic resource keys. Display values follow UI culture; every NUT parser and serializer remains culture-invariant. NUT filenames, directives, driver names, status tokens, and SFTP stay invariant. See [UI design system](UI-DESIGN-SYSTEM.md) and [Localization](LOCALIZATION.md).

## 8. Windows local administration

Windows-specific behavior remains in `Infrastructure.Platform.Windows` behind Core contracts:

```text
Normal desktop process
    → explicit review and confirmation
    → limited privileged boundary when needed
    → Windows adapter result
```

The adapter implements local installation detection, service metadata and control, UAC helper handling, conservative ACL assessment and repair, process and Event Log inspection, passive COM metadata, and controlled NUT driver diagnostics. Core remains platform-neutral.

## 9. Local and remote management boundary

Local Windows management is implemented through T17. A Remote profile explicitly selects SSH/SFTP or SMB for configuration files; neither transport accesses a remote NutManager instance. SSH/SFTP uses strict pinned-host-key verification. SMB uses only a manually supplied UNC share and either the current Windows identity or a session-scoped isolated Windows outbound identity created with `LOGON_NEW_CREDENTIALS`; it owns no global WNet connection and never maps a drive, disconnects a redirector connection, or discovers shares. Explicit SMB passwords are converted only for the native logon boundary and the resulting token is disposed when the session ends. A user may opt in after successful authentication to remember SSH or explicit-SMB secrets in Windows Credential Manager; the Core contract exposes only profile ID, fixed credential kind, and disposable secret buffers. Remote profiles may read and prepare configuration changes after validation. Writes remain read-only by default and require both the profile's Manage policy and an exact-directory capability probe: Windows/OpenSSH safe replacement for SSH/SFTP, or verified UNC `File.Replace` semantics for SMB. The transport-neutral remote pipeline preserves T14-style fingerprints, candidate verification, reserved generated backups, post-write verification, rollback, and recovery paths.

## 10. Windows-first CI and packaging

Official CI and package validation run on `windows-latest` only. Windows x64 is the only active, supported package and distribution target. Linux compatibility is deferred; it is not a CI gate and has no active package or administration support.

## 11. Error handling and logging

Infrastructure preserves technical errors; higher layers map them to actionable result categories and concise UI messages. Expected cancellation is not shown as a fault. Logs and diagnostic output must exclude passwords, complete secret-bearing configuration, and unsafe command details.

## 12. Upstream NUT workflow

The upstream NUT repository is not a project dependency or submodule. Approved upstream work reproduces and documents a limitation first, then uses a focused branch in `Marcelo-PX/nut` and follows NUT contribution, test, licensing, and DCO requirements.
