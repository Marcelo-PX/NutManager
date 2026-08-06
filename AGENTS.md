# AGENTS.md

This file defines mandatory operating rules for coding agents working in this repository.

## Primary objective

Complete only the requested task with the smallest correct change set. Do not expand scope, redesign the project, or begin a subsequent task automatically.

## Required reading order

Read only what is needed:

1. this file;
2. the specific task in `docs/TASKS.md`;
3. relevant sections of `docs/SPEC.md` and `docs/ARCHITECTURE.md`;
4. files directly involved in the requested change.

Do not scan the whole repository unless the task genuinely requires it.

## Scope control

- Modify only files required by the active task.
- Do not refactor unrelated code.
- Do not rename public types, projects, directories, or namespaces without explicit instruction.
- Do not add optional features, placeholders, abstractions, or dependencies pre-emptively.
- Do not start the next task after completing the current one.
- Prefer direct, readable implementations over speculative frameworks.

## Token and context economy

- Do not clone, add, index, or inspect the full `networkupstools/nut` repository during ordinary NutManager work.
- Consult NUT upstream only when the task explicitly requests upstream research, compatibility analysis, or a contribution.
- Reuse decisions already recorded in the project documentation; do not repeatedly reconsider the selected stack or architecture.
- Avoid verbose generated documentation and duplicated explanations.

## Safety

During development and automated tests, agents must not:

- access or modify a real NUT installation;
- edit real `ups.conf`, `upsd.conf`, `upsd.users`, `upsmon.conf`, or `nut.conf` files;
- start, stop, restart, install, or remove system services;
- access real serial ports or USB devices;
- execute commands requiring administrator, root, UAC, `sudo`, or `polkit` authorization;
- store or print credentials, secrets, or passwords;
- run destructive Git commands such as forced resets or unrequested history rewrites.

Use mocks, temporary directories, and test fixtures.

## Dependencies

- Do not add a NuGet package without a requirement that cannot reasonably be met by the existing stack or .NET libraries.
- When a dependency is necessary, state the reason in the completion summary.
- Keep package versions centrally managed when `Directory.Packages.props` exists.

## Architecture rules

- `NutManager.Core` must not depend on Avalonia or platform-specific APIs.
- `NutManager.Infrastructure` may depend on `NutManager.Core`.
- `NutManager.App` may depend on `NutManager.Core` and `NutManager.Infrastructure`.
- Platform-specific behavior must remain behind interfaces.
- UI code must not execute raw NUT, service-control, or operating-system commands directly.
- Cancellation, timeout, and error handling are required for external I/O.
- Missing UPS variables must remain missing; do not invent values or silently substitute estimates.

## Configuration integrity

When configuration editing is introduced in a later task:

- preserve comments, ordering, quoting, spacing where practical, unknown directives, and unmanaged sections;
- create a timestamped backup before any write;
- write through a temporary file and atomically replace the destination;
- validate before activation;
- never log secrets;
- tests must use temporary files only.

## Quality requirements

- Enable and respect nullable reference types.
- Keep warnings introduced by project code at zero.
- Add or update tests for behavior changes.
- Use deterministic tests; no dependency on real hardware, services, network access, or wall-clock timing where avoidable.
- Keep user-facing text clear and localizable; do not bury domain logic in AXAML code-behind.

## Validation

Run the validation relevant to the task, normally:

```bash
dotnet restore
dotnet build
dotnet test
```

If a command cannot be run, report the exact limitation rather than claiming success.

## Completion response

At the end of a task, report only:

1. files created or modified;
2. concise description of the implementation;
3. commands executed and their results;
4. known limitations directly related to the task.

Then stop.
