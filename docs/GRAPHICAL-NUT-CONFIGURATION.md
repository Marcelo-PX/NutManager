# Graphical NUT Configuration

## Product direction

For supported configuration, administrators should not need manual `.conf` editing. T26–T28 provide dedicated graphical experiences for `nut.conf`, `ups.conf`, `upsd.conf`, `upsd.users`, and `upsmon.conf`; raw generated configuration remains a read-only advanced preview. Every page has Basic, Advanced, Custom parameters, a pending-change bar, and the review drawer.

## UPS (`ups.conf`)

**Administration → NUT Configuration → UPS** has a configured-UPS selector plus Add, Rename, and Remove actions with section-name validation. Identification contains UPS name and `desc`. Driver selection supports passively detected/installed choices and explicit technical input. Passive discovery never selects or persists a driver automatically.

Port control depends on the driver: serial-capable schemas can suggest passively enumerated local COM names, `usbhid-ups` suggests its documented `auto` token, and network or unknown drivers retain explicit technical input. Remote profiles do not pretend to enumerate a server's COM ports. Protocol choices and Automatic are offered only where a driver schema documents them. Dynamic Basic/Advanced driver parameters model documented connection, polling, battery, and driver-specific options. UI-only helper metadata is never emitted as an invented NUT directive.

The implemented runtime-calibration assistant collects high/low load percentages and runtimes, validates high load > low load and positive valid values, generates official `runtimecal = runtime_high,load_high,runtime_low,load_low` syntax, previews it, and changes only the semantic draft after **Use calibration**. It does not fabricate `battery.runtime`, start a process, run a calibration command, or discharge the UPS.

### Implemented driver coverage and limits

The structured production catalog covers documented options used by `nutdrv_qx`, `usbhid-ups`, and `snmp-ups`. It includes shared `ups.conf` retry/polling/description settings, applicable USB matching, documented Qx protocol/battery/runtime settings, USB HID selection/reconnect flags, and SNMP version/MIB/security fields. Sensitive SNMP values use change-only replacement and removal. The application never treats this list as every NUT driver: other valid driver executables found passively under the detected installation, and existing valid configured names, stay selectable with a visible limited-validation warning.

Local COM choices reuse passive T17 metadata and do not open a port. Remote profiles do not receive local COM metadata. The form does not execute a driver or `upsdrvctl`; safe diagnostics remain the separately confirmed T17 workflow. Apply remains T14 preview/backup/temporary validation/replace/verify/rollback, including the existing T19 SFTP and T19B SMB transports.

Primary references used for the production descriptors are the official NUT `ups.conf(5)`, `nutdrv_qx(8)`, `usbhid-ups(8)`, and `snmp-ups(8)` manuals. Where documentation does not establish a universal default or range, NutManager does not invent one.

## Server and general files

**Server (`upsd.conf`)** groups repeated `LISTEN` address/optional-port rows, server behavior, timeouts, TLS/certificates, advanced settings, and custom parameters. Each occurrence is edited directly through stable draft identity; other listeners, comments, and unknown directives remain in place. IPv4, IPv6, hostnames, and the documented wildcard are validated syntactically without DNS resolution, socket bind, or fabricated default listeners. Wildcards produce a non-blocking exposure warning. `LISTEN` review records restart-required activation but Apply never restarts `upsd`.

The server schema covers `MAXAGE`, `TRACKINGDELAY`, `ALLOW_NO_DEVICE`, `ALLOW_NOT_ALL_LISTENERS`, `STATEPATH`, `MAXCONN`, `CERTFILE`, `CERTPATH`, `CERTIDENT`, `CERTREQUEST`, `DISABLE_WEAK_SSL`, and `DEBUG_MIN` as documented for NUT 2.8.5. It does not infer the compiled TLS backend or inspect certificate files. `CERTIDENT` is a protected NSS composite: existing passwords are never projected; replacement requires an explicit identity and new transient password.

**General (`nut.conf`)** exposes required `MODE` (`none`, `standalone`, `netserver`, `netclient`) plus the documented 2.8.5 Advanced service/integration/debug settings. A missing mode remains `MissingRequired`; the documented default is not written on page open. New assignments use the mandatory `KEY=value` grammar with no spaces around `=`, while existing formatting remains untouched. Package-dependent options remain explicitly advanced and NutManager does not execute their contents.

The production source is the official historic NUT 2.8.5 `nut.conf(5)` and `upsd.conf(5)` documentation. These settings can be package-, platform-, or TLS-backend-dependent on Windows, so the UI explains scope without claiming runtime activation. Generated configuration is read-only and all explicit Apply operations continue through the existing reviewed local/SFTP/SMB pipeline.

## Users and monitoring

**Users (`upsd.users`)** uses cards/list rows for username, role, actions, instant-command permissions, and password state. It supports add/rename/remove, change-only password replacement, and permission editing. Passwords are never revealed; dangerous permissions such as FSD show warnings, and configuring permission is distinct from executing it.

**Monitoring (`upsmon.conf`)** provides repeated graphical `MONITOR` rows for UPS, host, optional port, power value, username, change-only password, and primary/secondary role. General controls include `MINSUPPLIES`, timing/retry, `POWERDOWNFLAG`, `SHUTDOWNCMD`, `FINALDELAY`, notifications, advanced settings, and custom parameters. Secrets remain redacted.

## Review and transport

The first draft shows a bottom bar with pending count, Discard, and Review changes. The drawer explains semantic changes, validation, generated preview, backup/recovery, and explicit Apply. The same form is used locally, through SFTP, and through SMB; transport changes only readiness, capability, path, and write implementation. Remote management is never remote NutManager access.
