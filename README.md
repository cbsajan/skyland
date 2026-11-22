<p align="center">
  <img src="DynamicIsland/Assets/skyland-icon.svg" width="120" alt="SkyLand icon">
</p>

<h1 align="center">SkyLand</h1>

<p align="center">
  A Dynamic Island for Windows 11, by SkyZack.<br>
  A small black capsule at the top of your screen that shows what is going on, and gets out of the way when it is not.
</p>

<p align="center">
  <a href="https://github.com/cbsajan/skyland/releases/latest"><b>Download the latest release</b></a>
</p>

---

## Screenshots

<p align="center">
  <img src="docs/images/capsule-claude.png" width="400" alt="The island showing a working Claude Code session">
  <br><sub>One thing at a time: a Claude Code session working, with its timer.</sub>
</p>

<table>
  <tr>
    <td align="center"><img src="docs/images/capsule-claude-multi.png" alt="Three Claude Code sessions, one needs permission"><br><sub>Several sessions: one needs you.</sub></td>
    <td align="center"><img src="docs/images/card-claude.png" alt="The Claude Code card listing three sessions"><br><sub>Hover for the card.</sub></td>
  </tr>
  <tr>
    <td align="center"><img src="docs/images/capsule-music.png" alt="The island showing the song that is playing"><br><sub>Music: title, artist, equaliser.</sub></td>
    <td align="center"><img src="docs/images/card-music.png" alt="The music card with progress and controls"><br><sub>Hover for the player.</sub></td>
  </tr>
  <tr>
    <td align="center"><img src="docs/images/capsule-claude-music.png" alt="Claude Code in the capsule and the music in the round bubble"><br><sub>Two things at once: Claude, with music in the bubble.</sub></td>
    <td align="center"><img src="docs/images/card-music-bubble.png" alt="Hovering the bubble opens the music player while Claude shrinks into the bubble"><br><sub>Hover the bubble: the player opens, Claude shrinks into it.</sub></td>
  </tr>
  <tr>
    <td align="center"><img src="docs/images/idle.png" alt="The idle island showing CPU, memory, GPU and battery"><br><sub>Idle: your PC stats.</sub></td>
    <td align="center"><img src="docs/images/card-system.png" alt="The System card with four ring gauges"><br><sub>Hover the idle island: ring gauges.</sub></td>
  </tr>
  <tr>
    <td align="center"><img src="docs/images/privacy-popup.png" alt="A pop-up: Microphone in use, Teams, LIVE"><br><sub>An app starts using the microphone.</sub></td>
    <td align="center"><img src="docs/images/privacy-dot.png" alt="The Claude Code capsule with an orange microphone dot on the right"><br><sub>Then an orange dot stays while it does.</sub></td>
  </tr>
  <tr>
    <td align="center"><img src="docs/images/bluetooth-popup.png" alt="A pop-up: Wireless Headphones connected, 82 percent"><br><sub>A Bluetooth device connects, with its battery.</sub></td>
    <td align="center"><img src="docs/images/usb-popup.png" alt="A pop-up: USB drive connected, 32 GB"><br><sub>A USB drive is plugged in.</sub></td>
  </tr>
  <tr>
    <td align="center"><img src="docs/images/charging-popup.png" alt="A pop-up: Charging, charger connected, 76 percent"><br><sub>Charger connected.</sub></td>
    <td align="center"><img src="docs/images/dock-icon.png" width="100" alt="The minimized island: a round SkyLand icon with an orange ring"><br><sub>Minimized to a round icon on the screen edge; the ring shows the microphone is in use.</sub></td>
  </tr>
  <tr>
    <td align="center"><img src="docs/images/alert.png" alt="An alert under the island: High Memory load"><br><sub>A short alert under the island for heavy load or a low battery.</sub></td>
    <td align="center"><img src="docs/images/perf-rings.png" alt="The optional performance ring bubble on the left"><br><sub>Optional performance rings on the left.</sub></td>
  </tr>
</table>

## What it shows

The island works like the iOS Dynamic Island: **one main thing at a time, plus a small round bubble** for the second.

| | |
|---|---|
| **Claude Code** | Which [Claude Code](https://claude.com/claude-code) sessions are working, for how long, and an alert the moment one needs you. The card lists every session. |
| **Focus and timers** | Your [Microsoft Clock](https://apps.microsoft.com/detail/9wzdncrfj3pr) Focus sessions and timers, with a progress ring and the session count. |
| **Media** | Title, artist and artwork of whatever is playing (Spotify, browsers, any player in the Windows media flyout), with play, pause, next and previous. |
| **PC stats** | CPU, memory, GPU and battery when nothing else is on, and along the bottom of the main card. |
| **Alerts** | A short line under the island when the CPU, memory or GPU stays heavily loaded, or the battery is low. It shows once, then stays quiet for two minutes. |
| **Camera and microphone** | A short pop-up with the app name when an app starts using your microphone (orange dot) or camera (green dot), then the dot while it does. Nothing is recorded: only which app is capturing is read. The microphone works on every Windows 11 build; the camera needs a build that still records camera use in the registry (newer builds hide it). |
| **Devices and charging** | Short pop-ups when a Bluetooth device connects or disconnects (with its battery level when Windows reports one), a USB drive or device is plugged in or removed, or the charger is connected or unplugged. |

The most important thing (Claude, then Focus, then music) is the **main capsule**; the next one is the **bubble** beside it. Hover the capsule to open its card, or hover the bubble to open that card instead, for example the music player while Claude is running. You can also turn on a small **performance ring** bubble on the left (Settings > Island). **Drag the island anywhere** on screen, for example below your browser tabs; it remembers the spot and snaps back to the top centre when you get close.

## Install

1. Download `SkyLand-x.y.z.msi` from the [latest release](https://github.com/cbsajan/skyland/releases/latest) and run it.
2. SkyLand starts when it finishes and turns on **Start with Windows** (switch it off in Settings).

It installs for the current user only, so there is **no admin prompt**. SkyLand then looks for updates on its own: when a new version is out you get a prompt in the island with **Install** and **Later**.

> **Unsigned for now.** Windows SmartScreen may say "unknown publisher" the first time. Choose *More info*, then *Run anyway*. Each release has a `.sha256` file so you can check your download, and the in-app updater refuses any installer whose hash does not match.

**Requirements:** Windows 11 (64-bit). The installer is self-contained, so you do not need to install .NET.

## Use it

- **Minimize:** open any card and click the **minus** next to the gear. The island shrinks into a small round SkyLand icon on the left or right screen edge, with a colored ring when something needs you (amber: Claude, orange: microphone, green: camera). Choose the left or right edge (or automatic) in Settings > Island > Minimized icon, and drag the icon along the edge to move it; click it, or choose *Show island* in the tray menu, to bring the island back.
- **Settings gear:** open any card (hover the island) and click the small gear in its top-right corner.
- **Tray icon:** double-click for Settings; right-click for *Settings*, *Reset position* and *Exit*.
- **Settings** has switches for the expand trigger (hover or click), pop-up on track change, the performance ring, the camera and microphone indicator, Bluetooth and USB pop-ups, heavy-load and low-battery alerts, Microsoft Clock sync, Claude Code status, automatic updates, and a *Reset position* button.

### Claude Code

Open **Settings > Integrations > Connect Claude Code** once. It adds a few hook entries to `~/.claude/settings.json` (a backup is saved first, and *Disconnect* removes only its own entries), then new Claude Code sessions show up in the island. Only the event type, project folder, tool name and a short hint are sent, over a pipe private to your Windows user. Prompts and file contents never leave Claude Code. It is read-only: you still answer in the terminal.

### Microsoft Clock

Clock has no public API, so SkyLand reads its on-screen countdown through Windows UI Automation. That works while the Clock window is **open and not minimized** on the Focus or Timer page; when it is minimized Windows hides its content, so SkyLand shows the last values and says so. English Windows only.

## Build from source

You need the [.NET SDK](https://dotnet.microsoft.com/download) (8 or newer) on Windows.

```
git clone https://github.com/cbsajan/skyland.git
cd skyland
dotnet run --project DynamicIsland
```

To build the installer you also need [WiX Toolset 5](https://wixtoolset.org/) (`dotnet tool install --global wix --version 5.0.2`, then `wix extension add -g WixToolset.Util.wixext/5.0.2`):

```
cd Installer
.\build-installer.ps1        # creates dist\SkyLand-<version>.msi and its .sha256
```

## Project layout

```
DynamicIsland/        the app (WPF, .NET 8)   - code namespaces keep the old name on purpose
DynamicIsland.Hook/   island-hook.exe, the small program Claude Code runs on each event
Installer/            WiX package and the build script
docs/SkyLand.md       full documentation: behaviour, design decisions, limits, release process
NEXUS/                an unused WinUI 3 template project, kept for reference
```

## Credits

SkyLand is made by [**C B Sajan**](https://github.com/cbsajan), under the **SkyZack** name.

## Contributors

Thank you to everyone who has helped build SkyLand.

<a href="https://github.com/cbsajan/skyland/graphs/contributors">
  <img src="https://contrib.rocks/image?repo=cbsajan/skyland" alt="SkyLand contributors" />
</a>

**Notable contributions**

- [Ahmed Samy](https://github.com/ahmedsamy56): download and upload speed on the idle island and the System card ([#5](https://github.com/cbsajan/skyland/pull/5))

Want to be on this list? Pick an issue labeled [good first issue](https://github.com/cbsajan/skyland/labels/good%20first%20issue) and read [CONTRIBUTING.md](CONTRIBUTING.md).

## License

SkyLand is licensed under the [Apache License 2.0](LICENSE): you may use, modify and distribute it, including commercially, as long as you keep the license and the [NOTICE](NOTICE) file and state your changes. It comes with no warranty.

The license covers the source code. It does not give you the right to use the **SkyLand** or **SkyZack** names or logos to promote your own product, so if you fork it, please rename it and use your own icon.

Contributions are welcome: see [CONTRIBUTING.md](CONTRIBUTING.md) for how to build, test and send a change. By sending one you agree that it is licensed under the same terms.
