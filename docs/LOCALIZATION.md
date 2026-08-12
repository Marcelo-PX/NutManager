# NutManager Localization Architecture

## Official cultures

T24 establishes the official UI cultures:

- `pt-BR` — Português (Brasil), the initial/default culture;
- `en-US` — English (United States).

Settings includes **Appearance & Language** with System, Light, Dark, Português (Brasil), and English (United States). The language preference is non-secret per-user UI data. The current implementation persists the selected culture and clearly requires an application restart for a complete switch; it does not attempt a partial live resource replacement. The design does not prevent a future deterministic runtime switch.

## Resource boundary

The localization boundary is `NutManager.App/Localization`: `Strings.resx` is the neutral fallback, and `Strings.pt-BR.resx` and `Strings.en-US.resx` are the explicit official-culture resources. `NutManagerLocalizer` resolves the requested culture, falls back deterministically to `pt-BR`, and finally returns the semantic key when no resource exists, so a missing key does not crash the view.

Implemented groups include `App.*`, `Nav.*`, `Shell.*`, `Management.*`, `Access.*`, `Status.*`, `Appearance.*`, `Theme.*`, `Language.*`, `Sidebar.*`, `Settings.*`, `Profiles.*`, `Transport.*`, `SshAuth.*`, `SmbAuth.*`, `DirtyDraft.*`, `ConnectionTest.*`, `Credential.*`, and `Validation.*`. They cover the shell plus the T24A managed-server editor, localized options, inline validation, dirty decisions, restart state, and connection-test results. Tests require all declared keys in both cultures and exact key-set parity between `pt-BR` and `en-US`.

New user-facing T24–T29 strings use stable semantic keys rather than hard-coded text. T24B adds complete key families for `Administration.*`, `Overview.*`, `Devices.*`, and `Diagnostics.*`. T25 adds the generic `Semantic.*` families. T26 adds `Ups.Editor.*`, `Ups.Driver.*`, `Ups.Field.*`, `Ups.Group.*`, `Ups.Unit.*`, `Ups.Transport.*`, `Ups.Runtimecal.*`, and `Ups.Validation.*`; every production `ups.conf` descriptor has a label and help entry in both official cultures. Exact `pt-BR`/`en-US` key parity is tested. Core descriptors carry only resource keys; keys are never Portuguese display text.

Typed validation follows the same convention with implemented keys such as `Validation.Host.Required`, `Validation.Host.Invalid`, `Validation.Port.Range`, `Validation.Profile.NameDuplicate`, `Validation.Ssh.PrivateKeyRequired`, and `Validation.Smb.ShareRootRequired`. Core returns resource keys, never Portuguese or English messages. Option labels are localized presentation values and never final enum names.

Navigation and every new shared-shell label, tooltip, status, and accessibility name are localized. Future page work must also localize headings, fields, buttons, menus, contextual help, validation, warnings, errors, and semantic-review content. Resource fallback cannot alter a NUT value or translate a technical token incorrectly.

## Invariant NUT language

The following stay literal: NUT file names (`ups.conf`, `nut.conf`, `upsd.conf`, `upsd.users`, `upsmon.conf`), directives and tokens (`LISTEN`, `MONITOR`, `MINSUPPLIES`, `runtimecal`, `pollinterval`), driver names (`nutdrv_qx`, `usbhid-ups`), status tokens (`OL`, `OB`, `LB`), and **SFTP**. Friendly explanatory text around them is localized.

Display formatting follows the selected UI culture. NUT parsing and serialization always use culture-invariant syntax; for example, a UI may display `12,5` in `pt-BR` but a NUT syntax requiring a decimal point is serialized as `12.5`. Serialization must never rely implicitly on `CurrentCulture`.

## Acceptance for T24–T29

Both cultures require responsive Wide/Medium/Compact validation without clipping, overlap, or broken sidebar/drawer behavior. Keyboard navigation, focus, accessibility labels, semantic review, validation, and redaction remain equivalent. Automated coverage verifies exact official-culture key parity, deterministic fallback, localized missing/empty states, localized administrative section applicability, T25 semantic review resources, and invariant NUT tokens. Complete form labels and help for T26–T28 are added only with their documented production schemas.
