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
| T21 | DONE | Validate full Windows local and remote administration | End-to-end Windows-first validation; findings recorded and carried into later tasks |
| T22 | DEFERRED | Future Linux compatibility evaluation | Compatibility may be reconsidered in a future task |
| T23 | DONE | Evaluate upstream NUT improvements | Focused issues and PR candidates |
| T24 | DONE | Modern responsive shell, design system and localization foundation | Windows-first responsive presentation and pt-BR/en-US foundation |
| T24A | DONE | Managed server profile UX and typed validation | Reversible profiles, typed validation, deterministic migration, and explicit connection testing |
| T24B | DONE | Current page and administration presentation decomposition | Focused responsive surfaces over existing safe capabilities |
| T25 | DONE | Semantic graphical configuration framework | Core schemas, mutations, and semantic review over T13/T14 |
| T26 | DONE | Graphical ups.conf configuration | Driver-aware UPS administration and runtimecal assistant |
| T27 | DONE | Graphical server and general configuration | Dedicated upsd.conf and nut.conf forms |
| T27A | DONE | Approved visual fidelity, iconography and motion | Windows presentation aligned with the approved visual references |
| T28 | DONE | Graphical users and monitoring configuration | Dedicated upsd.users and upsmon.conf forms |
| T29 | DONE | Graphical configuration UX hardening | Responsive, accessibility, bilingual, and transport regression validation |
| T30 | DONE | Windows-native SMB credential authentication | Native Windows credential UI, simplified SMB profile UX, and protected explicit credentials |
| T31 | DONE | Collapsible NUT file rail | Page-level collapsible file navigation with the current visual language |
| T32 | IN PROGRESS | Icon library adoption and T31 visual acceptance | An explicit icon-system decision and the rail seen with its final glass |

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

**Status:** DONE

Validate Windows-first local and remote administration, including recovery paths, without unsafe UPS operations. The live Windows validation was executed and its findings are recorded in [LIVE-VALIDATION-FINDINGS.md](LIVE-VALIDATION-FINDINGS.md) as a historical record of that validation run. Findings that required product changes were carried into the later tasks that own them rather than keeping this validation stream open indefinitely. Further validation of surfaces introduced after this run belongs to the task that introduces them, and final graphical hardening remains T29.

## T22 — Evaluate Linux administrative compatibility

**Status:** DEFERRED

Future Linux compatibility evaluation. Linux is deferred and is not an active CI, packaging, distribution, or administration target.

## T23 — Evaluate upstream NUT improvements

**Status:** DONE

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

**Status:** DONE

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

**Status:** DONE

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

**Status:** DONE

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

### Implemented

- immutable production catalog for documented `nutdrv_qx`, `usbhid-ups`, and `snmp-ups` options, plus passively detected/configured drivers with limited validation;
- graphical section, driver, port, protocol, battery, polling, SNMP change-only secret, Basic/Advanced, and custom-parameter editing;
- documented `runtimecal` four-value assistant that changes only the semantic draft and never performs a battery-discharge operation;
- semantic validation, read-only review, and generated preview converging exclusively on the existing local/SFTP/SMB pipeline.

## T27 — Graphical server and general configuration

**Status:** DONE

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

### Implemented

- dedicated `nut.conf` General editor with required `MODE`, release-backed Advanced fields, `KEY=value` insertion grammar, and limited-validation custom assignments;
- dedicated `upsd.conf` Server editor with stable repeated `LISTEN` rows, syntax-only IPv4/IPv6/hostname/wildcard validation, server/timeouts, TLS metadata, and protected change-only `CERTIDENT`;
- semantic review, read-only redacted preview, external-change protection, and explicit Apply through the existing Local/SFTP/SMB pipelines only;
- responsive graphical composition with ReadOnly/Manage capability enforcement and no DNS, socket, certificate, process, or service side effects.

## T27A — Approved visual fidelity, iconography and motion

**Status:** DONE

### Objective

Align the existing Windows presentation with the approved visual references, including shared iconography, restrained motion, dashboard hierarchy and configuration-review fidelity without changing administrative behavior.

### Allowed scope

- `NutManager.App` presentation: shared themes, shared controls, views, and the presentation-only view-model projections required to surface state that already exists.

### Requirements

- shared surface hierarchy, typography, colour and motion tokens instead of page-local palettes;
- vector iconography from one shared resource dictionary, with no emoji, pictographic text, or raster UI icons;
- integrated window chrome so the product identity and window controls read as one bar;
- Overview composed as a UPS dashboard with battery, load gauge, runtime, input/output, state and connection;
- configuration review presented as pending-change cards, redacted generated preview, and an explicit action bar;
- restrained motion on navigation, selection, metric values, drawer and theme toggle only.

### Do not

- fabricate readings, tests, logs, service state, or capabilities that the product does not implement;
- change domain logic, writers, transports, credential handling, or semantic configuration behavior;
- add charting, icon, or animation dependencies;
- begin T28 or T29.

### Validation

- existing suites stay green; focused tests pin that absent readings remain absent in the dashboard projections;
- Release build with zero warnings, format, vulnerability and whitespace gates;
- application launched on Windows to confirm the shell, dashboard and configuration surfaces initialise.

### Completion criteria

- the rendered application matches the approved references closely enough for human visual acceptance, with no functional or safety regression. Final acceptance requires that human comparison and is not implied by passing gates.

### Implemented

- one surface hierarchy, typography, spacing and motion token set in `Presentation/Themes`, replacing page-local palettes; restyled cards, buttons, inputs, lists, tabs, badges, title bar, navigation and profile card;
- `NutIcons.axaml` as the only icon source: `StreamGeometry` on a 24×24 grid, no icon font, icon package or raster UI image; semantic icon colour always redundant with text;
- integrated window chrome through `WindowDecorations="BorderOnly"`, using standard Avalonia window operations with no platform interop;
- Overview composed as a UPS dashboard with battery, semicircular load gauge, runtime, input/output, state and connection, each projected from the current snapshot and pinned by tests that keep an absent NUT variable absent;
- restrained motion within roughly 140–320 ms for interaction feedback, plus the decorative connection LED as the only looping animation; loops use the Avalonia Composition API because a keyframe animation targeting `RenderTransform` has no registered animator in this version;
- configuration navigation hardening: the file list stays enabled during a load, a superseded selection is cancelled, and only the newest selection may publish an editor;
- two runtime defects fixed while validating the above: COM enumeration now reads the `SERIALCOMM` device map with WMI used only for enrichment, and Windows NUT service discovery recognises `nut.exe` inside the trusted installation root.

Human visual acceptance was given for the rendered application. Merged to `main` through PR #33 as merge commit `fae5b4d1`.

## T28 — Graphical users and monitoring configuration

**Status:** DONE

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

### Implemented

- dedicated `upsd.users` editor working one user at a time, since every section of the file is one account: add/rename/remove with section-name validation, change-only password, SET and FSD permissions with an explicit FSD warning, instant-command modes (none/`ALL`/specific) with an `ALL` warning, and the `upsmon` `primary`/`secondary` role with a primary warning;
- dedicated `upsmon.conf` editor with repeated `MONITOR` rows, `MINSUPPLIES`, shutdown settings, polling and timing directives, `NOTIFYCMD`, and a notification matrix over the 29 documented events with `IGNORE` exclusivity and per-event custom messages;
- embedded-secret handling for `MONITOR`, whose credential sits between ordinary editable arguments: `SecretTokenIndex` marks the position, the projector blanks the token before any codec or view model sees it, and neighbouring values are edited without revealing the stored password;
- token-list serialization so `actions = SET FSD` stays a list of tokens instead of being quoted into one;
- unmanaged permissions, roles, directives and notification events preserved and shown as preserved rather than dropped;
- semantic review with redacted preview, and Apply exclusively through the existing T13/T14 pipeline over Local, SFTP and SMB.

### Validation record

Gates on the final round: restore PASS; Release build PASS with 0 warnings and 0 errors; 1102/1102 tests PASS, up from a 1051 baseline; vulnerability gate PASS; `dotnet format --verify-no-changes` PASS; `git diff --check` PASS.

Windows runtime smoke against a real installation: ten rapid selections across the five configuration files kept the UI responsive with a maximum observed latency of 19.4 ms, no freeze, and the newest selection always winning. No stored password appeared in the UI, review redacted the sensitive line, and a comparison against the real credential found zero leaks. `C:\NUT\etc\upsd.users` and `C:\NUT\etc\upsmon.conf` kept their original modification times; no Apply was performed during the smoke.

## T29 — Graphical configuration UX hardening

**Status:** DONE

T29 was completed and merged into `main` through PR #35.

### Delivered

Sidebar motion, profile quick navigation, footer authorship, and consistent Administration and
Settings icon semantics.

### Objective

Validate and harden the complete graphical configuration experience.

### Allowed scope

- focused App/Core/UI tests, manual Windows validation artifacts, and defect fixes required by that validation.

### Requirements

- Wide/Medium/Compact, sidebar/drawer, keyboard, focus, automation, clipping/overflow, and semantic-error validation;
- accessible names on every actionable control;
- `pt-BR` and `en-US` validation; preference persistence;
- 100/125/150% Windows scaling, invalid-field states, and non-blue Windows system-accent regression;
- local, SFTP, and SMB regression of reviewed safe writes and recovery;
- final graphical configuration Windows validation.

### Do not

- broaden to Linux, remote service control, raw editors, or unreviewed writes.

### Validation

- automated responsive/resource/accessibility tests plus documented manual Windows validation.

### Known follow-up

The configuration action bar's buttons wrap their content in a panel, so UI Automation announces the
panel type rather than the button label. This is understood and deliberately deferred rather than
outstanding work on this task: it needs `AutomationProperties.Name` on those buttons and is worth
picking up with the next presentation change that touches that bar.

### Completion criteria

- graphical forms remain accessible, bilingual, responsive, and safe across local and supported remote transports.

## T30 — Windows-native SMB credential authentication

**Status:** DONE

### Current scope

Windows-native SMB credentials, the simplified SMB profile form, per-profile managed NUT file
selection, and detection of the supported files.

### Objective

Let an SMB profile authenticate the way Windows already does it: the current session's identity
when that is enough, and the operating system's own credential dialog when another account is
needed. Remove the redundant SMB fields that the new model makes meaningless.

### Allowed scope

- SMB profile model, validation, presentation, and the remote session's credential flow;
- a Windows credential-prompt boundary behind a testable interface;
- the connection LED's size and healthy colour.

### Requirements

- current Windows identity connects with no user name, no password and no stored credential;
- another Windows account uses `CredUIPromptForWindowsCredentialsW`, never a NutManager password control;
- an explicit credential is validated against the share before it is persisted, and a failed attempt leaves the previous one intact;
- the share is the exact configuration location, so the separate directory field is retired without discarding legacy values;
- Windows Credential Manager remains the only persistent secret store.

### Do not

- weaken exact-share confinement, map a drive, run `net use`/`cmdkey`, or disconnect global SMB sessions;
- change SSH authentication, the safe-write pipeline, or any writer boundary;
- let a password reach ordinary view-model state, profile JSON, logs, or the automation tree.

### Validation

- prompt state, credential-lifecycle, simplified-surface, and Manage/ReadOnly wording tests, plus Windows runtime validation of both authentication modes.

### Completion criteria

- both SMB authentication modes work on Windows with no redundant fields and no NutManager-owned password input.

### Implemented

- current Windows identity connects with the session's own token: no user name, no dialog, and no
  credential read from the store, ignoring one left over from an older profile rather than reusing it;
- another Windows account is collected by `CredUIPromptForWindowsCredentialsW` behind the
  platform-neutral `IWindowsCredentialPrompt` contract, with the owner window handle supplied by the
  App so the dialog belongs to NutManager; NutManager owns no password control for SMB;
- an explicit credential is proven against the share before it is persisted, and only when the
  dialog's own remember box was ticked; a refused credential is never stored and a failed
  replacement leaves the working one in place; cancelling changes nothing;
- the share became the exact configuration location, retiring the separate directory field; a legacy
  value is preserved and surfaced for correction instead of being dropped or silently retargeted;
- Windows Credential Manager remains the only persistent secret store, with the account name kept as
  ordinary non-secret profile metadata;
- the connection LED core was reduced and given its own brighter green while its glow, pulse, period
  and lifecycle stayed as they were;
- a connection failure no longer claims read-only access on a management profile, and an unprobed
  management session is no longer described as read-only.

### Per-profile managed NUT files

A profile now records which of the five supported files it exposes, defaulting to all of them so a
profile saved before the setting existed behaves exactly as it did. Disabling a file only removes it
from the Administration list for that profile: nothing on disk is created, renamed, or deleted, and a
file that is enabled but currently absent still appears and reports its missing state when opened.
Enabled-by-profile and currently-present are deliberately kept as separate facts.

Zero files is allowed rather than blocked. A remote profile used only for monitoring is a legitimate
product state, and the Administration surface already has an empty-file-list path, so forbidding it
would invent a rule the architecture does not need. The form says plainly what an empty selection
means.

Detection is a separate, explicit step. `INutManagedFileDetector` reports which supported files are
actually present and hands back a proposal; nothing is applied without the administrator asking.
The local detector reads the presence flags the installation detector already produces, and the
remote one reads what directory validation already established over the existing session — the same
pinned host key for SFTP, the same exact-share confinement and resolved credential for SMB. Neither
adds I/O, opens a session, or looks at a name outside the closed set, so the `.sample` files NUT
ships and directives like `upssched.conf` are never offered.

Administration takes its file list from the profile's selection, and refuses a file outside it, so
the selection can never point at something the profile does not expose. Because the runtime profile
context is captured at bootstrap, a change to the selection applies on restart, exactly like every
other profile edit.

### Validation record

Gates: Release build with 0 warnings and 0 errors; 1150/1150 tests, up from a 1114 baseline;
vulnerability gate clean; `dotnet format --verify-no-changes` clean; `git diff --check` clean.

Windows runtime, current-identity profile against a real SMB share: no password control present on
either surface, the management profile is described as such rather than as read-only, and the status
light renders with its reduced core, glow and pulse intact. No configuration was applied.

Not exercised on the development machine: the native dialog was not opened against a second Windows
account, because no alternate credential with access to the share was available there. That path —
prompt, cancel, successful sign-in, and both remember variants — is covered by automated tests
against a faked native seam rather than by manual validation, and is worth confirming on a machine
where a test account exists.

## T31 — Collapsible NUT file rail

**Status:** DONE

### Objective

Turn the fixed file list on Administration → NUT Configuration into a collapsible page rail, so the
editing form gets the space back, and bring that surface up to the current visual language.

### Allowed scope

- `NutManager.App` presentation for the configuration page, the shared rail styles it needs, and the
  settings preference that remembers its state.

### Requirements

- expanded and collapsed states, with the collapsed one showing icons and keeping every row named;
- only the files the profile manages, and a dignified empty state when it manages none;
- the state persists and is restored on the next launch;
- folding never changes the selected file, rebuilds an editor, or touches a draft;
- switching files keeps using the existing guard.

### Do not

- alter T13/T14, the transports, credential handling, or the T30 file-selection logic;
- add a continuous animation; the connection light stays the only one.

### Narrow layouts

Below 860 px the page cannot afford both a labelled rail and a usable form beside it, so the rail
folds. It folds without touching the stored preference: the administrator asked for it expanded, and
widening the window again gives back exactly that rather than a state the layout imposed. There is
no second UX for small windows — the same rail, just folded.

### Implemented

- a rail whose column changes width rather than a panel that is shown and hidden, so collapsing gives
  the editing form the space back;
- folding is presentation only: the selected file, its draft and its editor come through untouched,
  and rows are buttons rather than list items so the existing dirty-draft guard stays in charge of
  whether a switch happens at all;
- only the files the profile manages, with a dignified empty state when it manages none;
- the expanded state persists through the settings store at schema 4, and a document written before
  the preference existed opens expanded;
- an acrylic pane behind the shell, two deliberately separated tones, and glass surfaces in Apple's
  language — frosted and cool, a thin white hairline for an edge, larger continuous radii — with
  foreground colours untouched so text keeps the contrast it had;
- narrow layouts fold the rail without altering the stored preference.

### Validation

Gates: Release build with 0 warnings and 0 errors; 1201/1201 tests; vulnerability gate clean;
format and whitespace clean.

Windows runtime: the shell, the acrylic backdrop and both themes were confirmed on screen, and the
rail was driven expanded and collapsed with its rows named in both states.

### Known follow-ups

Two items were accepted rather than completed, and neither blocks the surface working:

- the rail was last seen on screen before the two-tone glass landed, so the current appearance of
  that specific panel is confirmed by the theme captures rather than by a picture of the rail itself.
  Seeing it needs a local profile or a reachable configuration share;
- external icon fonts were authorised but not adopted. Doing so means adding a package, recording it
  in the third-party notices, and reversing the "no icon font, no icon package" decision recorded in
  the design system and the agent rules. It belongs in a round that changes those documents together
  rather than leaving the repository asserting one thing and practising another.

## T32 — Icon library adoption and T31 visual acceptance

**Status:** IN PROGRESS

### Objective

Settle the icon system with an explicit, investigated decision, and see the T31 rail on screen with
the glass it actually ships with.

### Allowed scope

- the icon catalog and whatever dependency the decision requires, the three documents that record the
  icon policy, and focused tests;
- manual Windows validation of the configuration rail.

### Requirements

- a real comparison of maintained options, recorded with licences and reasons;
- the semantic catalog stays authoritative — views must not reference an icon library directly;
- no icon fetched over the network at runtime;
- the rail observed in dark and light, expanded and collapsed, plus the narrow-layout fold.

### Do not

- start operational functionality, reimplement the rail, or change any transport, writer or
  credential boundary.

### Icon decision

Investigated and recorded in the design system. `FluentIcons.Avalonia` (MIT, Avalonia 12) renders
through a font and exposes no geometry, so it cannot fill a catalog without putting a library
reference in every view. `Material.Icons.Avalonia` (MIT) is vector and exposes path data through
`MaterialIconDataProvider.GetData`, so one adapter can fill the catalog while the views go on asking
for semantic names. **It was adopted, and it now supplies all 62 icons in the product.**

Twenty-one glyphs had been assembled from several shapes each so their parts could animate
separately — LEDs blinking out of phase, a gear turning around a stationary hub, a dot sweeping a
trace, a sun's rays turning around a fixed disc. Those parts were removed. The trade was made
explicitly: one drawing system across the whole product outranks segmented animation, because a
single icon animating more richly is not worth having one icon that is not from the library. The
motion moved to the whole glyph — breathe, pulse, pop, beat, turn — and the amplitudes came down with
it. `NutIcons.axaml` is now a fallback catalog: it defines the valid names and a drawing for each, in
case a future version of the library drops a kind.

The dependency rule in the agent instructions permits an icon library on the same terms as any other
dependency, so the repository no longer asserts a rule it had replaced.

### Liquid glass hover

The glass panes react to the pointer: the surface lightens and its edge comes up over 180 ms. Scoped
to the surfaces that are actually glass — the cards and the configuration file rail — plus a separate
rung for the rows that sit on them, so a hovered row does not vanish into a hovered pane. Containers
(the sidebar, the shell chrome) stay inert. Nothing moves or resizes, and pressed, selected and
disabled all still win over hover.

### Visual acceptance

Observed on Windows with a temporary local profile, restored afterwards and verified by hash. Rail
seen expanded (263 px rows) and collapsed (57 px) in both dark and light, with the accent bar, the
selected sheen, the per-file icons and the glass surface against the acrylic backdrop.

Narrow layout is **not** confirmed on screen: at a window small enough to trigger the fold the shell
enters compact mode and the rail scrolls out of view, so the observation was inconclusive. The
behaviour is covered by four tests that drive the width directly.

### Validation

- icon catalog and policy tests, plus documented manual Windows observation of the rail.

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
