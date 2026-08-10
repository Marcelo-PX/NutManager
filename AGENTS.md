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

## Platform policy

- Windows x64 is the primary and official platform for development, CI, testing, packaging, distribution, and local administration.
- Official CI runs on Windows only.
- Linux is best-effort compatibility for shared code, not an official CI, release, or administration target.
- Do not treat Linux as a required CI or release target unless a task explicitly requests T22 or Linux work.

## Token and context economy

- Do not clone, add, index, or inspect the full `networkupstools/nut` repository during ordinary NutManager work.
- Consult NUT upstream only when the task explicitly requests upstream research, compatibility analysis, or a contribution.
- Reuse decisions already recorded in the project documentation; do not repeatedly reconsider the selected stack or architecture.
- Avoid verbose generated documentation and duplicated explanations.

## Safety

Automated tests and development fixtures must not depend on external network access, internet connectivity, a real NUT server, UPS, serial port, USB device, service, or elevation. Deterministic in-process or loopback fake servers and ephemeral local sockets are allowed for protocol tests. Tests must not mutate real configuration, services, ACLs, drivers, hardware, or credentials. Use mocks, temporary directories, and deterministic fixtures.

Read-only inspection of a real NUT environment is allowed only when the task explicitly authorizes it. Record exactly what was observed and do not hardcode an environment observation as a universal product rule.

Real mutations require an explicit task and confirmation or scope specific to that action. Never perform them by assumption. Do not store or print credentials, secrets, or passwords, and do not run destructive Git operations such as forced resets or unrequested history rewrites.

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

Configuration editing uses the syntax-preserving model and the T14 write pipeline. Preserve comments, ordering, quoting, spacing where practical, unknown directives, and unmanaged sections. Every write must use preview, backup, temporary-file validation, safe replacement, verification, and rollback as applicable; UI code must not write configuration files directly. Never log secrets, and never restart a service automatically after configuration apply.

## Git safety

For a new task, require `main` and a clean working tree, then run `git fetch --prune origin`, `git pull --ff-only origin main`, and verify `HEAD` equals `origin/main`. Confirm the target branch does not exist locally or remotely before creating it.

Never automatically stash, reset, clean, discard changes, force-push, force-with-lease, commit or push to `main`, rebase, merge, or delete a branch. Stage explicit files only: never use `git add .`, `git add -A`, or `git add --all`.

Before a commit, run `git status --short`, `git diff --stat`, `git diff --name-only`, and `git diff --check`. After staging, run the equivalent `git diff --cached` checks. Push only the task branch. Open a PR, merge, or clean up branches only when the workflow or a human explicitly requests it.

## Quality requirements

- Enable and respect nullable reference types.
- Keep warnings introduced by project code at zero.
- Add or update tests for behavior changes.
- Use deterministic tests; avoid real hardware, services, network access, or wall-clock timing where possible.
- Keep user-facing text clear and localizable; do not bury domain logic in AXAML code-behind.

## Validation

Run the validation relevant to the task, normally:

```bash
dotnet restore NutManager.sln
dotnet build NutManager.sln --configuration Release --no-restore
dotnet test NutManager.sln --configuration Release --no-build
dotnet list NutManager.sln package --vulnerable --include-transitive
dotnet format NutManager.sln --verify-no-changes --no-restore
git diff --check
```

If a command cannot be run, report the exact limitation rather than claiming success.

## Completion response

At the end of a task, report only:

1. files created or modified;
2. concise description of the implementation;
3. commands executed and their results;
4. known limitations directly related to the task.

Then stop.
