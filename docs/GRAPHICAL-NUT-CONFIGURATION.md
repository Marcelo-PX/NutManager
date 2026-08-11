# Graphical NUT Configuration

## Product direction

For supported configuration, administrators should not need manual `.conf` editing. T26–T28 provide dedicated graphical experiences for `nut.conf`, `ups.conf`, `upsd.conf`, `upsd.users`, and `upsmon.conf`; raw generated configuration remains a read-only advanced preview. Every page has Basic, Advanced, Custom parameters, a pending-change bar, and the review drawer.

## UPS (`ups.conf`)

**Administration → NUT Configuration → UPS** has a configured-UPS selector plus Add, Rename, and Remove actions with section-name validation. Identification contains UPS name and `desc`. Driver selection supports detected/installed choices, explicit documented input, and Detect automatically; detection persists a concrete required driver after confirmation.

Port control depends on the driver: Automatic, COM selector, USB auto, network endpoint, device path, or custom. Local COM choices use passive Windows enumeration; remote profiles do not pretend to enumerate a server's COM ports. Protocol choices and Automatic are offered only where a driver schema documents them. Dynamic Basic/Advanced driver parameters can model documented connection, polling, battery, and driver-specific options. UI-only helper metadata is never emitted as an invented NUT directive.

The planned runtime-calibration assistant collects high/low load percentages and runtimes, validates high load > low load and positive valid values, generates official `runtimecal` syntax, previews it, and changes only the semantic draft after **Use calibration**. It does not fabricate `battery.runtime`.

## Server and general files

**Server (`upsd.conf`)** groups repeated `LISTEN` address/port rows, server behavior, timeouts, TLS/certificates, advanced settings, and custom parameters. Address and port validation applies to every row.

**General (`nut.conf`)** exposes a primary NUT MODE selector from supported documented modes and advanced documented options. Its serializer respects `nut.conf` grammar; it is not assumed to be generic `key = value` syntax.

## Users and monitoring

**Users (`upsd.users`)** uses cards/list rows for username, role, actions, instant-command permissions, and password state. It supports add/rename/remove, change-only password replacement, and permission editing. Passwords are never revealed; dangerous permissions such as FSD show warnings, and configuring permission is distinct from executing it.

**Monitoring (`upsmon.conf`)** provides repeated graphical `MONITOR` rows for UPS, host, optional port, power value, username, change-only password, and primary/secondary role. General controls include `MINSUPPLIES`, timing/retry, `POWERDOWNFLAG`, `SHUTDOWNCMD`, `FINALDELAY`, notifications, advanced settings, and custom parameters. Secrets remain redacted.

## Review and transport

The first draft shows a bottom bar with pending count, Discard, and Review changes. The drawer explains semantic changes, validation, generated preview, backup/recovery, and explicit Apply. The same form is used locally, through SFTP, and through SMB; transport changes only readiness, capability, path, and write implementation. Remote management is never remote NutManager access.
