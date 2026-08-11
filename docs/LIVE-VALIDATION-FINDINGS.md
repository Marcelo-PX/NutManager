# Live validation findings

## Status

These are current findings from the ongoing Windows/NUT validation stream (T21). They do not mean that T21 acceptance is complete, and they do not change the safety boundaries already implemented for configuration, privileged administration, driver diagnostics, SSH/SFTP, SMB, or protected credentials.

| ID | Finding | Planned response |
| --- | --- | --- |
| F-01 | A host input accepted `NOBREAK@127.0.0.1`, mixing UPS identity with a network host. | Resolved in T24A by pure typed host validation. |
| F-02 | Mock mode currently defaults to enabled and confused the first live test. | Resolved in T24A: new installs default disabled and persisted legacy choices are preserved. |
| F-03 | Starting a Remote draft then choosing Local is blocked by dirty-draft handling. | Resolved in T24A by one reversible Local/Remote draft. |
| F-04 | A red Windows system accent colors normal selection and resembles an error. | T24 product-owned selection tokens. |
| F-05 | Internal enum values such as `Smb`, `CurrentWindowsIdentity`, `Manage`, and `Remote` reach the UI. | Resolved for the T24A managed-profile surface through localized presentation options. |
| F-06 | NUT installation/configuration detection succeeds while version metadata can be unavailable. | Resolved in T24B by metadata-first, bounded read-only `upsdrvctl.exe -V` fallback. |
| F-07 | Administration combines configuration, remote access, drivers, Windows service, ACL, processes, events, and results in one long surface. | Resolved in T24B with four focused presentation areas over the same capability context. |
| F-08 | COM and driver areas need empty states and grouped commands. | Resolved in T24B with localized empty states and intention-grouped diagnostic commands. |
| F-09 | Active-profile change requires restart, but restart-required is not a first-class state. | Resolved in T24A by comparing startup runtime and persisted active profile IDs. |
| F-10 | `ApplicationSettings` still mirrors legacy endpoint fields while managed profiles are the active profile source. | Resolved in T24A schema v3; legacy endpoint fields are read-only migration compatibility. |
| F-11 | The shell and pages have nested scrolling and rigid grids. | Resolved for current Overview, Devices, Diagnostics, and Administration surfaces by T24/T24B single-scroll responsive composition. |
| F-12 | Current user-facing strings are largely hard-coded. | Resolved for T24/T24A/T24B touched surfaces through semantic `pt-BR`/`en-US` resources; future forms localize as introduced. |

## Observed positive baseline

The live check confirmed that the real NUT path, configuration path, `ups.conf`, configured UPS/driver/port/protocol information, and configuration-file availability can be discovered. These observations are environment findings, not universal product defaults.

## Constraints for follow-up work

Future remediation keeps syntactic validation free of DNS/I/O, keeps connection testing explicit, and preserves existing reviewed safe-write, UAC, credential, remote-host-key, SMB, COM, and driver interlock boundaries. No finding authorizes a configuration write, service activation, raw serial operation, or secret exposure.
