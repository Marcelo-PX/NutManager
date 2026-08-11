# NutManager UI Design System

## Status and purpose

The shared Windows-first presentation foundation is implemented. It modernizes the Avalonia shell without changing NUT, safe-write, remote-transport, credential, driver, or privilege boundaries. T24A now applies that foundation to managed-server profiles; page decomposition in T24B and semantic configuration work in T25+ remain future work.

## Implemented shared presentation layer

`NutManager.App/Presentation` owns the reusable App-only presentation resources:

```text
Presentation
├── Themes
│   ├── NutColors.axaml
│   ├── NutMetrics.axaml
│   ├── NutMotion.axaml
│   ├── NutTypography.axaml
│   ├── NutControlStyles.axaml
│   ├── NutShellStyles.axaml
│   └── NutIcons.axaml
└── Controls
    ├── NutConnectionIndicator
    ├── NutStatusBadge
    └── NutReviewDrawerHost
```

`App.axaml` composes these dictionaries and retains the page data templates. Theme resources, component styles, and icon geometries are not duplicated in the window or page views. The shared controls contain presentation only: they do not poll, execute administrative operations, inspect files, or write configuration.

## Approved design references

`00_overview_reference.png` and `00_ups_conf_reference.png` are the primary fidelity targets at 1536×1024. They define shell proportions, surface hierarchy, spacing, typography, icon scale, selection treatment, semantic colors, and the future review-drawer proportions. `01_configuracoes.png` through `09_sobre.png` are secondary storyboards for information architecture and reusable component patterns; they are not evidence that unsupported commands or backends exist.

Phase A validates the shared shell against those primary references. T24B owns page-level fidelity for Overview, Devices, Diagnostics, Administration, Settings, and any approved About surface. T25+ owns the graphical configuration and populated review-drawer fidelity.

## Shell and responsive states

The shell has three presentation states:

| State | Width | Navigation and review |
| --- | --- | --- |
| Wide | >= 1200 px | Expanded sidebar and an optional 360–420 px review drawer share space with forms. |
| Medium | 860–1199 px | One or two form columns; sidebar may collapse and review may overlay. |
| Compact | < 860 px | Overlay navigation, single-column forms, overlay review, and no ordinary horizontal scrolling. |

The left sidebar has Expanded (currently 220 px), Collapsed (72 px), and Overlay states. In Wide, the chevron, header button, or `Ctrl+B` changes the persisted preference. Medium deliberately projects Collapsed and does not mutate a preference that would have no immediate visual effect. Compact projects navigation as an overlay opened by the header button or `Ctrl+B`; closing it or returning to a wider layout does not overwrite the persisted Expanded/Collapsed preference. The selected item uses a subtle product-owned surface, a 3 px accent bar, and accent foreground—never literal selected text. Collapsed items keep tooltips and accessible names. Sidebar preference is non-secret UI preference data.

The review presentation mapper defines Hidden, Collapsed, Expanded, and Overlay states, and `NutReviewDrawerHost` provides the shared 368 px content host. The current shell keeps it Hidden because no generic review-context adapter is connected yet. Semantic old-to-new changes, redaction, validation, generated preview, backup/recovery explanation, and Apply remain T25+ responsibilities and must use the existing safe-write pipeline when implemented.

## Header, theme, and visual tokens

The header shows the active runtime profile/UPS endpoint and a 12 px connection core with a 24 px soft halo through `NutConnectionIndicator`. It observes the existing Overview/polling state; it does not create another client, timer, or polling loop. Status is always accompanied by localized visible detail text rather than color alone:

- green: Connected and Fresh;
- yellow/orange: Connecting, Reconnecting, Stale, or pending;
- red: Disconnected, failure, or critical condition;
- gray: no active profile or unavailable context.

The halo is currently static. A future restrained transition may be added, but aggressive flashing is prohibited. Blue/cyan is the normal application accent. Green is reserved for healthy/success, while yellow/orange and red retain warning and error meaning. Color never carries the only meaning. Mock mode is displayed persistently through the warning-toned `NutStatusBadge`.

The header uses a compact PathIcon sun/moon toggle. System theme remains available in **Settings → Appearance & Language**; clicking the header control from System makes the next Light/Dark preference explicit from the effective theme.

The resource dictionaries define spacing 4/8/12/16/20/24/32; radii 6/8/12/16; a 38 px standard control height; shell/page/card measurements; and 140/180/220 ms motion tokens. Typography uses Segoe UI Variable with Segoe UI and Arial fallbacks: product title 21, page title 27, section title 18, body 14, and metadata 12. Reusable PathIcon geometries replace text glyphs in shell navigation and theme controls.

`NutAccentBrush`, `NutAccentBrightBrush`, and `NutSelectionBrush` are product-owned tokens. `NutColors.axaml` supplies intentional Light and Dark surface/text palettes plus invariant accent, cyan, healthy, warning, critical, purple, focus, and unavailable semantics. Shell navigation and selected `ListBoxItem` presentation use these resources rather than the Windows accent, so red is never normal selection. Compatibility aliases keep existing page surfaces on the same themed palette while T24B remains pending. Option controls introduced by later tasks use localized presentation objects, not raw `Enum.ToString()` values.

The shell follows the one-scroll-owner rule: `MainWindow` contains no page-level `ScrollViewer`; its content host gives the selected page the available space, and each page remains responsible for its own vertical scrolling. Medium and Compact modes reduce shell content padding; Medium projects collapsed navigation and Compact uses overlay navigation rather than horizontal scrolling. T24B remains responsible for replacing rigid internal page layouts where needed.

## Managed-server Settings surface

T24A uses the shared card, typography, spacing, border, product-selection, healthy, warning, and critical resources rather than introducing page-local colors. Managed servers appear as useful cards with endpoint, localized Local/Remote and ReadOnly/Manage summaries, transport, and active status. The editor uses a wide list/editor split and projects to a single column below its compact threshold, retaining one vertical scroll owner and no ordinary horizontal scroll. Inline validation and connection-test results always include text; the dirty-draft decision is keyboard-operable and does not rely on color.

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

Icon-only shell controls have `AutomationProperties.Name`, tooltips, and the shared focus-visible border. Connection state includes text as well as color. Opening Compact navigation transfers focus to its localized close button, cycles keyboard navigation inside the overlay, and disables the shell controls behind the scrim. The overlay can be closed without changing the saved navigation preference, and `Ctrl+B` remains available in applicable states. Critical warnings must always include explicit text. The product displays **SFTP**; internal contracts may retain `SshSftp`.

Mock/demo state is an unambiguous persistent badge, never merely an incidental checkbox value.

All layouts introduced by T24A–T29 must be validated in both official cultures as those tasks are implemented. See [Localization](LOCALIZATION.md) and [Graphical NUT configuration](GRAPHICAL-NUT-CONFIGURATION.md).
