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
| T06 | READY | Implement read-only NUT client | TCP protocol client with tests |
| T07 | TODO | Add UPS discovery and selection | Device listing and details |
| T08 | TODO | Persist local settings | Atomic per-user settings storage |
| T09 | TODO | Add polling and stale-data handling | Robust refresh and reconnect behavior |
| T10 | TODO | Complete MVP diagnostics | Read-only diagnostics page |
| T11 | TODO | Package and validate MVP | Windows and Linux test packages |
| T12 | TODO | Design preserved NUT configuration parser | Post-MVP syntax model and tests |
| T13 | TODO | Add backup and restoration pipeline | Recoverable configuration changes |
| T14 | TODO | Add Windows administration | Services, permissions, and diagnostics |
| T15 | TODO | Add Linux administration | systemd/OpenRC and permissions |
| T16 | TODO | Evaluate upstream NUT improvements | Focused issues and PR candidates |

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

**Status:** READY

Implement the minimum TCP protocol commands needed to list UPS devices and fetch variables. Include cancellation, timeout, partial-read, malformed-reply, and fake-server tests.

## T07 — Add UPS discovery and selection

**Status:** TODO

Connect the Devices page to the NUT client, list exposed UPS devices, retain selection, and provide a raw variable details view.

## T08 — Persist local settings

**Status:** TODO

Persist non-secret endpoint, preferred UPS, polling, timeout, theme, and mock-mode settings per user using versioned atomic JSON storage.

## T09 — Add polling and stale-data handling

**Status:** TODO

Add bounded asynchronous polling, cancellation, reconnect behavior, stale snapshot retention, and timestamps without busy loops.

## T10 — Complete MVP diagnostics

**Status:** TODO

Expose read-only endpoint, connection, discovery, polling, version, and application-error diagnostics. No service or serial operations.

## T11 — Package and validate MVP

**Status:** TODO

Produce and test documented Windows x64 and mainstream Linux x64 packages. Record exact supported environments and known limitations.

## T12 — Design preserved NUT configuration parser

**Status:** TODO

After MVP stabilization, design and test a document model that preserves comments, order, unknown directives, unmanaged sections, and quoting. No real file writes.

## T13 — Add backup and restoration pipeline

**Status:** TODO

Implement temporary-directory tests for timestamped backups, atomic writes, comparison, retention, restoration, and rollback behavior.

## T14 — Add Windows administration

**Status:** TODO

Implement explicitly confirmed Windows service, process, Event Log, UAC, COM-port, and NUT-tool integrations behind platform interfaces.

## T15 — Add Linux administration

**Status:** TODO

Implement explicitly confirmed systemd and later OpenRC operations, permissions, `/dev/tty*` discovery, and journald/syslog diagnostics behind platform interfaces.

## T16 — Evaluate upstream NUT improvements

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
