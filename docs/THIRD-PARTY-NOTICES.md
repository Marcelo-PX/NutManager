# Third-party notices

This file records third-party material redistributed inside the NutManager source tree. Packages
consumed through NuGet are not listed here; they carry their own licences through the package
manager. Only assets copied into this repository are recorded.

## Fluent UI System Icons

- **Project:** [microsoft/fluentui-system-icons](https://github.com/microsoft/fluentui-system-icons)
- **Licence:** MIT
- **Copyright:** Copyright (c) 2020 Microsoft Corporation
- **What is used:** the path data of a small number of 24 px "regular" icons, copied verbatim from
  the official repository into `src/NutManager.App/Presentation/Themes/NutIcons.axaml` as Avalonia
  `StreamGeometry` resources.
- **Why it is vendored:** the application must run entirely offline, so the geometry is stored
  locally instead of being fetched or pulled in as a runtime dependency. No icon font, icon package
  or remote asset is referenced.
- **Icons imported:** Home, Server, Settings (gear), Pulse, and the five configuration-domain glyphs
  used by the NUT file rail — document list, battery checkmark, server, people team and pulse.
- **Not imported:** the options/sliders glyph was previously taken from the same source but is now
  drawn in this repository. Fluent packs the tracks and both handle rings into a single path, which
  makes the handles impossible to move independently; the local version reproduces the same drawing
  at the same coordinates as three pieces so each handle can travel its track as one unit.

No icon package or icon font is referenced. That is a consequence of how this application draws
icons rather than a preference: twenty-one of its glyphs are split into independently animated parts
— the two device LEDs blink out of phase, the gear teeth spin while the hub stays still, the
diagnostics dot sweeps across its base — and a font glyph is a single indivisible shape. Vendoring
the geometry is what makes those animations possible at all.

The MIT licence permits this use provided the copyright notice and permission notice are retained.
The notice is reproduced below.

```text
MIT License

Copyright (c) 2020 Microsoft Corporation

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
```

All other icons in `NutIcons.axaml` are original NutManager geometry.

## Server illustration

`src/NutManager.App/Assets/Illustrations/server-security.png` is a decorative illustration supplied
by the project owner for this application. It is cropped and resized from the provided source; no
third-party asset is embedded in it.
