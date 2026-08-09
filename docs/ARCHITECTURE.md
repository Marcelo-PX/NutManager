# NutManager Architecture

## 1. Architectural goals

- Windows-first desktop UI with secondary, best-effort Linux compatibility in shared code;
- safe read-only MVP;
- clear separation of domain, UI, protocol, and operating-system concerns;
- testability without a real UPS or NUT server;
- minimal dependencies and low context cost for coding agents;
- a future path to Windows local and SSH/SFTP remote administration without redesigning the MVP.

## 2. Selected stack

- C#;
- current supported .NET LTS selected when the solution is created;
- Avalonia Desktop and AXAML;
- MVVM with CommunityToolkit.Mvvm;
- built-in .NET dependency injection and logging only when required;
- xUnit for automated tests;
- `System.Text.Json` for NutManager's own settings.

Package versions shall be centrally managed through `Directory.Packages.props` after the solution is created.

## 3. Solution structure

```text
NutManager/
├── src/
│   ├── NutManager.App/
│   │   ├── Assets/
│   │   ├── Controls/
│   │   ├── Styles/
│   │   ├── ViewModels/
│   │   └── Views/
│   ├── NutManager.Core/
│   │   ├── Models/
│   │   ├── Services/
│   │   ├── Status/
│   │   └── Validation/
│   └── NutManager.Infrastructure/
│       ├── NutProtocol/
│       ├── Persistence/
│       ├── Mock/
│       └── Platform/
├── tests/
│   └── NutManager.Tests/
└── docs/
```

Directories should be created only when the task introducing them requires them.

## 4. Dependency direction

```text
NutManager.App
    ├──> NutManager.Core
    └──> NutManager.Infrastructure

NutManager.Infrastructure
    └──> NutManager.Core

NutManager.Core
    └──> no UI or platform project
```

### Core

Contains models, interfaces, status interpretation, validation rules, stale-state rules, and other deterministic domain behavior.

Core must not reference Avalonia, file-system locations, sockets, serial ports, service-control APIs, or operating-system-specific packages.

### Infrastructure

Contains implementations for:

- NUT network protocol access;
- local settings persistence;
- mock data;
- clocks and timers when abstraction is needed;
- later Windows platform and remote-management integrations.

### App

Contains Avalonia startup, navigation, views, view models, styles, resources, and dependency composition.

View models depend on Core abstractions. Views must not call NUT commands, sockets, services, or file-system operations directly.

## 5. Product capability split

```text
NutManager
├── Monitoring
│   └── NUT Protocol
└── Management
    ├── Local
    │   └── Windows platform adapter
    └── Remote
        └── SSH/SFTP transport
```

Monitoring uses the NUT protocol, normally over TCP port `3493`. Management is a separate concern with independent connection state: local management uses filesystem and platform APIs, while remote management uses a secure transport.

Future management concepts include `ManagedNutServer` and `ManagementMode` (`Local` or `Remote`). They are architectural concepts only; types are not introduced until a task requires them. Management capabilities will include reading and writing configuration, managing services, inspecting installations, and reporting privileged-operation availability.

Windows is the primary development, manual-test, distribution, and first-administration platform. Linux remains secondary, best-effort compatibility. Shared code must not take a Windows dependency unless the behavior genuinely belongs to the Windows platform adapter.

## 6. Initial domain model

The exact types may be refined during implementation, but the domain should represent:

```text
NutEndpoint
UpsIdentity
UpsSnapshot
UpsVariable
UpsStatusToken
ConnectionState
DataFreshness
DiagnosticResult
ApplicationSettings
```

`UpsSnapshot` should retain:

- normalized values used by the UI;
- the original variable dictionary;
- timestamp of successful retrieval;
- source indication such as live or simulated.

Numeric values must use culture-invariant protocol parsing and culture-aware display formatting.

## 7. Key abstractions

The MVP should converge on small interfaces similar to:

```csharp
public interface INutClient
{
    Task<IReadOnlyList<UpsIdentity>> ListUpsAsync(
        NutEndpoint endpoint,
        CancellationToken cancellationToken);

    Task<UpsSnapshot> GetSnapshotAsync(
        NutEndpoint endpoint,
        string upsName,
        CancellationToken cancellationToken);
}

public interface IApplicationSettingsStore
{
    Task<ApplicationSettings> LoadAsync(CancellationToken cancellationToken);
    Task SaveAsync(ApplicationSettings settings, CancellationToken cancellationToken);
}
```

Do not add interfaces merely to wrap trivial pure functions. Introduce abstractions at I/O or platform boundaries.

## 8. Data flow

```text
View
  ↕ binding
ViewModel
  ↕ Core models and interfaces
Application service
  ↕
INutClient / settings store / mock provider
  ↕
NUT server or local application-data storage
```

External work runs asynchronously. Results are marshalled back to observable view-model state without blocking the UI thread.

## 9. NUT integration

### MVP protocol strategy

Prefer direct implementation of the small read-only portion of the NUT network protocol required by the MVP rather than launching `upsc` for every poll.

Reasons:

- works consistently on Windows and Linux;
- avoids process startup overhead;
- avoids locating external executables;
- enables precise cancellation and timeout handling;
- is testable with an in-process fake server.

The implementation must be scoped to documented, observed commands needed for:

- listing UPS devices;
- listing variables for one UPS;
- returning protocol errors without losing their text.

Protocol parsing shall preserve unknown variable names and values.

Launching NUT tools remains an option for later diagnostics and administrative features, behind a dedicated process-execution service.

### Polling

- only one poll per selected UPS may be active;
- cancellation must stop an in-flight poll;
- retries must be bounded;
- reconnect delay must not create a busy loop;
- failed polls preserve the last successful snapshot but mark it stale;
- the polling service must be disposable.

## 10. Status interpretation

`ups.status` is a space-separated set of tokens. Parsing must:

- preserve all original tokens;
- recognize common tokens;
- allow multiple simultaneous states;
- avoid reducing the value to a single boolean;
- map recognized tokens to user-facing descriptions and severity;
- display unknown tokens verbatim.

Severity presentation must not rely on color alone.

## 11. Settings persistence

MVP settings are non-secret and stored per user.

Requirements:

- operating-system-appropriate application-data directory;
- UTF-8 JSON;
- schema/version field for future migration;
- write to a temporary file then atomically replace;
- tolerate missing files by returning defaults;
- report malformed files clearly and avoid overwriting them automatically without confirmation.

## 12. Mock provider

The mock implementation must be deterministic and support scenarios including:

- online normal operation;
- on battery;
- low battery;
- overloaded;
- replace battery;
- missing optional values;
- disconnected;
- stale data;
- unknown status token.

It must be visibly labeled as simulated in the UI.

## 13. Error handling

Errors are represented in layers:

- technical exception or protocol error in Infrastructure;
- actionable application error in Core/application service;
- concise message plus optional details in the UI.

Expected connection failures must not terminate the process. Cancellation must not be reported as a fault when initiated by navigation, shutdown, or a new connection attempt.

## 14. Logging

Use structured logging only where it provides diagnostic value.

Never log:

- passwords;
- complete future credential-bearing configuration files;
- secret command arguments;
- unnecessary raw data at high frequency.

Logs should include endpoint, operation, duration, result category, and exception type where safe.

## 15. Windows-first platform boundary

The monitoring MVP and Core remain platform-neutral. Windows-specific management implementations belong under an explicit namespace such as `Infrastructure.Platform.Windows`; Core must never depend on Windows APIs or packages.

Linux has secondary, best-effort compatibility in shared code. It has no official package or immediate administrative-feature commitment. Any future Linux management adapter must remain isolated behind the same platform boundaries rather than influencing Core prematurely.

Likely Windows-specific concerns include services, UAC, Event Log, `COMx` ports, drivers, and ACLs. Do not leak those differences into Core models unless the domain genuinely requires them.

## 16. Testing strategy

### Unit tests

Cover:

- status token parsing and severity;
- culture-invariant numeric parsing;
- variable mapping;
- stale-state transitions;
- settings validation and migration;
- deterministic mock scenarios.

### Protocol tests

Use recorded protocol lines or an in-process fake TCP server. Cover partial reads, multiple lines, malformed replies, protocol errors, cancellation, and timeout.

### UI tests

Keep most view-model behavior testable without rendering. Add UI automation only after stable flows justify its maintenance cost.

### Prohibited test dependencies

Tests must not require:

- a real NUT server;
- a real UPS;
- internet access;
- elevated privileges;
- system service changes;
- serial ports.

## 17. Future configuration architecture

Configuration editing is post-MVP and requires a syntax-preserving document model rather than a generic INI serializer. It applies to `nut.conf`, `ups.conf`, `upsd.conf`, `upsd.users`, and `upsmon.conf`; comments, order, unknown directives, unmanaged sections, quoting, and relevant formatting must remain preserved.

The write pipeline shall be:

```text
read → parse while preserving syntax → requested change → preview/diff → backup
→ temporary file → validation → safe replacement → activation when necessary
→ test → rollback on failure
```

Local management will discover the local NUT installation, executables, version, and configuration directory, while allowing manual path correction. Remote management will require manual directory selection and validation over SSH/SFTP; it must not attempt remote directory autodiscovery.

Administrative activation shall be separated from ordinary UI execution and require explicit user confirmation.

## 18. Upstream NUT workflow

The official NUT repository is not a project dependency or submodule.

When an upstream task is approved:

1. reproduce and document the limitation independently;
2. open the separate local NUT checkout;
3. update from `networkupstools/nut:master`;
4. create a focused branch in `Marcelo-PX/nut`;
5. follow NUT style, tests, DCO, and disclosure requirements;
6. keep NutManager and NUT commits separate;
7. submit a focused PR only after local validation.

## 19. Fixed decisions for the initial implementation

Coding agents must not rediscuss these without an explicit architecture task:

- Avalonia is the desktop UI framework;
- Windows x64 is the primary development, testing, distribution, and first-administration platform;
- Linux is secondary, best-effort compatibility rather than an official distribution target;
- MVVM is used;
- the first milestone is read-only;
- Core remains platform-independent;
- the NUT repository is not embedded in the workspace;
- real hardware and administrative actions are excluded from automated tests;
- direct read-only NUT protocol access is preferred for the MVP;
- configuration editing and service control are post-MVP;
- monitoring and management have independent connection state.
