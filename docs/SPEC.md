# NutManager Product Specification

## 1. Product vision

NutManager is a Windows-first desktop interface for Network UPS Tools (NUT). Its final direction is to make NUT monitoring, configuration, and administration understandable for both local and remote NUT deployments without hiding the underlying NUT concepts.

The initial monitoring milestone is deliberately non-administrative and read-only. The final product direction adds safe configuration and administration rather than remaining a read-only product. NutManager uses standard NUT protocols, commands, and configuration formats wherever possible; it is a client and management layer, not a replacement UPS driver.

## 2. Intended users

- system administrators running or managing NUT from Windows;
- small organizations that need a practical UPS monitoring console;
- advanced home-lab users;
- NUT contributors diagnosing real device and platform behavior, including users relying on secondary Linux compatibility.

## 3. Product principles

1. Safety before automation.
2. Read-only monitoring before configuration and administrative behavior.
3. Missing information is shown as unavailable, never fabricated.
4. Platform-specific operations are explicit and isolated.
5. Standard NUT compatibility is preferred over project-specific modifications.
6. Every configuration write must be recoverable.
7. The interface must remain useful without access to the NUT source repository.

## 4. MVP scope

The initial monitoring milestone is a non-administrative, read-only desktop application. It does not limit the final product direction to read-only behavior.

### 4.1 Functional requirements

#### FR-001 — Application shell

The application shall provide a responsive Avalonia desktop window with navigation for:

- Overview;
- Devices;
- Diagnostics;
- Settings.

#### FR-002 — Theme

The application shall support light, dark, and follow-system themes. The selected preference shall persist locally.

#### FR-003 — NUT server configuration

The user shall be able to configure:

- host name or IP address;
- TCP port, defaulting to `3493`;
- optional connection timeout;
- optional preferred UPS name.

The MVP shall not store NUT administrator credentials.

#### FR-004 — Connection

The application shall connect to a NUT server using a bounded timeout and cancellation support.

The UI shall distinguish:

- disconnected;
- connecting;
- connected;
- reconnecting;
- stale data;
- connection failed.

#### FR-005 — UPS discovery

The application shall list UPS devices exposed by the configured NUT server and allow the user to select one.

#### FR-006 — Telemetry

For the selected UPS, the application shall display available values for:

- UPS name;
- description or model;
- `ups.status`;
- input voltage;
- output voltage;
- load percentage;
- input or output frequency when supplied;
- temperature when supplied;
- battery voltage when supplied;
- battery charge when supplied;
- runtime estimate when supplied;
- last successful update.

The UI shall retain the original NUT variable names in a details view.

#### FR-007 — Status interpretation

The application shall parse common NUT status tokens, including at least:

- `OL` — online;
- `OB` — on battery;
- `LB` — low battery;
- `RB` — replace battery;
- `CHRG` — charging;
- `DISCHRG` — discharging;
- `BYPASS` — bypass;
- `OFF` — output off;
- `OVER` — overloaded;
- `CAL` — calibration.

Unknown tokens shall be preserved and displayed, not discarded.

#### FR-008 — Missing values

A variable not supplied by NUT shall appear as unavailable. The application shall not derive or estimate it silently.

#### FR-009 — Stale data

If polling stops succeeding, the last values may remain visible but must be clearly marked as stale with the timestamp of the last successful update.

#### FR-010 — Mock mode

A deterministic mock provider shall supply realistic sample data for UI development and automated tests without a NUT server or UPS.

Mock data must be explicitly labeled as simulated.

#### FR-011 — Diagnostics summary

The MVP diagnostics page shall report, without administrative actions:

- configured endpoint;
- DNS or connection outcome where applicable;
- protocol connection result;
- discovered UPS names;
- last polling error;
- client application version.

It shall not open serial ports, stop services, or modify NUT.

#### FR-012 — Local settings

Application settings shall be stored per user in an operating-system-appropriate application-data directory.

Settings writes shall be atomic and must not contain secrets in the MVP.

### 4.2 MVP screens

#### Overview

Shows the selected UPS, interpreted status, key metric cards, connection state, and last update.

#### Devices

Lists UPS devices returned by the server and exposes raw variables for the selected device.

#### Diagnostics

Shows read-only connection diagnostics and recent application-level errors.

#### Settings

Configures endpoint, preferred UPS, polling interval, timeout, theme, and mock mode.

## 5. Non-functional requirements

### NFR-001 — Windows-first platform strategy

Windows x64 is the primary platform for development, manual testing, official distribution, and the first administrative capabilities. Linux has secondary, best-effort compatibility: shared code should remain portable where practical, but Linux has no official package or immediate administrative-feature commitment.

The shared architecture must avoid unnecessary Windows dependencies. Alpine Linux desktop is not validated or supported for the MVP.

### NFR-002 — Performance

The idle application should avoid unnecessary polling, busy loops, and unbounded background tasks. UI updates must not block the UI thread.

### NFR-003 — Reliability

Network operations require cancellation, bounded timeouts, and deterministic disposal. A failed poll must not crash the application.

### NFR-004 — Security

- no administrator or root requirement in the MVP;
- no passwords in logs;
- no shell command construction from untrusted values;
- no direct modification of NUT configuration or services;
- endpoint input validation.

### NFR-005 — Testability

Core behavior shall be testable without Avalonia, network access, real-time delays, hardware, or elevated privileges.

### NFR-006 — Accessibility

Controls must be keyboard accessible, readable at common DPI scales, and not communicate status using color alone.

### NFR-007 — Observability

Errors shall include an actionable summary and technical detail suitable for logs. Logs must remain bounded and exclude secrets.

## 6. Product direction after the initial monitoring milestone

The final product shall support monitoring, configuration, and administration of NUT servers. Monitoring and management are separate connections with independent state:

- monitoring uses the NUT protocol over TCP, with port `3493` as the default;
- management uses local filesystem and platform APIs for a local server, or a secure remote transport for a remote server.

### Local management

Windows local management is the priority. NutManager will automatically discover a local NUT installation, including its executables, version, and configuration directory. The user may correct the discovered path manually.

### Remote management

NutManager must not automatically discover a remote configuration directory. The user will enter or navigate to the remote directory, and NutManager will validate the selected directory. The first planned remote management transport is SSH/SFTP.

## 7. Planned post-MVP capabilities

These capabilities are intentionally deferred:

1. managed local and remote NUT server profiles;
2. syntax-preserving editing of `nut.conf`, `ups.conf`, `upsd.conf`, `upsd.users`, and `upsmon.conf`;
3. timestamped backups, validation, activation testing, and rollback;
4. Windows service, UAC, ACL, COM-port, and driver workflows;
5. SSH/SFTP remote management and secure credential storage;
6. multiple simultaneous NUT servers;
7. historical charts, notifications, and shutdown-policy management;
8. evaluation of Linux administrative compatibility;
9. installation and update workflows.

Every administrative capability must require explicit confirmation and a rollback path.

## 8. Configuration editing requirements for later phases

Configuration management must use a syntax-preserving document model rather than a generic INI serializer. It must preserve comments, ordering, unknown directives, unmanaged sections, quoting, and relevant formatting.

The required write pipeline is:

```text
read → parse while preserving syntax → requested change → preview/diff → backup
→ temporary file → validation → safe replacement → activation when necessary
→ test → rollback on failure
```

The design must separate ordinary user operations from elevated operations and never reveal stored credentials after saving.

## 9. Upstream strategy

NutManager should first identify and document limitations through real usage. Improvements to NUT shall be proposed only when the limitation belongs in NUT rather than in the client application.

Upstream work shall use:

- official repository: `networkupstools/nut`;
- contributor fork: `Marcelo-PX/nut`;
- focused branches and independent pull requests;
- NUT contribution, style, testing, licensing, and DCO requirements.

The upstream repository shall not be embedded in the normal NutManager workspace.

## 10. MVP acceptance criteria

The MVP is complete when:

1. the application builds and tests on the supported development platform;
2. it starts with a polished Avalonia shell;
3. mock mode renders all primary states deterministically;
4. it connects to a configured NUT server without elevation;
5. it discovers and selects an exposed UPS;
6. it displays supplied variables and marks absent ones unavailable;
7. it handles disconnects without crashing;
8. it marks retained data as stale after failed polling;
9. settings persist per user;
10. automated tests cover status parsing, data mapping, stale-state logic, configuration persistence, and protocol parsing;
11. no MVP workflow modifies NUT files, services, drivers, or hardware.
