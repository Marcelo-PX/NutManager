# MVP package validation

NutManager MVP is distributed as portable, self-contained packages for Windows x64 and mainstream desktop Linux x64. The package includes the .NET runtime required by the application; no separate .NET installation is required.

## Packages

- Windows x64: `NutManager-win-x64.zip`
- Linux x64: `NutManager-linux-x64.tar.gz`

Extract the archive to a writable directory and run `NutManager.App.exe` on Windows or `./NutManager.App` on Linux. There is no installer, automatic update workflow, code signing, or GitHub Release in this MVP step.

On Windows, the unsigned executable may trigger a reputation or SmartScreen warning. On Linux, Avalonia requires the graphical libraries normally provided by a mainstream desktop distribution. Alpine Linux desktop is not validated or supported for the MVP.

## Runtime behavior

The application stores its local, non-secret settings in the operating-system-appropriate per-user application-data directory. It does not package or migrate settings, logs, NUT configuration, or credentials.

NUT uses TCP port `3493` by default. To use a real NUT server, the user must have TCP connectivity to the configured endpoint. The MVP runs without administrator or root privileges and never modifies NUT files, services, drivers, or hardware.

## Tested package paths

- Windows x64 package: published self-contained, archived, extracted, and started from the extracted executable.
- Linux x64 package: published self-contained and archived by the Ubuntu workflow; the workflow extracts and starts it with Xvfb before terminating the smoke-test process.

## Manual real-NUT checklist

Automated packaging validation uses mock mode and does not require a real NUT server or UPS. Before declaring the MVP fully validated against a live environment, perform this read-only checklist manually:

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
