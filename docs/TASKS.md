# NutManager Implementation Tasks

## Status legend

- `TODO` — not started;
- `READY` — specified and ready for implementation;
- `IN PROGRESS` — currently assigned;
- `BLOCKED` — waiting on a dependency or decision;
- `DONE` — implemented and validated.

Only one task should normally be in progress at a time.

## Roadmap

| ID | Status | Task | Primary outcome |
|---|---|---|---|
| T01 | DONE | Create Avalonia solution | Compilable project skeleton |
| T02 | DONE | Build visual shell and navigation | Modern themed application shell |
| T03 | DONE | Define domain models | Stable UPS and connection models |
| T04 | DONE | Implement mock provider | Deterministic simulated scenarios |
| T05 | DONE | Build overview dashboard | Functional UI using mock data |
| T06 | DONE | Implement read-only NUT client | TCP protocol client with tests |
| T07 | DONE | Add UPS discovery and selection | Device listing and details |
| T08 | DONE | Persist local settings | Atomic per-user settings storage |
| T09 | DONE | Add polling and stale-data handling | Robust refresh and reconnect behavior |
| T10 | DONE | Complete MVP diagnostics | Read-only diagnostics page |
| T11 | IN PROGRESS | Package and validate MVP | Official Windows x64 package and live Windows NUT validation |
| T12 | DONE | Detect local NUT installation on Windows | Autodetect installation, executables, version, and configuration directory |
| T13 | DONE | Design syntax-preserving NUT configuration model | Safe model for managed and unmanaged configuration content |
| T14 | DONE | Add configuration backup, write, and rollback pipeline | Previewed, validated, recoverable configuration changes |
| T15 | DONE | Build graphical NUT configuration editor | Windows-first configuration experience |
| T16 | DONE | Add Windows service, UAC, and ACL administration | Explicitly confirmed local administrative actions |
| T17 | DONE | Add Windows COM-port and driver workflows | Local device and driver diagnostics |
| T18 | READY | Add managed server profiles | Separate local and remote monitoring and management profiles |
| T19 | TODO | Add remote SSH/SFTP management | Manual remote directory selection and secure management transport |
| T20 | TODO | Add secure credential storage | Protected remote-management credentials |
| T21 | TODO | Validate full Windows local and remote administration | End-to-end Windows-first validation |
| T22 | TODO | Evaluate Linux administrative compatibility | Secondary, best-effort compatibility assessment |
| T23 | TODO | Evaluate upstream NUT improvements | Focused issues and PR candidates |

---

## T01 — Create Avalonia solution

**Status:** DONE

### Objective

Create the minimal compilable solution and project references without implementing product behavior.

### Allowed scope

- root solution and shared build files;
- `src/NutManager.App`;
- `src/NutManager.Core`;
- `src/NutManager.Infrastructure`;
- `tests/NutManager.Tests`;
- minimal README build instructions if required.

### Requirements

- create `NutManager.sln`;
- create an Avalonia Desktop application project;
- create Core and Infrastructure class libraries;
- create an xUnit test project;
- reference Core and Infrastructure from App;
- reference Core from Infrastructure;
- reference tested projects from the test project as required;
- add CommunityToolkit.Mvvm;
- create `Directory.Build.props` and `Directory.Packages.props`;
- enable nullable reference types and implicit usings;
- use the Avalonia Fluent theme;
- create a minimal main window showing `NUT Manager`;
- keep build warnings introduced by project code at zero.

### Do not

- implement a NUT client;
- create production domain models;
- add dependency injection unless required by the template;
- implement navigation beyond what the template minimally needs;
- add service control, configuration parsing, serial access, backups, charts, installer, or platform-specific code;
- add the NUT upstream repository to the workspace.

### Validation

```bash
dotnet restore
dotnet build
dotnet test
```

### Completion criteria

- all three validation commands succeed;
- the application project starts and displays the minimal window;
- the agent reports created files and stops.

---

## T02 — Build visual shell and navigation

**Status:** DONE

Create the modern application frame, side navigation, Overview, Devices, Diagnostics, and Settings placeholders, plus persisted theme selection. No NUT data access.

## T03 — Define domain models

**Status:** DONE

Create Core models and status parsing contracts for endpoints, UPS identity, variables, snapshots, connection state, freshness, and diagnostics. Include unit tests. No network access.

## T04 — Implement mock provider

**Status:** DONE

Implement deterministic simulated scenarios defined in the architecture and expose them through the same application-facing abstraction intended for live data.

## T05 — Build overview dashboard

**Status:** DONE

Bind the overview UI to mock data. Include clear simulated-data labeling, accessible state presentation, missing-value rendering, and responsive metric cards.

## T06 — Implement read-only NUT client

**Status:** DONE

Implement the minimum TCP protocol commands needed to list UPS devices and fetch variables. Include cancellation, timeout, partial-read, malformed-reply, and fake-server tests.

## T07 — Add UPS discovery and selection

**Status:** DONE

Connect the Devices page to the NUT client, list exposed UPS devices, retain selection, and provide a raw variable details view.

## T08 — Persist local settings

**Status:** DONE

Persist non-secret endpoint, preferred UPS, polling, timeout, theme, and mock-mode settings per user using versioned atomic JSON storage.

## T09 — Add polling and stale-data handling

**Status:** DONE

Add bounded asynchronous polling, cancellation, reconnect behavior, stale snapshot retention, and timestamps without busy loops.

## T10 — Complete MVP diagnostics

**Status:** DONE

Expose read-only endpoint, connection, discovery, polling, version, and application-error diagnostics. No service or serial operations.

## T11 — Package and validate MVP

**Status:** IN PROGRESS

Produce and test the official self-contained Windows x64 package. Windows is the primary development, validation, and distribution platform; Linux has secondary, best-effort shared-code compatibility and no official package. T11 remains in progress until real Windows NUT validation is complete.

## T12 — Detect local NUT installation on Windows

**Status:** DONE

Discover a local Windows NUT installation, its executables, version, and configuration directory. Allow a user to correct the path manually. Do not change configuration or services.

## T13 — Design syntax-preserving NUT configuration model

**Status:** DONE

Design and test a document model for `nut.conf`, `ups.conf`, `upsd.conf`, `upsd.users`, and `upsmon.conf` that preserves comments, order, unknown directives, unmanaged sections, quoting, and relevant formatting. No real file writes.

## T14 — Add configuration backup, write, and rollback pipeline

**Status:** DONE

Implement preview/diff, timestamped backup, temporary-file write, validation, safe replacement, activation testing, and rollback using temporary-directory tests.

## T15 — Build graphical NUT configuration editor

**Status:** DONE

Build a Windows-first editor over the syntax-preserving model and recoverable write pipeline. Every administrative change requires explicit confirmation.

## T16 — Add Windows service, UAC, and ACL administration

**Status:** DONE

Implement explicitly confirmed local Windows service, UAC, ACL, process, and Event Log actions behind platform interfaces.

## T17 — Add Windows COM-port and driver workflows

**Status:** DONE

Implement explicitly confirmed local COM-port, driver, and NUT-tool diagnostics behind platform interfaces.

## T18 — Add managed server profiles

**Status:** TODO

Add separate local and remote monitoring and management profiles. Local profiles use installation autodetection; remote profiles use manual directory selection.

## T19 — Add remote SSH/SFTP management

**Status:** TODO

Add secure remote management transport. The user selects and NutManager validates the remote configuration directory; remote autodiscovery is not permitted.

## T20 — Add secure credential storage

**Status:** TODO

Store remote-management credentials using platform-appropriate protected storage without exposing secrets in logs or the interface.

## T21 — Validate full Windows local and remote administration

**Status:** TODO

Validate Windows-first local and remote administration, including recovery paths, without unsafe UPS operations.

## T22 — Evaluate Linux administrative compatibility

**Status:** TODO

Evaluate secondary, best-effort Linux administrative compatibility without creating an official package commitment.

## T23 — Evaluate upstream NUT improvements

**Status:** TODO

Review mature, reproducible limitations discovered by NutManager. Separate client concerns from NUT concerns, then prepare focused issues or branches in `Marcelo-PX/nut` for potential PRs to `networkupstools/nut`.

## Task execution template

Use this structure when assigning a task to a coding agent:

```text
Objective:
Implement only [task ID and title].

Read:
- AGENTS.md
- relevant section of docs/TASKS.md
- only the required sections of docs/SPEC.md and docs/ARCHITECTURE.md

Allowed files:
- [explicit paths]

Requirements:
- [testable requirements]

Do not:
- change unrelated files
- add unrequested dependencies
- begin another task

Validation:
- [exact commands]

Completion:
- list changed files
- report command results
- report task-specific limitations
- stop
```
