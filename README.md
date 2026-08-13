# NutManager

NutManager is a Windows-first desktop client and local administration interface for [Network UPS Tools (NUT)](https://github.com/networkupstools/nut), built with Avalonia and .NET.

> Project status: active development. Windows x64 monitoring, local and remote administration, the recoverable configuration pipeline, and dedicated graphical configuration for the supported NUT files are implemented. Final graphical-configuration UX hardening remains T29.

## Purpose

NutManager makes NUT monitoring, configuration, diagnostics, and explicitly confirmed local administration easier to use without replacing NUT drivers or its standard protocols and configuration formats.

## Current capabilities

### Monitoring

- NUT TCP monitoring, UPS discovery, telemetry, polling, reconnect, and stale-data behavior;
- deterministic mock mode and read-only diagnostics.

### Graphical configuration

All five supported files have a dedicated graphical form: `nut.conf`, `ups.conf`, `upsd.conf`, `upsd.users`, and `upsmon.conf`. Forms build a semantic draft that is validated, projected onto the syntax-preserving document, reviewed, and only then written.

Editing preserves comments, ordering, quoting, spacing, unknown directives, and unmanaged sections. Every write goes through the same pipeline: generated read-only preview with secrets redacted, backup, temporary-file validation, safe replacement, verification, and rollback on failure. Generated configuration text is never the primary editor, and applying a change never restarts a service.

### Persistence

- per-user application settings;
- managed NUT server profiles with an active profile and separate monitoring and management metadata.

### Windows local management

- local NUT installation detection;
- Windows service, UAC-boundary, ACL, process, and Event Log administration;
- passive COM-port enumeration and controlled NUT driver diagnostics.

### Remote management

Remote profiles can monitor through the standard NUT TCP connection and access configuration through SSH/SFTP or SMB. The user manually browses and validates the selected remote directory; no server/share autodiscovery or local-management fallback is used. SSH/SFTP host keys require explicit SHA-256 fingerprint trust/pinning. SMB accesses only a user-supplied UNC share and can use the current Windows identity or session-only explicit credentials.

Remote ReadOnly profiles can inspect configuration. Remote Manage profiles can write only after an explicit same-directory safe-write capability probe: Windows/OpenSSH for SSH/SFTP, or verified `File.Replace` behavior for SMB. Local, SFTP, and SMB use the same configuration architecture; the transport changes only readiness, path, and write implementation. Remote service, ACL, COM-port, and driver administration are not implemented.

### Secrets

Two separate domains:

- **transport credentials** — SSH and SMB secrets are session-only by default and may be explicitly remembered after a successful connection in Windows Credential Manager. Profile JSON contains only non-secret metadata, including an optional private-key path.
- **NUT configuration credentials** — passwords inside `upsd.users` and `upsmon.conf` are change-only. A stored value is never projected into the interface, review, or generated preview; the application reports only whether one is configured.

## Platform support

**Windows x64** is the only active and officially supported platform for development, CI, testing, packaging, distribution, and local administration.

**Linux** compatibility is deferred and may be reconsidered by a future task. It has no active CI, package, distribution, or administration-support commitment.

## Build

Official CI runs the following validation on `windows-latest`:

```bash
dotnet restore NutManager.sln
dotnet build NutManager.sln --configuration Release --no-restore
dotnet test NutManager.sln --configuration Release --no-build
```

## Package

The official package is `NutManager-win-x64.zip`, a self-contained Windows x64 archive. There is currently no installer, code signing, auto-update, or release automation. See [MVP package validation](docs/MVP-VALIDATION.md) for package acceptance guidance.

## Project documentation

- [Product specification](docs/SPEC.md)
- [Architecture](docs/ARCHITECTURE.md)
- [Implementation roadmap](docs/TASKS.md)
- [Graphical NUT configuration](docs/GRAPHICAL-NUT-CONFIGURATION.md)
- [Semantic configuration architecture](docs/SEMANTIC-CONFIGURATION-ARCHITECTURE.md)
- [UI design system](docs/UI-DESIGN-SYSTEM.md)
- [Localization](docs/LOCALIZATION.md)
- [Profile validation architecture](docs/PROFILE-VALIDATION-ARCHITECTURE.md)
- [MVP package validation](docs/MVP-VALIDATION.md) and [live validation findings](docs/LIVE-VALIDATION-FINDINGS.md) — historical acceptance records
- [Rules for coding agents](AGENTS.md)

## Upstream relationship

- Official NUT repository: `networkupstools/nut`
- Contributor fork used for approved upstream work: `Marcelo-PX/nut`
- NutManager repository: `Marcelo-PX/NutManager`

The NUT source tree is not a submodule and should be opened only for a task that explicitly requires upstream analysis or contribution work.

## License

NutManager is licensed under the GNU General Public License v2.0. See [LICENSE](LICENSE).

## Disclaimer

NutManager is an independent project and is not an official component of Network UPS Tools.
