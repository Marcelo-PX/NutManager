# NutManager Localization Architecture

## Official cultures

T24 establishes the official UI cultures:

- `pt-BR` — Português (Brasil), the initial/default culture;
- `en-US` — English (United States).

Settings gains **Appearance & Language** with System, Light, Dark, Português (Brasil), and English (United States). The language preference is non-secret per-user UI data. Runtime switching is preferred; a clearly communicated restart may be the initial implementation only when deterministic runtime resource replacement is not safe. The design must not prevent future runtime switching.

## Resource boundary

All new user-facing T24–T29 strings use stable semantic keys rather than hard-coded text. Examples: `Nav.Overview`, `Status.Connected`, `Config.Ups.Driver`, `Config.Ups.Port`, `Config.Ups.Protocol`, `Config.Ups.RuntimeCalibration`, `Config.Review.Apply`, and `Config.Review.Discard`. Keys are not Portuguese text.

Validation follows the same convention: `Validation.Host.Required`, `Validation.Host.Invalid`, `Validation.Host.NoScheme`, `Validation.Port.Range`, `Validation.Profile.NameDuplicate`, `Validation.Remote.ManagementHostRequired`, and `Validation.Smb.ShareRootRequired`. Option labels and contextual help are localized resource values, never final enum names.

Localize navigation, headings, labels, buttons, menus, tooltips, contextual help, validation, warnings, errors, semantic-review content, and accessibility names/descriptions. Resource fallback is deterministic: a missing resource cannot crash a page, alter a NUT value, or translate a technical token incorrectly. Tests must verify required keys and fallback in both cultures.

## Invariant NUT language

The following stay literal: NUT file names (`ups.conf`, `nut.conf`, `upsd.conf`, `upsd.users`, `upsmon.conf`), directives and tokens (`LISTEN`, `MONITOR`, `MINSUPPLIES`, `runtimecal`, `pollinterval`), driver names (`nutdrv_qx`, `usbhid-ups`), status tokens (`OL`, `OB`, `LB`), and **SFTP**. Friendly explanatory text around them is localized.

Display formatting follows the selected UI culture. NUT parsing and serialization always use culture-invariant syntax; for example, a UI may display `12,5` in `pt-BR` but a NUT syntax requiring a decimal point is serialized as `12.5`. Serialization must never rely implicitly on `CurrentCulture`.

## Acceptance for T24–T29

Both cultures require responsive Wide/Medium/Compact validation without clipping, overlap, or broken sidebar/drawer behavior. Keyboard navigation, focus, accessibility labels, semantic review, validation, and redaction must remain equivalent. Tests cover resource completeness, deterministic fallback, selection-dependent display, and invariant NUT serialization.
