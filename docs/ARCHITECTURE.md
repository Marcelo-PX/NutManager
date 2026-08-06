# NutManager Architecture

## 1. Architectural goals

- modern cross-platform desktop UI;
- safe read-only MVP;
- clear separation of domain, UI, protocol, and operating-system concerns;
- testability without a real UPS or NUT server;
- minimal dependencies and low context cost for coding agents;
- a future path to administrative Windows and Linux features without redesigning the MVP.

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
- later Windows and Linux integrations.

### App

Contains Avalonia startup, navigation, views, view models, styles, resources, and dependency composition.

View models depend on Core abstractions. Views must not call NUT commands, sockets, services, or file-system operations directly.

## 5. Initial domain model

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

## 6. Key abstractions

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

## 7. Data flow

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

## 8. NUT integration

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

## 9. Status interpretation

`ups.status` is a space-separated set of tokens. Parsing must:

- preserve all original tokens;
- recognize common tokens;
- allow multiple simultaneous states;
- avoid reducing the value to a single boolean;
- map recognized tokens to user-facing descriptions and severity;
- display unknown tokens verbatim.

Severity presentation must not rely on color alone.

## 10. Settings persistence

MVP settings are non-secret and stored per user.

Requirements:

- operating-system-appropriate application-data directory;
- UTF-8 JSON;
- schema/version field for future migration;
- write to a temporary file then atomically replace;
- tolerate missing files by returning defaults;
- report malformed files clearly and avoid overwriting them automatically without confirmation.

## 11. Mock provider

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

## 12. Error handling

Errors are represented in layers:

- technical exception or protocol error in Infrastructure;
- actionable application error in Core/application service;
- concise message plus optional details in the UI.

Expected connection failures must not terminate the process. Cancellation must not be reported as a fault when initiated by navigation, shutdown, or a new connection attempt.

## 13. Logging

Use structured logging only where it provides diagnostic value.

Never log:

- passwords;
- complete future credential-bearing configuration files;
- secret command arguments;
- unnecessary raw data at high frequency.

Logs should include endpoint, operation, duration, result category, and exception type where safe.

## 14. Cross-platform boundary

MVP behavior is platform-neutral. Later administrative implementations belong under explicit namespaces such as:

```text
Infrastructure.Platform.Windows
Infrastructure.Platform.Linux
```

Likely future differences include:

- Windows services versus systemd/OpenRC;
- UAC versus polkit/sudo workflows;
- Event Log versus journald/syslog;
- `COMx` versus `/dev/tty*` devices;
- Windows ACLs versus POSIX permissions.

Do not leak those differences into Core models unless the domain genuinely requires them.

## 15. Testing strategy

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

## 16. Future configuration architecture

Configuration editing is post-MVP and requires a syntax-preserving document model rather than a generic INI serializer.

The write pipeline shall be:

```text
read → parse → validate requested change → create backup → write temporary file
→ validate candidate → atomic replace → activate/test → rollback on failure
```

Administrative activation shall be separated from ordinary UI execution and require explicit user confirmation.

## 17. Upstream NUT workflow

The official NUT repository is not a project dependency or submodule.

When an upstream task is approved:

1. reproduce and document the limitation independently;
2. open the separate local NUT checkout;
3. update from `networkupstools/nut:master`;
4. create a focused branch in `Marcelo-PX/nut`;
5. follow NUT style, tests, DCO, and disclosure requirements;
6. keep NutManager and NUT commits separate;
7. submit a focused PR only after local validation.

## 18. Fixed decisions for the initial implementation

Coding agents must not rediscuss these without an explicit architecture task:

- Avalonia is the desktop UI framework;
- the application is cross-platform;
- MVVM is used;
- the first milestone is read-only;
- Core remains platform-independent;
- the NUT repository is not embedded in the workspace;
- real hardware and administrative actions are excluded from automated tests;
- direct read-only NUT protocol access is preferred for the MVP;
- configuration editing and service control are post-MVP.
