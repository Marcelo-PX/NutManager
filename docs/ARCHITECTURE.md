# NutManager Architecture

## 1. Architectural goals

- Windows-first desktop product with best-effort shared-code compatibility on Linux;
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
    └── Remote SSH/SFTP transport
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

`managed-servers.json` is schema-versioned, per-user metadata for managed profiles and the active profile. Schema v2 adds remote SSH port, user name, and a pinned host-key fingerprint/algorithm. It uses temporary-file, atomic persistence and never contains credentials; passwords, passphrases, and private-key material are session-only. T20 owns protected credential storage.

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

Local Windows management is implemented through T17. A Remote profile uses an explicit SSH/SFTP session with strict pinned-host-key verification; an unknown key requires an explicit trust action, and a mismatch is rejected. Remote configuration directories are manually browsed and validated without autodiscovery or local-management fallback. Remote profiles may read and prepare configuration changes after validation. Writes remain read-only by default and require both the profile's Manage policy and an explicit capability probe on a Windows/OpenSSH server. That probe uses same-directory temporary files and `File.Replace`; the remote pipeline preserves T14-style fingerprints, candidate verification, backup, post-write verification, and recovery rollback through fixed structured remote operations.

## 10. Windows-first CI and packaging

Official CI and package validation run on `windows-latest` only. Windows x64 is the official package and distribution target. Linux is not a CI gate and has no official package; shared code remains best-effort compatible until T22 evaluates it separately.

## 11. Error handling and logging

Infrastructure preserves technical errors; higher layers map them to actionable result categories and concise UI messages. Expected cancellation is not shown as a fault. Logs and diagnostic output must exclude passwords, complete secret-bearing configuration, and unsafe command details.

## 12. Upstream NUT workflow

The upstream NUT repository is not a project dependency or submodule. Approved upstream work reproduces and documents a limitation first, then uses a focused branch in `Marcelo-PX/nut` and follows NUT contribution, test, licensing, and DCO requirements.
