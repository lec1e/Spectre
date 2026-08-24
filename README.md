<p align="center">
  <img src="./.github/preview/banner.png" width="920" alt="Eclipse banner"/>
</p>

<p align="center">
  <a href="https://github.com/lec1e/Eclipse/releases/latest">
    <img src="https://img.shields.io/github/v/release/lec1e/Eclipse?label=latest&color=a855f7" alt="Latest release"/>
  </a>
  <a href="https://github.com/lec1e/Eclipse/releases/latest">
    <img src="https://img.shields.io/github/downloads/lec1e/Eclipse/total?color=22d3ee" alt="Downloads"/>
  </a>
</p>

<p align="center">
  <strong>Eclipse</strong> is a Froststrap-based Roblox launcher with MrExLive / ExploitStrap features ported in —
  Versions Manager profiles, LIVE channel lock, BanAsync tools, multi-instance, and a dark Eclipse glass theme system.
</p>

<p align="center">
  <img src="./.github/preview/home.png" width="900" alt="Eclipse Home"/>
</p>

<p align="center">
  <img src="./.github/preview/games.png" width="430" alt="Eclipse Games"/>
  &nbsp;&nbsp;
  <img src="./.github/preview/library.png" width="430" alt="Eclipse Library"/>
</p>

<p align="center">
  <img src="./.github/preview/mods.png" width="430" alt="Eclipse Mods"/>
  &nbsp;&nbsp;
  <img src="./.github/preview/settings.png" width="430" alt="Eclipse Settings"/>
</p>

## What’s new vs stock Froststrap

- **Versions Manager** — one profile per executor (WEAO / manual hash), dropdown to switch, optional picker on launch
- **LIVE channel lock** — Player launches forced to production (toggleable)
- **Eclipse themes** — dark liquid-glass shell, abyss glow background, 30+ color presets
- **BanAsync** (Windows) — clean Roblox traces, MAC / MachineGuid helpers
- **Multi-instance + window tiling**
- **VIP / Server Browser / News** tabs
- **Privacy mode** — truncate `RobloxCookies.dat` before launch
- **Stream mode** — hide account-identifying UI while streaming

C# namespaces remain `Froststrap.*` for upstream compatibility; the product name, install folder, and branding are **Eclipse**.

## Download

Grab the latest **Eclipse.exe** from [Releases](https://github.com/lec1e/Eclipse/releases/latest).  
Auto-update picks up new stable releases when update checks are enabled.

## Build

```bash
# dependencies (if not already present)
git clone --depth 1 https://github.com/Froststrap/FluentAvalonia.git FluentAvalonia
git clone --depth 1 https://github.com/Froststrap/LucideAvalonia.git LucideAvalonia
git clone --depth 1 https://github.com/Froststrap/ColorPicker.git ColorPicker

dotnet build Froststrap/Froststrap.csproj -c Release
```

## License

Same multi-license model as Froststrap (AGPL-3.0 for Eclipse modifications; MIT for upstream Bloxstrap/Fishstrap code).
MrExLive / ExploitStrap ports inherit MIT from that fork.
