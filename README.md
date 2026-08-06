# NutManager

A modern, cross-platform desktop manager for [Network UPS Tools (NUT)](https://github.com/networkupstools/nut), built with Avalonia and .NET.

> Project status: planning and initial implementation. The first milestone is a safe, read-only MVP.

## Purpose

NutManager aims to make NUT easier to configure, monitor, diagnose, and operate through a modern graphical interface on Windows and Linux.

The project is designed to work with standard NUT installations and public NUT interfaces. It must not require a private fork of NUT for normal use.

## Initial MVP

The first functional version will provide:

- Avalonia desktop interface for Windows and Linux;
- connection to a NUT server by host and port;
- discovery and selection of available UPS devices;
- read-only UPS telemetry;
- clear connection, stale-data, unavailable-data, and error states;
- a mock provider for UI development and automated tests;
- local application settings without administrative privileges.

The MVP will not edit NUT configuration files, control system services, access serial ports directly, or require elevation.

## Planned technology

- C# and .NET LTS;
- Avalonia UI with AXAML;
- MVVM using CommunityToolkit.Mvvm;
- xUnit for automated tests;
- platform-specific code isolated behind interfaces.

## Planned repository structure

```text
NutManager/
├── src/
│   ├── NutManager.App/
│   ├── NutManager.Core/
│   └── NutManager.Infrastructure/
├── tests/
│   └── NutManager.Tests/
├── docs/
│   ├── SPEC.md
│   ├── ARCHITECTURE.md
│   └── TASKS.md
├── AGENTS.md
└── NutManager.sln
```

## Project documentation

- [Product specification](docs/SPEC.md)
- [Architecture](docs/ARCHITECTURE.md)
- [Implementation plan](docs/TASKS.md)
- [Rules for coding agents](AGENTS.md)

## Upstream relationship

- Official NUT repository: `networkupstools/nut`
- Contributor fork used for upstream work: `Marcelo-PX/nut`
- NutManager repository: `Marcelo-PX/NutManager`

The NUT source tree is intentionally not included as a submodule. It should only be opened for a task that explicitly requires upstream analysis or contribution work.

## Build

Build instructions will be added after the initial Avalonia solution is created. The intended validation commands are:

```bash
dotnet restore
dotnet build
dotnet test
```

## License

NutManager is licensed under the GNU General Public License v2.0. See [LICENSE](LICENSE).

## Disclaimer

NutManager is an independent project and is not currently an official component of Network UPS Tools.
