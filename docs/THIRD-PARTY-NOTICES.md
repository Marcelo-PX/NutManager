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
- **Status:** fallback only. Since T32 the application draws its icons from
  `Material.Icons.Avalonia`, a NuGet dependency that carries its own MIT licence through the package
  manager and is therefore not recorded in this file. `NutIconLibrary.cs` replaces every catalog
  entry at start-up, so nothing vendored here is rendered while the library supplies the kind mapped
  to that name. The geometry is retained so a name still resolves to a glyph if a future version of
  the library drops a kind, rather than leaving an empty box in a view.
- **Icons imported:** the configuration-domain glyphs used by the NUT file strip — document list,
  battery checkmark, server, people team and pulse.
- **No longer present:** the navigation silhouettes and the options/sliders glyph, which were split
  into independently animated parts. Those parts were removed in T32 when the icons moved onto the
  library, which supplies one shape per name.

The application is offline regardless: the library ships the geometry inside the package and nothing
is fetched at runtime.

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
