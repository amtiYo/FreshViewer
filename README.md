<h1 align="center">FreshViewer</h1>

<p align="center">

![Release Download](https://img.shields.io/github/downloads/amtiYo/BlurViewer/total?style=flat-square)
[![Release Version](https://img.shields.io/github/v/release/amtiYo/BlurViewer?style=flat-square)](https://github.com/amtiYo/BlurViewer/releases/latest)
[![GitHub license](https://img.shields.io/github/license/amtiYo/BlurViewer?style=flat-square)](LICENSE)
[![GitHub Star](https://img.shields.io/github/stars/amtiYo/BlurViewer?style=flat-square)](https://github.com/amtiYo/BlurViewer/stargazers)
[![GitHub Fork](https://img.shields.io/github/forks/amtiYo/BlurViewer?style=flat-square)](https://github.com/amtiYo/BlurViewer/network/members)
![GitHub Repo size](https://img.shields.io/github/repo-size/amtiYo/BlurViewer?style=flat-square&color=3cb371)
</p>

A modern, distraction‑free image viewer for Windows built with .NET 8 and Avalonia. FreshViewer features a crisp Liquid Glass interface, smooth navigation, rich format support, and a handy info overlay — all optimized for everyday use.

## Highlights
- Liquid Glass UI: translucent cards, soft shadows, and subtle motion for a premium feel
- Smooth navigation: kinetic panning, focus‑aware zoom, rotate, and fit‑to‑view
- Info at a glance: compact summary card + detailed metadata panel (EXIF/XMP)
- Powerful formats: stills, animations, modern codecs, and DSLR RAW families
- Personalization: themes, language (ru/en/uk/de), and keyboard‑shortcut profiles

## Liquid Glass design
FreshViewer embraces a lightweight “Liquid Glass” aesthetic:
- Top app bar with rounded glass buttons (Back, Next, Fit, Rotate, Open, Info, Settings, Fullscreen)
- Left summary card (file name, resolution, position in folder)
- Slide‑in information panel (I) with fluid enter/exit animation
- Compact status pill at the bottom with action hints

The result is a calm, legible interface that stays out of the way while keeping essential controls at your fingertips.

## Supported formats
- Common: PNG, JPEG, BMP, TIFF, ICO, SVG
- Modern: WEBP, HEIC/HEIF, AVIF, JXL
- Pro: PSD, HDR, EXR
- DSLR RAW: CR2/CR3, NEF, ARW, DNG, RAF, ORF, RW2, PEF, SRW, MRW, X3F, DCR, KDC, ERF, MEF, MOS, PTX, R3D, FFF, IIQ
- Animation: GIF/APNG (with loop handling)

## Keyboard & mouse (default)
- Navigate: A/← and D/→
- Fit to view: Space / F
- Rotate: R / L
- Zoom: mouse wheel, + / −
- Info panel: I
- Settings: P
- Fullscreen: F11
- Copy current frame: Ctrl+C

## Requirements
- Windows 10 1809 or newer (x64)
- .NET 8 SDK

## Build & run
```bash
dotnet restore
dotnet build -warnaserror
dotnet test
dotnet run --project FreshViewer/FreshViewer.csproj -- <optional-image-path>
```

Publish (Windows x64, single file):
```bash
dotnet publish FreshViewer/FreshViewer.csproj -c Release -r win-x64 \
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true --self-contained=false
```

### Liquid Glass effect
- Requires Windows 10 1809+ with GPU-backed composition. The shader is disabled automatically on unsupported platforms.
- Toggle the feature from **Settings → Liquid Glass effects** or via the `FRESHVIEWER_FORCE_LIQUID_GLASS` environment variable (`true`/`false`).
- A small preview card inside the settings panel helps verify the shader versus the fallback gradient.
- When the GPU path is unavailable the app renders a static translucent fallback so the UI remains legible.

## Settings
- Themes: switch between pre‑tuned Liquid Glass palettes
- Language: ru / en / uk / de
- Shortcuts: select a profile (Standard, Photoshop, Lightroom) or export/import your own mapping (JSON)

## Contributing
Contributions are welcome. Please see [CONTRIBUTING.md](./CONTRIBUTING.md) for a short guide.

## License
MIT — see [LICENSE](./LICENSE).

## Credits
Avalonia, SkiaSharp, ImageSharp, Magick.NET, and MetadataExtractor.
