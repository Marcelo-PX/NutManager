# NutManager Implementation Tasks

## Status legend

- `TODO` — not started;
- `READY` — specified and ready for implementation;
- `IN PROGRESS` — currently assigned;
- `BLOCKED` — waiting on a dependency or decision;
- `DEFERRED` — intentionally postponed for future evaluation;
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
| T11 | DONE | Package and validate MVP | Official Windows x64 package and completed live Windows NUT validation |
| T12 | DONE | Detect local NUT installation on Windows | Autodetect installation, executables, version, and configuration directory |
| T13 | DONE | Design syntax-preserving NUT configuration model | Safe model for managed and unmanaged configuration content |
| T14 | DONE | Add configuration backup, write, and rollback pipeline | Previewed, validated, recoverable configuration changes |
| T15 | DONE | Build graphical NUT configuration editor | Windows-first configuration experience |
| T16 | DONE | Add Windows service, UAC, and ACL administration | Explicitly confirmed local administrative actions |
| T17 | DONE | Add Windows COM-port and driver workflows | Local device and driver diagnostics |
| T18 | DONE | Add managed server profiles | Managed profile metadata and strict local/remote management-context separation |
| T19 | DONE | Add remote SSH/SFTP management | Manual remote directory browse, validation, and secure management transport |
| T19B | DONE | Add SMB remote configuration transport | Manual UNC SMB configuration access and verified safe replacement |
| T20 | DONE | Add secure credential storage | Protected SSH and SMB remote-management credentials |
| T21 | IN PROGRESS | Validate full Windows local and remote administration | End-to-end Windows-first validation; current findings recorded separately |
| T22 | DEFERRED | Future Linux compatibility evaluation | Compatibility may be reconsidered in a future task |
| T23 | TODO | Evaluate upstream NUT improvements | Focused issues and PR candidates |
| T24 | DONE | Modern responsive shell, design system and localization foundation | Windows-first responsive presentation and pt-BR/en-US foundation |
| T24A | DONE | Managed server profile UX and typed validation | Reversible profiles, typed validation, deterministic migration, and explicit connection testing |
| T24B | TODO | Current page and administration presentation decomposition | Focused responsive surfaces over existing safe capabilities |
| T25 | TODO | Semantic graphical configuration framework | Core schemas, mutations, and semantic review over T13/T14 |
| T26 | TODO | Graphical ups.conf configuration | Driver-aware UPS administration and runtimecal assistant |
| T27 | TODO | Graphical server and general configuration | Dedicated upsd.conf and nut.conf forms |
| T28 | TODO | Graphical users and monitoring configuration | Dedicated upsd.users and upsmon.conf forms |
| T29 | TODO | Graphical configuration UX hardening | Responsive, accessibility, bilingual, and transport regression validation |

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

**Status:** DONE

Produce and test the official self-contained Windows x64 package. Windows x64 is the only active development, validation, and distribution platform. The real Windows NUT acceptance is complete; this section remains as the package-validation record.

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

**Status:** DONE

Add managed local and remote monitoring and management profiles, manual remote configuration-directory metadata, and strict local/remote management-context separation. T18 does not implement remote browse, remote validation transport, or local-management fallback for a remote profile; those are T19 work.

## T19 — Add remote SSH/SFTP management

**Status:** DONE

Add SSH/SFTP remote management transport, manual remote configuration-directory browse and selection, strict host-key verification, and remote validation. Remote autodiscovery is not permitted. Remote writes remain limited to an explicitly verified Windows/OpenSSH safe-replace path.

## T20 — Add secure credential storage

**Status:** DONE

Store opt-in SSH and SMB remote-management secrets in Windows Credential Manager without exposing them in logs, view-model state, or JSON profile metadata.

## T21 — Validate full Windows local and remote administration

**Status:** IN PROGRESS

Validate Windows-first local and remote administration, including recovery paths, without unsafe UPS operations. Current live findings are recorded in [LIVE-VALIDATION-FINDINGS.md](LIVE-VALIDATION-FINDINGS.md); they are not completed acceptance or authorization for redesign work in this validation stream.

## T22 — Evaluate Linux administrative compatibility

**Status:** DEFERRED

Future Linux compatibility evaluation. Linux is deferred and is not an active CI, packaging, distribution, or administration target.

## T23 — Evaluate upstream NUT improvements

**Status:** TODO

Review mature, reproducible limitations discovered by NutManager. Separate client concerns from NUT concerns, then prepare focused issues or branches in `Marcelo-PX/nut` for potential PRs to `networkupstools/nut`.

## T24 — Modern responsive shell, design system and localization foundation

**Status:** DONE

### Objective

Build the Windows-first responsive shell and design tokens, with official `pt-BR` and `en-US` localization infrastructure.

### Allowed scope

- App presentation/resources, shell, settings, localization resources, and focused tests;
- non-secret UI-preference persistence required for theme, language, or sidebar state;
- documentation directly required by the implementation.

### Requirements

- connection indicator with text, accessible status, and restrained status colors;
- sun/moon theme control, with System theme in Appearance & Language;
- Expanded/Collapsed/Overlay sidebar and Hidden/Collapsed/Expanded/Overlay review-drawer shell;
- Wide (>=1200), Medium (860–1199), and Compact (<860) layouts without ordinary horizontal scrolling;
- stable semantic resources for all new user-facing strings in both official cultures;
- culture-invariant NUT serialization and deterministic resource fallback;
- accessible icon controls, focus, and tab order.
- product-owned accent/selection colors, one-scroll-owner rule, localized option presentation, persistent mock/demo indication, and responsive validation-field layout.

### Do not

- implement semantic configuration mutations or graphical file forms;
- add Linux scope, a new writer, or automatic service activation;
- claim that T25–T29 experiences are implemented.

### Validation

- automated resource/fallback/serialization tests;
- manual Windows validation in both cultures and all responsive states;
- standard restore, build, test, vulnerability, format, and diff checks.

### Completion criteria

- both cultures are usable and persisted safely;
- responsive shell and review-drawer foundation are accessible;
- no NUT token is localized or culture-serialized.

## T24A — Managed server profile UX and typed validation

**Status:** DONE

**Dependency:** T24

### Objective

Redesign managed-server profiles and introduce reusable typed validation before semantic configuration work.

### Allowed scope

- Core host/port and reusable validation contracts;
- profile draft/settings/persistence migration required by the new source-of-truth boundary;
- Settings profile UX, localization resources, focused test fakes, and documentation.

### Requirements

- one **New server** flow with reversible Local/Remote, ReadOnly/Manage, and SFTP/SMB choices;
- typed host, TCP/SSH port, UNC, field, and cross-field validation with localized inline errors and Save disabled on Error;
- explicit operational Test Connection, separate from syntax, with no secrets in diagnostics;
- Save/Discard/Continue editing decision for dirty drafts and a first-class restart-required active-profile state;
- future schema migration separating application preferences from managed-profile endpoints/metadata;
- mock/demo target policy: disabled for new normal installs, preserved for existing persisted settings, and visibly indicated when active.

### Do not

- implement semantic `.conf` mutations or configuration writes;
- change T14, T19, T19B, or T20 safety boundaries;
- perform DNS during syntactic validation or persist a resolved IP in place of a hostname.

### Validation

- deterministic host/port/cross-field tests; Local↔Remote and SFTP↔SMB transitions; dirty drafts; settings migration; connection-tester fakes; and pt-BR/en-US validation-resource completeness.

### Completion criteria

- profiles are reversible while drafting, invalid input cannot be saved, and endpoint source-of-truth migration is deterministic without secret migration.

## T24B — Current page and administration presentation decomposition

**Status:** TODO

**Dependency:** T24A

### Objective

Decompose current responsive presentation surfaces before adding T25–T28 graphical forms.

### Allowed scope

- App presentation/view-model decomposition, localized UI resources, focused tests, and documentation;
- existing capability composition only, without new administrative behavior.

### Requirements

- Administration sections for NUT Configuration, Windows Service, Devices & Drivers, and Remote Access;
- neutral selection cards, grouped commands, useful empty states, and responsive Overview/Devices/Diagnostics improvements including copy diagnostics;
- reduce ordinary non-secret code-behind when useful while preserving password/passphrase input at the View boundary;
- bounded read-only NUT-version fallback when file-version metadata is unavailable.

### Do not

- alter safe-write behavior; create remote service control; reduce hardware/admin confirmation; or move passwords/passphrases into ordinary ViewModel state.

### Validation

- responsive/empty-state/command-grouping tests and manual Windows review, with existing local/SFTP/SMB and privileged-boundary regressions.

### Completion criteria

- current pages are focused and responsive while all existing safety boundaries retain their behavior.

## T25 — Semantic graphical configuration framework

**Status:** TODO

### Objective

Create the Core semantic schema, projection, validation, and mutation layer that extends T13 for complete graphical configuration while retaining T14/T19/T19B writes.

### Allowed scope

- Core configuration models/contracts and T13 explicit mutation primitives;
- Infrastructure/App projections and semantic-review support;
- focused deterministic tests and documentation.

### Requirements

- schema registry, file/section/field/driver descriptors, stable resource keys, validation, applicability, insertion order, and activation metadata;
- Explicit, AutomaticByOmission, ExplicitAutoToken, MissingRequired, Unsupported, and CustomUnknown states;
- setting-specific Automatic policies and sensitive change-only model;
- deterministic add/remove/rename/section/repeated-row mutations preserving comments, raw nodes, ordering, quoting, line endings, encoding, duplicates, and unknown content;
- graphical custom parameters with limited-validation warning and read-only generated preview.

### Do not

- write directly from views, reformat whole files, guess defaults, or use runtime internet;
- treat Automatic as universal directive deletion.

### Validation

- deterministic parser/mutation/serialization, culture-invariant, sensitive-redaction, and safe-pipeline integration tests.

### Completion criteria

- semantic mutations project through T13 and exclusively use existing safe transports.

## T26 — Graphical ups.conf configuration

**Status:** TODO

### Objective

Provide a dedicated driver-aware UPS configuration form for `ups.conf`.

### Allowed scope

- T25 semantic framework extensions, local passive driver/COM metadata, App UPS form, and focused tests/documentation.

### Requirements

- add/rename/remove validated UPS sections; identification and `desc`;
- concrete driver selection/detection, driver-aware port/protocol/parameter controls, and documented battery settings;
- local passive COM selector where applicable, never fabricated remote COM enumeration;
- documented `runtimecal` assistant that edits only semantic draft;
- Basic/Advanced/Custom parameters and semantic review.

### Do not

- open serial ports directly, run driver control/shutdown commands, invent driver defaults, or persist UI metadata as NUT directives.

### Validation

- schema, section, driver/port/protocol applicability, runtimecal, redaction, local/SFTP/SMB safe-pipeline regression, and Windows UI validation.

### Completion criteria

- supported UPS settings are graphical and all writes remain reviewed/recoverable.

## T27 — Graphical server and general configuration

**Status:** TODO

### Objective

Provide dedicated graphical `upsd.conf` and `nut.conf` forms.

### Allowed scope

- T25 semantic schemas/forms, focused tests, and documentation for server/general configuration.

### Requirements

- repeated `LISTEN` address/port rows, server behavior, timeouts, TLS/certificate metadata, and custom parameters;
- documented NUT MODE and advanced `nut.conf` options respecting that file's own grammar;
- review, redaction where applicable, and existing safe pipeline.

### Do not

- assume all NUT files use `key = value`, create an unrestricted raw editor, or restart services automatically.

### Validation

- parser/serializer grammar, repeated-row, validation, preservation, and local/SFTP/SMB regression tests.

### Completion criteria

- supported server/general settings are graphical without altering unmanaged syntax.

## T28 — Graphical users and monitoring configuration

**Status:** TODO

### Objective

Provide dedicated graphical `upsd.users` and `upsmon.conf` forms with protected secret handling.

### Allowed scope

- T25 schemas/forms, focused tests, and documentation for users and monitoring.

### Requirements

- user add/rename/remove, roles/actions/instcmd permissions, password state, and change-only replacement;
- repeated graphical `MONITOR` rows plus MINSUPPLIES, timing, shutdown, notification, advanced, and custom controls;
- explicit warning for dangerous permissions such as FSD; secret redaction in UI, review, and logs.

### Do not

- reveal existing secrets, execute FSD/shutdown, or conflate permission configuration with command execution.

### Validation

- secret non-exposure, repeated row, user mutation, validation, and transport safe-pipeline regression tests.

### Completion criteria

- users and monitoring are manageable graphically with change-only secrets.

## T29 — Graphical configuration UX hardening

**Status:** TODO

### Objective

Validate and harden the complete graphical configuration experience.

### Allowed scope

- focused App/Core/UI tests, manual Windows validation artifacts, and defect fixes required by that validation.

### Requirements

- Wide/Medium/Compact, sidebar/drawer, keyboard, focus, automation, clipping/overflow, and semantic-error validation;
- `pt-BR` and `en-US` validation; preference persistence;
- 100/125/150% Windows scaling, invalid-field states, and non-blue Windows system-accent regression;
- local, SFTP, and SMB regression of reviewed safe writes and recovery;
- final graphical configuration Windows validation.

### Do not

- broaden to Linux, remote service control, raw editors, or unreviewed writes.

### Validation

- automated responsive/resource/accessibility tests plus documented manual Windows validation.

### Completion criteria

- graphical forms remain accessible, bilingual, responsive, and safe across local and supported remote transports.

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
