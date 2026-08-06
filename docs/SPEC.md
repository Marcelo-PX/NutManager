# NutManager Product Specification

## 1. Product vision

NutManager is a modern desktop interface for Network UPS Tools (NUT). It should make UPS monitoring, diagnosis, and later administration understandable without hiding the underlying NUT concepts.

The application must use standard NUT protocols, commands, and configuration formats wherever possible. NutManager is a client and management layer; it is not a replacement UPS driver.

## 2. Intended users

- system administrators running NUT on Windows or Linux;
- small organizations that need a practical UPS monitoring console;
- advanced home-lab users;
- NUT contributors diagnosing real device and platform behavior.

## 3. Product principles

1. Safety before automation.
2. Read-only behavior before administrative behavior.
3. Missing information is shown as unavailable, never fabricated.
4. Platform-specific operations are explicit and isolated.
5. Standard NUT compatibility is preferred over project-specific modifications.
6. Every configuration write must be recoverable.
7. The interface must remain useful without access to the NUT source repository.

## 4. MVP scope

The MVP is a non-administrative, read-only desktop application.

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

### NFR-001 — Cross-platform

The shared application shall target Windows and mainstream desktop Linux distributions supported by the selected Avalonia and .NET versions.

Alpine Linux desktop support is not an MVP requirement.

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

## 6. Planned post-MVP capabilities

These capabilities are intentionally deferred:

1. multiple simultaneous NUT servers;
2. historical charts and event retention;
3. authenticated read/write operations;
4. preserved-format editing of NUT configuration files;
5. automatic timestamped backups and restoration;
6. driver test workflows;
7. Windows service control;
8. systemd and OpenRC service control;
9. serial-port discovery and direct driver diagnostics;
10. installation and update workflows;
11. remote administrative agent;
12. notifications and shutdown-policy management.

Every administrative capability must require explicit confirmation and a rollback path.

## 7. Configuration editing requirements for later phases

When implemented, configuration management must:

- parse NUT syntax without treating every file as a generic INI document;
- preserve comments and unknown directives;
- create a backup before writing;
- validate generated configuration;
- write to a temporary file and replace atomically;
- restore the previous configuration if activation fails;
- separate ordinary user operations from elevated operations;
- never reveal stored credentials after saving.

## 8. Upstream strategy

NutManager should first identify and document limitations through real usage. Improvements to NUT shall be proposed only when the limitation belongs in NUT rather than in the client application.

Upstream work shall use:

- official repository: `networkupstools/nut`;
- contributor fork: `Marcelo-PX/nut`;
- focused branches and independent pull requests;
- NUT contribution, style, testing, licensing, and DCO requirements.

The upstream repository shall not be embedded in the normal NutManager workspace.

## 9. MVP acceptance criteria

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
