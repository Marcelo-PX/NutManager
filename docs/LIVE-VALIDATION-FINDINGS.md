# Live validation findings

## Status

These are current findings from the ongoing Windows/NUT validation stream (T21). They do not mean that T21 acceptance is complete, and they do not change the safety boundaries already implemented for configuration, privileged administration, driver diagnostics, SSH/SFTP, SMB, or protected credentials.

| ID | Finding | Planned response |
| --- | --- | --- |
| F-01 | A host input accepted `NOBREAK@127.0.0.1`, mixing UPS identity with a network host. | T24A typed host validation. |
| F-02 | Mock mode currently defaults to enabled and confused the first live test. | T24A mock/demo policy and migration. |
| F-03 | Starting a Remote draft then choosing Local is blocked by dirty-draft handling. | T24A reversible Local/Remote draft. |
| F-04 | A red Windows system accent colors normal selection and resembles an error. | T24 product-owned selection tokens. |
| F-05 | Internal enum values such as `Smb`, `CurrentWindowsIdentity`, `Manage`, and `Remote` reach the UI. | T24/T24A localized option presentation. |
| F-06 | NUT installation/configuration detection succeeds while version metadata can be unavailable. | T24B bounded read-only version fallback. |
| F-07 | Administration combines configuration, remote access, drivers, Windows service, ACL, processes, events, and results in one long surface. | T24B presentation decomposition. |
| F-08 | COM and driver areas need empty states and grouped commands. | T24B presentation work. |
| F-09 | Active-profile change requires restart, but restart-required is not a first-class state. | T24A profile UX. |
| F-10 | `ApplicationSettings` still mirrors legacy endpoint fields while managed profiles are the active profile source. | T24A source-of-truth migration plan. |
| F-11 | The shell and pages have nested scrolling and rigid grids. | T24/T24B single-scroll and responsive layout work. |
| F-12 | Current user-facing strings are largely hard-coded. | T24 localization foundation. |

## Observed positive baseline

The live check confirmed that the real NUT path, configuration path, `ups.conf`, configured UPS/driver/port/protocol information, and configuration-file availability can be discovered. These observations are environment findings, not universal product defaults.

## Constraints for follow-up work

Future remediation keeps syntactic validation free of DNS/I/O, keeps connection testing explicit, and preserves existing reviewed safe-write, UAC, credential, remote-host-key, SMB, COM, and driver interlock boundaries. No finding authorizes a configuration write, service activation, raw serial operation, or secret exposure.
