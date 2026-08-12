# Server and General Configuration Schemas

## Authority and version

T27 production descriptors target the NUT 2.8.5 release documentation bundled by the historic upstream site:

- [`nut.conf(5)` 2.8.5](https://networkupstools.org/historic/v2.8.5/docs/man/nut.conf.html)
- [`upsd.conf(5)` 2.8.5](https://networkupstools.org/historic/v2.8.5/docs/man/upsd.conf.html)
- [`upsd(8)` 2.8.5](https://networkupstools.org/historic/v2.8.5/docs/man/upsd.html)

Development documentation is not used to broaden the schema. Runtime never downloads a schema.

## `nut.conf`

`MODE` is required and supports `none`, `standalone`, `netserver`, and `netclient`. Missing remains a semantic error until the administrator chooses a value; NutManager does not write the documented default automatically. New assignments use `KEY=value` with no whitespace around `=`.

Advanced descriptors are `ALLOW_NO_DEVICE`, `ALLOW_NOT_ALL_LISTENERS`, `UPSD_OPTIONS`, `UPSMON_OPTIONS`, `POWEROFF_WAIT`, `POWEROFF_QUIET`, `NUT_DEBUG_LEVEL`, `NUT_DEBUG_PID`, `NUT_DEBUG_PROCNAME`, `NUT_DEBUG_SYSLOG`, `NUT_IGNORE_CHECKPROCNAME`, and `NUT_QUIET_INIT_UPSNOTIFY`. Several are package/service-integration dependent. `NUT_DEBUG_SYSLOG` remains technical text because the release documents `stderr`, `none`, `default`, and other/unset behavior; the editor does not falsely reject preserved package values. NutManager edits and reviews their text but does not execute component options, perform shutdown, or claim that a Windows package consumes every setting.

## `upsd.conf`

`LISTEN` is repeated and accepts an address/hostname plus an optional separate port token. Syntax supports IPv4, IPv6, hostnames, and the documented `*` wildcard. The optional port is 1–65535. Validation performs no DNS lookup, socket bind, interface enumeration, or default-listener insertion. Its activation metadata requires a separate restart.

Server descriptors are `MAXAGE`, `TRACKINGDELAY`, `ALLOW_NO_DEVICE`, `ALLOW_NOT_ALL_LISTENERS`, `STATEPATH`, `MAXCONN`, `CERTFILE`, `CERTPATH`, `CERTIDENT`, `CERTREQUEST`, `DISABLE_WEAK_SSL`, and `DEBUG_MIN`. Defaults from the manpage are presentation help only and remain omitted until explicitly chosen.

TLS applicability depends on the compiled backend: `CERTFILE` is documented for OpenSSL; `CERTPATH`, `CERTIDENT`, and `CERTREQUEST` for NSS. NutManager does not probe or assume that backend. `CERTIDENT` is change-only: projection exposes only configured state, replacement accepts a transient new password, review/preview remain redacted, and removal is explicit.

## Persistence and activation

Both forms materialize the T25 draft into a T13 document and call the existing `INutConfigurationFilePipeline`. Local writes remain T14; remote writes remain T19 SFTP or T19B SMB. Review activation metadata is informational. Apply saves only and never starts, reloads, or restarts a service.
