# NutManager UI Design System

## Status and purpose

This is the implementation target for T24–T29, not a description of the current shell. It modernizes the Windows-first Avalonia presentation without changing NUT, safe-write, remote-transport, or privilege boundaries.

## Shell and responsive states

The shell has three presentation states:

| State | Width | Navigation and review |
| --- | --- | --- |
| Wide | >= 1200 px | Expanded sidebar and an optional 360–420 px review drawer share space with forms. |
| Medium | 860–1199 px | One or two form columns; sidebar may collapse and review may overlay. |
| Compact | < 860 px | Overlay navigation, single-column forms, overlay review, and no ordinary horizontal scrolling. |

The left sidebar has Expanded (200–220 px), Collapsed (64–72 px), and Overlay states. A chevron on its divider changes the state; `Ctrl+B` is the planned shortcut. The selected item uses a subtle surface, a 2–3 px accent bar, and an accent icon—never literal selected text. Collapsed items keep tooltips and accessible labels. Sidebar preference is non-secret UI preference data.

The right review drawer has Hidden, Collapsed, Expanded, and Overlay states. It is hidden with no draft; otherwise its collapsed tab states the pending-change count. Collapsing restores form space. The drawer contains semantic old-to-new changes, additions/removals, redacted sensitive values, validation, generated-file preview when requested, backup/recovery explanation, and explicit Apply.

## Header, theme, and visual tokens

The header shows an active-profile/UPS endpoint and a 10–12 px connection core with an 18–24 px soft halo. Status is always accompanied by text or a tooltip:

- green: Connected and Fresh;
- yellow/orange: Connecting, Reconnecting, Stale, or pending;
- red: Disconnected, failure, or critical condition;
- gray: no active profile or unavailable context.

The halo may pulse slowly; no aggressive flashing is allowed. Blue/cyan is the normal application accent. Green is reserved for healthy/success, while yellow/orange and red retain warning and error meaning. Color never carries the only meaning.

T24 replaces the header theme ComboBox with a compact sun/moon toggle (roughly 180–240 ms transition). System theme remains available in **Settings → Appearance & Language**.

Reusable App resources will define spacing 4/8/12/16/20/24/32, radii 6/8/12, surface/border/accent/status brushes, standard control height, and motion durations. Typography prefers Segoe UI Variable on Windows with a safe system fallback: page title 26–28, section title 18–20, body 14–15, metadata 12–13. Hover motion is 120–160 ms; sidebar/drawer 200–260 ms; page transition 160–220 ms.

## Administration information architecture

```text
Administration
├── NUT Configuration
│   ├── General (nut.conf)
│   ├── UPS (ups.conf)
│   ├── Server (upsd.conf)
│   ├── Users (upsd.users)
│   └── Monitoring (upsmon.conf)
├── Windows Service
├── Devices and Drivers
└── Remote Access
```

Graphical forms are the primary configuration experience. Generated configuration is read-only Preview, Generated configuration, or Advanced inspection—not an embedded Notepad.

## Accessibility and terminology

Icon-only controls require `AutomationProperties.Name` and a tooltip. Focus is visible, tab order remains logical after a responsive transition, and critical warnings always include explicit text. The product displays **SFTP**; internal contracts may retain `SshSftp`.

All T24–T29 layouts are validated in both official cultures. See [Localization](LOCALIZATION.md) and [Graphical NUT configuration](GRAPHICAL-NUT-CONFIGURATION.md).
