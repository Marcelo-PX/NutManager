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
- The .NET 10 runtime, unless you publish self-contained.
- Administrative rights on the server for the installation steps below. The agent itself never
  performs any of them.

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

The published payload is about 8 MB and currently includes libraries the agent never calls — the
SSH and WMI stacks arrive because the agent references `NutManager.Infrastructure` as a whole. They
are carried, not used: no code path in the agent reaches them. Narrowing the payload is a packaging
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

The profile stores the agent transport. New and existing profiles use the named pipe, which is the
only transport this build implements. A profile that names HTTPS is refused by name — the application
will not quietly use the named pipe instead — until the HTTPS listener exists.

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
