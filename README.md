# Opsis — Desktop Customization

<p align="center">
  <img src="assets/Opsis.ico" width="96" height="96" alt="Opsis logo" />
</p>

<p align="center">
  <strong>Your desktop, organized into a lightweight workspace hub.</strong><br/>
  Live wallpapers, icon containers, snap zones, widgets and a real plugin system — all native Windows, no Electron.
</p>

<p align="center">
  <img alt="Platform" src="https://img.shields.io/badge/platform-Windows%2010%2F11-0078D6?logo=windows&logoColor=white" />
  <img alt=".NET" src="https://img.shields.io/badge/.NET-10.0-512BD4?logo=dotnet&logoColor=white" />
  <img alt="Version" src="https://img.shields.io/badge/version-1.9.3-00B4D8" />
  <img alt="License" src="https://img.shields.io/badge/license-MIT-green" />
  <img alt="Languages" src="https://img.shields.io/badge/localized-14%20languages-blue" />
</p>

---

## Contents

- [What is Opsis](#what-is-opsis)
- [Features](#features)
- [Plugins](#plugins)
- [Install](#install)
- [Build from source](#build-from-source)
- [Project structure](#project-structure)
- [Architecture notes](#architecture-notes)
- [Localization](#localization)
- [Contributing](#contributing)
- [License](#license)

## What is Opsis

**Opsis** is a native WinForms desktop-customization suite for Windows 10/11. It renders live wallpapers directly on the desktop (WorkerW/Progman injection, the same technique behind tools like Lively Wallpaper), gives you Fences-style icon containers, FancyZones-style window snapping, native always-on-top widgets, and system-wide quality-of-life tooling — all as a single lightweight app with no browser runtime bundled in.

Formerly released as *MotionDesk Studio*.

## Features

### 🖼️ Live Wallpaper Engine
- WorkerW/Progman desktop injection (classic and Windows 11 "raised desktop" paths), with self-healing retry across boot races.
- Canvas-based animated styles (Waves, Aurora, TechGrid, more) with wind-reactive sea waves and real rain-on-glass, plus full video/playlist wallpapers (`.mp4`, `.wmv` auto-converted via FFmpeg, `.zip`/`.7z` playlist archives).
- Live weather overlay (temperature, wind, condition) driving the wallpaper's own animation.
- Smooth crossfade between playlist items, per-video enable/disable, shuffle.
- Optional Web-Audio EQ (bass/mid/treble/volume) applied to the wallpaper's own video audio.

### 🗂️ DeskZones
- FancyZones-style window snapping: grid, rows, columns, custom and priority-grid templates, per-monitor layouts.
- Real per-pixel-alpha translucent overlay, driven by the OS's own `EVENT_SYSTEM_MOVESIZESTART/END` signal (not a mouse-hook heuristic).
- AquaSnap-style always-on magnetic edge/corner snapping, independent of the Shift-held grid overlay.
- Keyboard-driven zone navigation (`Ctrl+Alt+Shift+Arrow`).

### 📦 DeskContainers
- Fences-style icon containers: hover roll-up, large icons, drag & drop (copy, never move your files), adjustable opacity, quick-hide integration.
- Double-click title to rename; offline items preserved; positions clamped on-screen.

### 🧩 Native Widgets
- Clock (analog themes + digital font/color), Network (sparkline or bars), Audio, Weather, Disk (ring gauges with paging), System Monitor (CPU/RAM/GPU with colored meter bars).
- Independent widget theme mode, per-widget opacity, native resize grip.

### 🎚️ Audio Enhancement
- 5-band live spectrum visualizer with presets, WMP/Winamp-style toggle.
- Optional real system-wide EQ via [Equalizer APO](https://equalizerapo.sourceforge.io/) integration (no virtual driver, no kernel signing — just config generation against an already-signed, already-trusted APO).

### ⚡ Automation & Workspaces
- Editable/deletable automation rules (launch a profile when a process starts).
- Workspace profiles (Work / Gaming / Focus) saving zones, widgets and theme together.
- Command palette (`Ctrl+K`).

### 🎨 Personalization
- Custom desktop icon set via `IconAtlasEngine`, `.theme` package export, Mica/Acrylic/DWM blur, real taskbar translucency (TranslucentTB technique — no DLL injection into `dwm.exe`/`explorer.exe`).
- Dark/light theme, accent colors, animations toggle.

### 🌍 Fully Localized
14 languages (English, Greek, Spanish, French, German, Italian, Portuguese, Russian, Turkish, Arabic, Hindi, Japanese, Korean, Chinese), including the in-app Version History and the installer itself.

## Plugins

Opsis loads plugins from:
1. `<Opsis install folder>\plugins\*\`
2. `%AppData%\Opsis\plugins\*\`
3. Any path registered under `HKCU\Software\Opsis\PluginPaths`

Each plugin runs in its own `AssemblyLoadContext` for dependency isolation, against a small shared contract assembly. A plugin can contribute sidebar pages, tray menu items and an About-page card (help, usage, version history, license) — and it inherits Opsis's live theme and language automatically. A failing plugin is logged and skipped; it never takes the host down.

The reference plugin is **Opsis News Crawler** — a scrolling news/market ticker bar that docks as a Windows AppBar and plays nicely with DeskZones' work-area awareness.

Toggle plugin loading from **Settings → Load plugins**.

## Install

Download the latest installer from the [Releases](../../releases/latest) page and run `OpsisSetup.exe`. Opsis checks for updates automatically (monthly, configurable).

## Build from source

Requirements: Windows 10/11, [.NET 10 SDK](https://dotnet.microsoft.com/download), [WebView2 Runtime](https://developer.microsoft.com/microsoft-edge/webview2/) (usually already present).

```bash
dotnet build MotionDeskStudio.csproj -c Release
```

Run directly:

```bash
dotnet run --project MotionDeskStudio.csproj
```

or use `RunLatest.bat`.

Publish a self-contained-false build for packaging:

```bash
dotnet publish MotionDeskStudio.csproj -c Release -r win-x64 --self-contained false
```

Build the installer ([Inno Setup 6](https://jrsoftware.org/isinfo.php) required):

```bash
ISCC.exe installer.iss
```

This produces `Output\OpsisSetup.exe`.

## Project structure

```
src/
  Services/     Cross-cutting services (settings, paths, updates, plugin host, device interop)
  UI/           Main window, pages, themed controls
  Widgets/      Wallpaper engine, DeskZones, DeskContainers, native widgets, tray
locales/        14 language JSON files
assets/         Icons
installer.iss   Inno Setup script
```

## Architecture notes

- **Desktop injection**: Opsis positions its wallpaper window between `Progman` and the desktop icon layer using the same WorkerW technique used by Lively Wallpaper/Wallpaper Engine, not a licensed component from either.
- **Zone snapping**: uses the public, documented `SetWinEventHook`/`EVENT_SYSTEM_MOVESIZESTART` accessibility API — the same authoritative signal PowerToys FancyZones relies on — rather than a custom mouse hook.
- **Taskbar blur**: `SetWindowCompositionAttribute` applied directly to the Taskbar's own HWND (the TranslucentTB technique). Deliberately **does not** do DLL injection into `dwm.exe`/`explorer.exe` — that class of technique (as used by some third-party shell mods) carries real desktop-shell-stability risk and was explicitly scoped out.
- **Settings/data**: atomic file writes (`AtomicFile`, tmp+rename) for every JSON store to avoid partial-write corruption.

## Localization

All UI strings live in `locales/<culture>.json`. English and Greek are maintained first-hand; the rest are kept in parity via scripted key/placeholder checks. Missing keys fall back to English.

## Contributing

Issues and pull requests are welcome. Please keep changes focused — small, reversible diffs are much easier to review than broad refactors bundled with a feature.

## License

[MIT](LICENSE) © Opsis Team
