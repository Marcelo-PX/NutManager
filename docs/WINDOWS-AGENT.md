# NutManager Windows agent

The agent is a Windows service that runs on the machine hosting NUT and controls that machine's NUT
service on behalf of authorized NutManager operators.

It exists because of what T34 established: a remote SCM call is authenticated across machines, and a
client that is not recognised by the server is refused before it can ask anything. The agent moves
the SCM call to the machine that owns the service, so the cross-machine authentication that failed
there is no longer on the path to controlling a service.

## What it does

- reports the state, process id and identity of the NUT service on its own machine;
- starts, stops and restarts that one service;
- records every control operation in the Windows Event Log.

## What it does not do

- it does not read or write NUT configuration files;
- it does not accept a service name, a path or a command from a caller;
- it does not restart the NUT service after a configuration change;
- it does not create its own operators group or its own Event Log source;
- it does not terminate processes.

The service it controls is the one it validated for itself at startup: a Windows service whose binary
lives inside the detected NUT installation. If two services qualify, or none does, the agent reports
that it has no authority and refuses every control operation.

## Requirements

- Windows x64, with NUT installed and registered as a Windows service.
- The **ASP.NET Core Runtime 10** on the server, unless you publish self-contained. This is stricter
  than it used to be: the agent's optional HTTPS transport is hosted on ASP.NET Core's HTTP.sys
  server, so the published `runtimeconfig.json` requires both `Microsoft.NETCore.App` and
  `Microsoft.AspNetCore.App`. Installing the ASP.NET Core runtime brings the .NET runtime with it, so
  it is one download rather than two — but the plain .NET runtime alone is no longer enough, even
  when HTTPS stays disabled.
- Administrative rights on the server for the installation steps below. The agent itself never
  performs any of them.

Check what is present:

```powershell
dotnet --list-runtimes
```

Both `Microsoft.NETCore.App 10.x` and `Microsoft.AspNetCore.App 10.x` must appear.

## Authentication, and the one thing to check first

The agent's default transport is a named pipe. A named pipe reached from another machine is carried
by SMB and authenticated by Windows, which means the client needs an identity the server recognises —
the same requirement that stopped T34's remote SCM query.

If NutManager runs on a machine that is not joined to the server's domain, and under a local account,
the server has no identity to recognise and the connection is refused. The client reports that as
**access denied**, with the numeric Windows code, and never as a NUT outage.

Two ways to satisfy it:

- run NutManager under an account the server recognises (a domain account, or an account that exists
  on the server with the same name and password); or
- establish a session to the server first, with credentials it recognises. This is an administrative
  step performed by an operator at a prompt, and it is not something NutManager does: the product
  never launches `net`, `sc`, `cmd` or PowerShell on anyone's behalf.

```bash
net use \\GANDALF /user:SBRA\operator
```

The agent does not accept a password over the wire, and it does not fall back to any weaker
authentication if Windows refuses the caller.

## Publishing

From the repository root:

```bash
dotnet publish src/NutManager.Agent/NutManager.Agent.csproj --configuration Release --runtime win-x64 --self-contained false --output publish/agent
```

`publish/` is not versioned.

Copy the contents of `publish/agent` to the server, for example to
`C:\Program Files\NutManager Agent`.

The published payload is about 7 MB and still includes libraries the agent never calls — the SSH and
WMI stacks arrive because the agent references `NutManager.Infrastructure` as a whole. They are
carried, not used: no code path in the agent reaches them. Narrowing the payload is a packaging
change worth making, and it is recorded as a known limitation rather than solved by moving
platform-specific code into `NutManager.Core`, which is not allowed to hold it.

## Installation

Run every command in this section from an **elevated** prompt on the server.

### 1. Create the operators group

Membership of this local group is the only thing that authorizes control. If the group does not
exist, the agent refuses to start — it never falls back to Administrators.

```bash
net localgroup "NutManager Operators" /add /comment:"May control the NUT service through the NutManager agent."
```

Add the accounts that may control the service. A domain group may be added instead of individual
accounts; membership held through it is recognised.

```bash
net localgroup "NutManager Operators" "SBRA\operator" /add
```

### 2. Register the Event Log source

Control is refused whenever the audit sink is unusable, so this step is not optional. It is performed
by an administrator once, and never by the agent.

```powershell
New-EventLog -LogName Application -Source "NutManager Agent"
```

### 3. Create the service

The agent must run as LocalSystem. It verifies this at startup and refuses to run as any other
account.

```bash
sc.exe create NutManagerAgent binPath= "\"C:\Program Files\NutManager Agent\NutManager.Agent.exe\"" obj= LocalSystem start= auto DisplayName= "NutManager Agent"
```

```bash
sc.exe description NutManagerAgent "Controls the local Network UPS Tools service for authorized NutManager operators."
```

The spaces after `binPath=`, `obj=`, `start=` and `DisplayName=` are required by `sc.exe`.

### 4. Start it

```bash
sc.exe start NutManagerAgent
```

## Verifying the installation

```bash
sc.exe query NutManagerAgent
```

The service should report `RUNNING`. Then, from NutManager on the client machine, the agent should
answer a handshake and report the NUT service's state and process id.

If the service starts and immediately stops, the reason is in the Application event log under the
source `NutManager Agent`. The startup checks that can stop it are, in order: the account is not
LocalSystem, and the operators group could not be resolved.

## Using it from NutManager

The agent panel lives on the Administration page of a **remote** profile. It reports four things that
are deliberately kept apart:

- **Agent** — whether the agent answered: connected, unavailable, access denied, host unreachable, no
  answer, incompatible, failed.
- **Transport** — named pipe or HTTPS, as the profile selects.
- **Service** — the NUT service's identity, state, process and pid, as the agent reports them.
- NUT's own protocol health, which is shown elsewhere in the shell and is never touched by any of the
  above.

An agent that cannot be reached on a server whose upsd is answering normally is an administrative
gap. NutManager says so, and does not fall back to any other route: there is no second path to the
service control manager behind the agent.

Start, Stop and Restart appear only when the agent advertises them. If the operators group or the
event source is missing, the agent reports control as unavailable with the reason, and no button is
offered. Stop and Restart ask for confirmation first, naming the host and the service; Restart is a
single request to the agent, which holds both phases under one lock.

### Transport selection

The profile stores the agent transport, and the named pipe is the default for new and existing
profiles. HTTPS is selected per profile and requires the server-side setup below.

The profile editor does not yet have controls for these options — that is planned work (T36), not a
missing capability. Until then the transport, endpoint, authentication mode and account name are set
in the profile document itself; the application reads, validates and migrates them exactly as it will
once the editor exists.

There is no fallback in either direction. A profile that selects HTTPS never quietly uses the named
pipe when the endpoint is wrong, and a profile on the named pipe never tries HTTPS: an operator who
cannot tell which transport answered cannot diagnose either.

## The optional HTTPS transport

HTTPS exists for the case the named pipe cannot serve. A pipe reached from another machine rides SMB
and needs a Windows session the client may not have; Negotiate over HTTPS can be given an explicit
credential instead, so a client outside the server's domain can authenticate without anyone
establishing a session first.

It is **disabled by default**. Installing the agent opens no TCP port. Everything below is a
deliberate act by an administrator.

### 1. The agent configuration file

Create `%ProgramData%\NutManager\Agent\agent.json` on the server:

```json
{
  "httpsEnabled": true,
  "httpsPrefix": "https://gandalf.sbra.local:5199/",
  "certificateThumbprint": "A909502DD82AE41433E6F83886B00D4277A32A7B"
}
```

The file holds no secret and cannot: there is no password, no PFX and no private key in it. The
certificate is named by thumbprint and lives in the Windows certificate store, where the private key
is protected by the operating system.

Restrict the file so that only `SYSTEM` and `Administrators` can modify it — it decides where a
privileged agent listens:

```powershell
icacls "C:\ProgramData\NutManager\Agent\agent.json" /inheritance:r /grant "SYSTEM:(R)" /grant "Administrators:(F)"
```

The prefix must use `https`, must end with a forward slash, and must name an explicit host. A
wildcard (`https://*:5199/`) is refused: on a privileged agent it would accept requests aimed at any
name that resolves to the machine. Any of these mistakes stops the HTTPS listener from starting; the
named pipe keeps working, and the reason is written to the Application event log.

### 2. The certificate

The certificate must be in `LocalMachine\My`, must have a private key on that machine, and its
subject or SAN must match the host name clients will use. The agent verifies the first two at
startup and refuses to listen otherwise. It never creates, installs or trusts a certificate.

```powershell
Get-ChildItem Cert:\LocalMachine\My | Select-Object Thumbprint, Subject, HasPrivateKey, NotAfter
```

### 3. Bind the certificate to the port

HTTP.sys owns the TLS binding, and it is a deployment step. The agent never runs `netsh`.

```powershell
netsh http add sslcert ipport=0.0.0.0:5199 certhash=A909502DD82AE41433E6F83886B00D4277A32A7B appid="{00000000-0000-0000-0000-000000000000}"
```

Use a stable GUID of your own for `appid`. Verify with:

```powershell
netsh http show sslcert ipport=0.0.0.0:5199
```

### 4. Reserve the URL

The agent runs as LocalSystem, which can normally bind without a reservation. If your policy
requires one:

```powershell
netsh http add urlacl url=https://gandalf.sbra.local:5199/ user="NT AUTHORITY\SYSTEM"
```

### 5. Firewall

Opening the port is a deliberate administrative act, and the agent never touches firewall rules:

```powershell
New-NetFirewallRule -DisplayName "NutManager Agent HTTPS" -Direction Inbound -Protocol TCP -LocalPort 5199 -Action Allow
```

### 6. Restart the agent

```powershell
Restart-Service NutManagerAgent
```

### Authentication over HTTPS

The transport is hosted by ASP.NET Core on the HTTP.sys server, configured through `UseHttpSys`. TLS
stays with HTTP.sys and the certificate an administrator bound to the port: nothing inside the agent
loads a certificate or terminates TLS.

The listener requires **Negotiate** and does not accept anonymous requests — HTTP.sys authenticates
before the request reaches the agent, so an unauthenticated caller never gets as far as the code that
could refuse it. Membership of `NutManager Operators` is then required, exactly as on the named pipe.
There is no bearer token, no Basic authentication and no password anywhere in the agent protocol.

In NutManager, an HTTPS profile chooses between:

- **Current Windows identity** — the account NutManager runs as. Nothing is stored.
- **Alternate Windows account** — a different account, supplied to Negotiate as an explicit
  credential. Its password is kept in the Windows Credential Manager under the agent's own target,
  separate from the SMB and SSH secrets: those authorize reading configuration files, this authorizes
  controlling a service, and one stored secret must not silently grant both.

### Troubleshooting HTTPS

**The service starts but HTTPS does not.** Application event log, source `NutManager Agent`, event
1001. It names which precondition failed: a missing or plain-text prefix, a wildcard host, a
thumbprint that is not hexadecimal, a certificate that is absent or has no private key, or a bind
that failed because no SSL certificate is attached to the port.

**NutManager reports an incompatible agent after enabling HTTPS.** That state also covers TLS
failures — an untrusted certificate or one whose name does not match the host. Certificate validation
is the platform default and is never bypassed, so fix the certificate rather than the client.

**NutManager reports access denied over HTTPS.** Negotiate failed, or the account is not a member of
`NutManager Operators` on the server. A 401 and a 403 both arrive here.

**Rolling HTTPS back.** Set `httpsEnabled` to `false` (or delete the file) and restart the service.
The named pipe is unaffected. To remove the binding as well:

```powershell
netsh http delete sslcert ipport=0.0.0.0:5199
```

## Event log

All entries are written to the Application log under the source `NutManager Agent`. The event ids are
part of the agent's contract and are stable:

| Id | Meaning |
|----|---------|
| 1001 | A security precondition failed at startup |
| 1002 | A caller was refused for not belonging to the operators group |
| 1003 | The NUT service stopped matching what was validated at startup |
| 1010 | A control operation was requested |
| 1011 | A control operation succeeded |
| 1012 | A control operation failed |

Each entry records the caller, the transport, the service, the state before and after, the result and
the operation id. No entry can contain a credential: the audit record has no field one could travel
in.

## Upgrading

```bash
sc.exe stop NutManagerAgent
```

Replace the files, then:

```bash
sc.exe start NutManagerAgent
```

The operators group and the Event Log source survive an upgrade and do not need to be recreated.

## Uninstalling

```bash
sc.exe stop NutManagerAgent
```

```bash
sc.exe delete NutManagerAgent
```

Remove the files. The group and the event source are left alone unless you remove them deliberately,
because both may predate this installation:

```powershell
Remove-EventLog -Source "NutManager Agent"
```

```bash
net localgroup "NutManager Operators" /delete
```

## Troubleshooting

**The service will not start.** Read the Application log for source `NutManager Agent`, event 1001.
The two causes it reports are an account that is not LocalSystem and an operators group that could
not be resolved.

**NutManager reports that no agent is available.** Nothing accepted a connection: the service is not
running on that host, or it is not installed there. This says nothing about NUT — a server whose upsd
is answering normally can be a server with no agent on it.

**NutManager reports access denied.** Windows refused the caller. Either the account is not a member
of `NutManager Operators` on the server, or the server does not recognise the client's identity at
all — see the authentication section above.

**The agent reports that it has no NUT service.** No Windows service on the server runs a binary
inside the detected NUT installation, or more than one does. The agent refuses to guess: a wrong
guess here would attach service control rights to the wrong service.

**Control is unavailable although the agent is running.** The handshake reports why. The causes are a
missing operators group, an unusable Event Log source, and an unresolved NUT service.
