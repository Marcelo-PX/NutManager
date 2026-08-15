# NutManager UI Design System

## Status and purpose

The shared Windows-first presentation foundation is implemented. It modernizes the Avalonia shell without changing NUT, safe-write, remote-transport, credential, driver, or privilege boundaries. T24A applies that foundation to managed-server profiles and T24B applies it to current operational pages; semantic configuration work remains T25+.

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

T24 established the shared shell against those primary references. T24B supplies responsive current-page composition for Overview, Devices, Diagnostics, and Administration without inventing unsupported health, history, test, or service capabilities. T25–T28 now populate the graphical configuration and review-drawer foundation; final cross-surface hardening remains T29.

## Shell and responsive states

The shell has three presentation states:

| State | Width | Navigation and review |
| --- | --- | --- |
| Wide | >= 1200 px | Expanded sidebar and an optional 360–420 px review drawer share space with forms. |
| Medium | 860–1199 px | One or two form columns; sidebar may collapse and review may overlay. |
| Compact | < 860 px | Overlay navigation, single-column forms, overlay review, and no ordinary horizontal scrolling. |

The left sidebar has Expanded (currently 220 px), Collapsed (72 px), and Overlay states. In Wide, the chevron, header button, or `Ctrl+B` changes the persisted preference. Medium deliberately projects Collapsed and does not mutate a preference that would have no immediate visual effect. Compact projects navigation as an overlay opened by the header button or `Ctrl+B`; closing it or returning to a wider layout does not overwrite the persisted Expanded/Collapsed preference. The selected item uses a subtle product-owned surface, a 3 px accent bar, and accent foreground—never literal selected text. Collapsed items keep tooltips and accessible names. Sidebar preference is non-secret UI preference data.

The review presentation mapper defines Hidden, Collapsed, Expanded, and Overlay states, and `NutReviewDrawerHost` provides the shared 368 px content host. T25 connects an optional generic semantic-review presentation: deterministic changes, localized validation issues, custom parameters, activation information, and redacted generated-preview lines. With no semantic draft it remains Hidden. The presentation is read-only and has no Apply command; the T26–T28 forms provide draft actions while persistence continues through the existing safe-write pipeline.

## Header, theme, and visual tokens

The header shows the active runtime profile/UPS endpoint and a 12 px connection core with a 24 px soft halo through `NutConnectionIndicator`. It observes the existing Overview/polling state; it does not create another client, timer, or polling loop. Status is always accompanied by localized visible detail text rather than color alone:

- green: Connected and Fresh;
- yellow/orange: Connecting, Reconnecting, Stale, or pending;
- red: Disconnected, failure, or critical condition;
- gray: no active profile or unavailable context.

The Composition-driven halo breathes without a UI-thread timer: Healthy uses the approved 2.0-second pulse, Pending and Critical share the same 3.2-second curve in their respective amber/red semantic colours, and Unavailable is static. State transitions and visual-tree detach explicitly stop the old Composition animations. Aggressive flashing is prohibited. Blue/cyan is the normal application accent. Green is reserved for healthy/success, while yellow/orange and red retain warning and error meaning. Color never carries the only meaning. Mock mode is displayed persistently through the warning-toned `NutStatusBadge`.

The header uses a compact PathIcon sun/moon toggle. System theme remains available in **Settings → Appearance & Language**; clicking the header control from System makes the next Light/Dark preference explicit from the effective theme.

The resource dictionaries define spacing 4/8/12/16/20/24/32; radii 6/8/12/16; a 38 px standard control height; shell/page/card measurements; and 140/180/220 ms motion tokens. Typography uses Segoe UI Variable with Segoe UI and Arial fallbacks: product title 21, page title 27, section title 18, body 14, and metadata 12. Reusable PathIcon geometries replace text glyphs in shell navigation and theme controls.

`NutAccentBrush`, `NutAccentBrightBrush`, and `NutSelectionBrush` are product-owned tokens. `NutColors.axaml` supplies intentional Light and Dark surface/text palettes plus invariant accent, cyan, healthy, warning, critical, purple, focus, and unavailable semantics. Shell navigation, Administration selectors, and selected `ListBoxItem` presentation use these resources rather than the Windows accent, so red is never normal selection. Localized presentation properties replace raw enum text on touched summaries. Option controls introduced by later tasks continue to use localized presentation objects, not `Enum.ToString()` values.

The shell follows the one-scroll-owner rule: `MainWindow` contains no page-level `ScrollViewer`; its content host gives the selected page the available space, and each page owns one vertical scroll surface. Medium and Compact modes reduce shell content padding; Medium projects collapsed navigation and Compact uses overlay navigation. T24B replaces rigid master/detail grids with responsive projection and wrap-based cards without ordinary horizontal scrolling.

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

T24B preserves the existing-entry fallback and reviewed T14 preview inside NUT Configuration. T25 supplies the generic semantic draft/review/generated-preview foundation without adding a writer. T26 uses that foundation for graphical `ups.conf`. T27 adds dedicated General (`nut.conf`) and Server (`upsd.conf`) surfaces with Basic/Advanced/Custom groups, wrapping LISTEN/TLS/custom rows, textual accessible actions, and the same page-level scroll owner. T28 completes the supported set with dedicated Users (`upsd.users`) and Monitoring (`upsmon.conf`) forms, including change-only password presentation and repeated monitor/notification rows.

## Approved visual fidelity (T27A)

T27A aligns the rendered application with the approved visual references without changing domain, transport, write, privilege, or hardware safety boundaries. Its functional hardening is limited to presentation/runtime defects found during visual validation, including latest-selection-wins configuration navigation and passive Windows metadata discovery.

`Presentation/Themes` is the single source for the visual language. `NutColors.axaml` defines an explicit surface hierarchy — window, shell, surface, elevated, interactive, selected — plus border, text, accent and semantic families in both themes, so cards no longer carry the same visual weight and navigation selection is a restrained accent bar and low-contrast surface instead of a saturated block. `NutTypography.axaml` separates page title, section title, card title, label, metadata and the dominant metric readout. `NutMetrics.axaml` owns spacing, radii, icon sizes and shell dimensions. `NutControlStyles.axaml` and `NutShellStyles.axaml` restyle cards, buttons, inputs, lists, tabs, badges, the title bar, navigation and the profile card so surfaces stop reading as default Fluent controls.

`NutIcons.axaml` is the only icon source. Glyphs are `StreamGeometry` on a 24×24 grid using the even-odd rule for outlined shapes, covering navigation, configuration domains, metrics, connectivity, security, service control, actions, chevrons, theme and window chrome. Emoji, pictographic text and raster images are not used as icons, and no icon package is referenced. Semantic icon colour is always redundant with text.

The window uses `WindowDecorations="BorderOnly"` so product identity, connection state, the theme control and the window buttons share one integrated bar instead of a separate Windows title strip. Drag, double-click maximise, minimise, restore and close remain standard Avalonia window operations with no platform interop.

Motion is defined in `NutMotion.axaml` and stays within roughly 140–320 ms for interaction feedback: navigation selection, hover, card and input state, drawer content, tab underline, theme selection, load-gauge sweep and battery value transitions. The semantic status halo is the only looping animation, is purely decorative, and never carries state on its own. No animation timer, background worker or polling loop is introduced for decoration.

Overview is composed as a UPS dashboard: battery with animated charge bar, semicircular load gauge built from the native `Arc` shape, runtime with its raw NUT reading, input and output, UPS state with its status tokens, and connection. Every reading is projected from the current snapshot; a missing NUT variable keeps its card composition and shows the unavailable label rather than a substituted value, and this is pinned by tests.

## Accessibility and terminology

Icon-only shell controls have `AutomationProperties.Name`, tooltips, and the shared focus-visible border. Connection state includes text as well as color. Opening Compact navigation transfers focus to its localized close button, cycles keyboard navigation inside the overlay, and disables the shell controls behind the scrim. The overlay can be closed without changing the saved navigation preference, and `Ctrl+B` remains available in applicable states. Critical warnings must always include explicit text. The product displays **SFTP**; internal contracts may retain `SshSftp`.

Mock/demo state is an unambiguous persistent badge, never merely an incidental checkbox value.

All layouts introduced by T24A–T29 must be validated in both official cultures as those tasks are implemented. See [Localization](LOCALIZATION.md) and [Graphical NUT configuration](GRAPHICAL-NUT-CONFIGURATION.md).

## Configuration file rail (T31)

The NUT configuration page carries its own collapsible rail for the file list. It is built from the
same pieces as the shell navigation item — accent bar for selection, `NutSelectedSheenBrush` for the
selected surface, hover lift — so the two rails read as one idea at two scales rather than as two
components that happen to sit near each other.

`NutFileRailExpandedWidth` (228 px) matches the shell sidebar; `NutFileRailCollapsedWidth` (64 px) is
tighter because this rail sits inside a page and only has to hold an 18 px icon. The width animates
over `NutMotionShell` with `CubicEaseOut`, and the labels fade with it: a rail whose text vanished
instantly read as content being dropped rather than folded away.

The surface is `NutGlassSurfaceBrush`, a translucent tint over the page rather than a second opaque
card, so the rail reads as glass above the content. The alpha is deliberately high — at lower opacity
the file names lost contrast against whatever scrolled underneath, and legibility outranks the
effect.

Each file keeps its own icon, so a collapsed rail is still readable: `NutIconGeneral`, `NutIconUps`,
`NutIconServer`, `NutIconUsers`, `NutIconMonitoring`. Collapsed, the row is only that icon, so its
accessible name and tooltip carry both the purpose and the real file name. Selection is never colour
alone: the accent bar and a semibold label carry it too. The selected row's icon pops once when it
becomes current; nothing in the rail loops.

## Glass surfaces and the two-tone window (T31)

The window is transparent with an `ExperimentalAcrylicBorder` behind the entire shell. This is not
decoration for its own sake: Avalonia cannot blur in-page content, so before the pane existed the
translucent cards were tinting a flat colour and the effect was invisible. The transparency hint
degrades from acrylic to Mica to plain blur, and `NutWindowBrush` sits on the same value as the
tint, so where a compositor offers none of them the fallback keeps the same separation instead of
collapsing the layers together.

Transparency only reads when the layers differ, which is why the palette is deliberately two-tone:

| Token | Role | Dark | Light |
| --- | --- | --- | --- |
| `NutAcrylicTintColor` | the pane behind everything | `#05080E` | `#DDE4EF` |
| `NutGlassSurfaceBrush` | cards, rail, panels | `#8C3A4A66` | `#A6FFFFFF` |
| `NutGlassBorderBrush` | the pane's edge | `#40FFFFFF` | `#73FFFFFF` |
| `NutGlassSheenBrush` | top-edge highlight | — | — |

With the backdrop and the surfaces on the same navy, a 70% panel still looked opaque; the backdrop
is now the darkest value in the window and the surfaces lift well clear of it.

The language follows Apple's glass rather than a tinted panel: frosted and cool instead of tinted
navy, a thin white hairline instead of a coloured border — on those panes the rim is light catching
an edge, not a drawn outline — and larger continuous radii, which is half of what makes a surface
read as glass at all. Badge fills and the navigation selection sheen carry alpha for the same
reason, so they read as tinted glass over the pane rather than as painted chips.

Foreground colours are untouched throughout. The alpha on every surface stops where body text would
start losing contrast: the effect is never allowed to cost legibility.

The acrylic pane breathes over sixteen seconds with a narrow swing. It and the connection light are
the only two continuous animations in the application, and neither is a control style — a looping
style would apply to every instance of a control, which remains forbidden and is what the
interaction tests defend.
