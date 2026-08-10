# MVP package validation

NutManager is Windows-first. The official MVP package is a portable, self-contained Windows x64 archive that includes the required .NET runtime and does not require a separate runtime installation.

## Official package

- Windows x64: `NutManager-win-x64.zip`

Extract the archive to a writable directory and run `NutManager.App.exe`. There is currently no installer, automatic update workflow, code signing, or GitHub Release.

## Platform policy

Windows x64 is the official development, CI, manual-testing, and distribution platform. Linux remains secondary, best-effort shared-code compatibility, with no official CI, package, or current administration-support commitment.

## T11 acceptance scope

This document covers the read-only monitoring acceptance of T11. The current product also contains explicitly confirmed administration capabilities, but they are outside this checklist. During T11 validation do not edit configuration, control services, run driver diagnostics, access COM or hardware, request elevation, or change ACLs.

## Runtime setup

The application stores non-secret settings and managed profile metadata in an operating-system-appropriate per-user application-data directory. It does not package or migrate NUT configuration or credentials.

To connect to a real NUT server:

1. Select or create a managed profile.
2. Set its Monitoring host and port and, if desired, its preferred UPS.
3. Make the profile active.
4. Restart the application if the active profile changed.
5. Disable mock mode.
6. Run discovery and polling.

## Manual real-NUT checklist

Automated package validation uses mock mode and does not require a real NUT server or UPS. T11 remains **IN PROGRESS** until this checklist is completed against a real Windows NUT environment using the distributed package:

1. Start the Windows package.
2. Configure and activate the managed monitoring profile.
3. Discover available UPS devices.
4. Select a UPS.
5. Confirm that a snapshot and variables are displayed.
6. Confirm the polling interval is respected.
7. Safely simulate a connection loss without changing the UPS.
8. Confirm `Stale` and `Reconnecting` state.
9. Restore connectivity.
10. Confirm `Connected` and `Fresh` state.

Do not send UPS commands, edit NUT configuration, control services, run driver diagnostics, open serial ports, or perform administrative actions during this validation.
