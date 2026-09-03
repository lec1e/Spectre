<p align="center">
  <img src="./.github/preview/banner.png" width="920" alt="Spectre banner"/>
</p>

<p align="center">
  <a href="https://github.com/lec1e/Spectre/releases/latest">
    <img src="https://img.shields.io/github/v/release/lec1e/Spectre?label=latest&color=e11d48" alt="Latest release"/>
  </a>
  <a href="https://github.com/lec1e/Spectre/releases/latest">
    <img src="https://img.shields.io/github/downloads/lec1e/Spectre/total?color=7f1d1d" alt="Downloads"/>
  </a>
</p>

<p align="center">
  <strong>Spectre</strong> is a Roblox launcher with a dark crimson liquid-glass shell —
  Versions Manager, LIVE channel lock, VIP, BanAsync, multi-instance, and more.
</p>

<p align="center">
  <img src="./.github/preview/app-preview.png" width="920" alt="Spectre Home"/>
</p>

<p align="center">
  <img src="./.github/preview/games.png" width="450" alt="Games"/>
  &nbsp;
  <img src="./.github/preview/library.png" width="450" alt="Library"/>
</p>
<p align="center"><sub>Games · Library</sub></p>

<p align="center">
  <img src="./.github/preview/mods.png" width="450" alt="Mods"/>
  &nbsp;
  <img src="./.github/preview/settings.png" width="450" alt="Settings"/>
</p>
<p align="center"><sub>Mods · Settings</sub></p>

## What’s new in 2.0.30

- Download is **Spectre.exe** (Eclipse.exe still accepted for existing installs)
- Home greeting uses your computer name
- Animated crimson nebula background from the Spectre banner
- Tab previews for Home, Games, Library, Mods, and Settings

## Features

- **Versions Manager** — one profile per executor (WEAO / manual hash), optional picker on launch
- **LIVE channel lock** — Player launches forced to production (toggleable)
- **Liquid glass UI** — dark crimson shell, cursor ribbon, 30+ color presets
- **BanAsync** (Windows) — clean Roblox traces, MAC / MachineGuid helpers
- **Multi-instance + window tiling**
- **VIP / Server Browser / News**
- **Privacy mode** — truncate `RobloxCookies.dat` before launch
- **Stream mode** — hide account-identifying UI while streaming

## Download

Grab the latest **Spectre.exe** from [Releases](https://github.com/lec1e/Spectre/releases/latest).

Auto-update picks up new stable releases when update checks are enabled. Older **Eclipse.exe** installs still update from the same release.

## Build

```bash
dotnet build Froststrap/Froststrap.csproj -c Release
```

## License

AGPL-3.0 for Spectre modifications; MIT for upstream Bloxstrap / Fishstrap / Froststrap code.
