# Graphical NUT Configuration

## Product direction

For supported configuration, administrators do not need manual `.conf` editing. T26–T28 delivered dedicated graphical experiences for all five supported files — `nut.conf`, `ups.conf`, `upsd.conf`, `upsd.users`, and `upsmon.conf` — and raw generated configuration remains a read-only advanced preview. Each form uses the semantic groups applicable to that file and shares the pending-change bar and review drawer; unsupported content remains preserved rather than forced into an invented control.

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

## Users (`upsd.users`)

Every section of `upsd.users` is one NUT account, so the form works one user at a time instead of flattening sections into a single page. A selector lists the configured users with Add, Rename, and Remove actions; a name must be a single token without whitespace, brackets, or `#`, and must not collide with an existing account. An empty file shows an empty state rather than inventing a first user.

**Password** shows only *configured*, *not configured*, or *new password set*. The stored value is never displayed, echoed, placed in a tooltip, or written to review, preview, or diagnostics. Changing one opens two write-only fields whose contents go straight into the semantic mutation and are cleared immediately; a mismatch between them is refused with a localized reason and nothing is written.

**Permissions** are `SET` and `FSD` checkboxes. Enabling `FSD` displays a warning that this permission lets the account request a forced shutdown, which can start the shutdown process on `upsmon` clients — NutManager records the permission and never exercises it. Clearing every permission removes the directive rather than writing an empty one. Permission tokens this release does not manage are listed as preserved and are written back untouched.

**Instant commands** offer three modes: none, all (`ALL`), or an explicit list. Choosing `ALL` warns that the account will be able to trigger every instant command the server and driver authorise. Removing the last entry of an explicit list drops the directive instead of leaving it empty.

**Use by `upsmon`** selects none, `primary`, or `secondary`. `primary` warns that such a client can take part in forced-shutdown coordination. The historic `master`/`slave` spellings are read and preserved as written; `primary`/`secondary` is the terminology the interface uses for new values.

## Monitoring (`upsmon.conf`)

**`MONITOR` rows** are edited as cards: system identifier (`ups`, `ups@host`, or `ups@host:port`), power value, username, and `primary`/`secondary` role, plus the password state. The credential lives inside the line between ordinary arguments, so editing a power value or username preserves the stored password without ever revealing it, and changing the password replaces only that token. Adding a row requires a credential, because a new line has none to preserve. A structurally incomplete `MONITOR` line is reported rather than silently accepted, and is preserved as written.

**General settings** cover `MINSUPPLIES`; the shutdown group `SHUTDOWNCMD`, `POWERDOWNFLAG`, `FINALDELAY`, and `HOSTSYNC`; and the polling group `POLLFREQ`, `POLLFREQALERT`, `DEADTIME`, `NOCOMMWARNTIME`, and `RBWARNTIME`. Timers are written as plain numbers without a unit suffix; the unit appears only as interface help. An absent timer stays absent instead of being materialised with a documented default.

**Notifications** are a matrix over the 29 documented events. Each row exposes `SYSLOG`, `WALL`, `EXEC`, and `IGNORE` flags plus an optional custom message. `IGNORE` is exclusive in both directions: selecting it clears the others, and selecting another flag clears it. Clearing every flag removes the directive. `NOTIFYCMD` is a separate field, and marking events `EXEC` without configuring one raises an advisory warning, since `upsmon` accepts the combination but never runs anything. An event this release does not know is listed separately as preserved and survives edits to the events that are managed.

**What the form does not do.** `SHUTDOWNCMD` and `NOTIFYCMD` are recorded as text. NutManager writes this file; it does not run a shutdown command, run a notification command, issue a forced shutdown, or restart NUT after an apply.

Examples in this document never contain a real credential, and the interface has no view that would produce one.

## Review and transport

The first draft shows a bottom bar with pending count, Discard, and Review changes. The drawer explains semantic changes, validation, generated preview, backup/recovery, and explicit Apply. The same form is used locally, through SFTP, and through SMB; transport changes only readiness, capability, path, and write implementation. Remote management is never remote NutManager access.
