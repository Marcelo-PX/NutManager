# MVP package validation

NutManager is Windows-first. The official MVP package is a portable, self-contained Windows x64 archive that includes the required .NET runtime and does not require a separate runtime installation.

## Official package

- Windows x64: `NutManager-win-x64.zip`

Extract the archive to a writable directory and run `NutManager.App.exe`. There is currently no installer, automatic update workflow, code signing, or GitHub Release.

The unsigned executable may trigger a Windows reputation or SmartScreen warning.

## Platform policy

Windows x64 is the primary development, manual-testing, and distribution platform. Linux has secondary, best-effort compatibility in shared code, but no official package and no current commitment for administrative capabilities. Alpine Linux desktop is not validated or supported for the MVP.

## Runtime behavior

The application stores its local, non-secret settings in the operating-system-appropriate per-user application-data directory. It does not package or migrate settings, logs, NUT configuration, or credentials.

NUT monitoring uses TCP port `3493` by default. To use a real NUT server, the user must have TCP connectivity to the configured endpoint. The MVP runs without administrator privileges and never modifies NUT files, services, drivers, or hardware.

## Tested package path

The Windows x64 package is published self-contained, archived, extracted, and started from the extracted executable.

## Manual real-NUT checklist

Automated packaging validation uses mock mode and does not require a real NUT server or UPS. Before declaring the MVP fully validated against a live Windows environment, perform this read-only checklist manually:

1. Start the Windows package.
2. Disable mock mode.
3. Configure the NUT endpoint.
4. Discover available UPS devices.
5. Select a UPS.
6. Confirm that a snapshot and variables are displayed.
7. Confirm the polling interval is respected.
8. Safely simulate a connection loss without changing the UPS.
9. Confirm `Stale` and `Reconnecting` state.
10. Restore connectivity.
11. Confirm `Connected` and `Fresh` state.

Do not send UPS commands, edit NUT configuration, access serial ports, or stop services during this validation.
