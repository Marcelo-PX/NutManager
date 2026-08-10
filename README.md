# NutManager

NutManager is a Windows-first desktop client and local administration interface for [Network UPS Tools (NUT)](https://github.com/networkupstools/nut), built with Avalonia and .NET.

> Project status: active development. The monitoring MVP and Windows local-administration foundations are implemented; live package acceptance remains in progress.

## Purpose

NutManager makes NUT monitoring, configuration, diagnostics, and explicitly confirmed local administration easier to use without replacing NUT drivers or its standard protocols and configuration formats.

## Current capabilities

### Monitoring

- NUT TCP monitoring, UPS discovery, telemetry, polling, reconnect, and stale-data behavior;
- deterministic mock mode and read-only diagnostics.

### Persistence

- per-user application settings;
- managed NUT server profiles with an active profile and separate monitoring and management metadata.

### Windows local management

- local NUT installation detection;
- syntax-preserving configuration editing with review, backup, safe replacement, and rollback;
- Windows service, UAC-boundary, ACL, process, and Event Log administration;
- passive COM-port enumeration and controlled NUT driver diagnostics.

### Remote profiles

Remote profiles can monitor through the standard NUT TCP connection and access configuration through SSH/SFTP or SMB. The user manually browses and validates the selected remote directory; no server/share autodiscovery or local-management fallback is used. SSH/SFTP host keys require explicit SHA-256 fingerprint trust/pinning. SMB accesses only a user-supplied UNC share and can use the current Windows identity or session-only explicit credentials.

Remote ReadOnly profiles can inspect configuration. Remote Manage profiles can write only after an explicit same-directory safe-write capability probe: Windows/OpenSSH for SSH/SFTP, or verified `File.Replace` behavior for SMB. Passwords, passphrases, and private-key paths remain session-only until T20, which will cover protected SSH and SMB credentials. Remote service, ACL, COM-port, and driver administration are not implemented.

## Platform support

**Windows x64** is the official and primary platform for development, CI, testing, packaging, distribution, and local administration.

**Linux** remains secondary, best-effort compatibility for shared code. It has no official CI gate, package, or current administration-support guarantee; T22 will evaluate it explicitly.

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
- [Implementation plan](docs/TASKS.md)
- [MVP package validation](docs/MVP-VALIDATION.md)
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
