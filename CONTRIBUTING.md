# Contributing to SkyLand

Thanks for wanting to help. Bug reports, ideas, design feedback, testing on your own PC and code are all welcome.

## Ways to help

- **Report a bug.** Open an issue with your Windows version, the SkyLand version (tray icon > Settings), what you did, what you expected and what happened. A screenshot helps.
- **Suggest a feature.** Open an issue and describe the problem first ("I keep missing X"), then the idea. Small, focused ideas are easiest to ship.
- **Test.** Some things are hard to test on one PC: real Bluetooth devices, USB drives, several monitors, other Windows 11 builds, high-DPI screens. Telling us what works or not on your setup is very useful.
- **Send code or docs.** See below.

If you are not sure where to start, look for issues labeled `good first issue`.

## Build and run

You need Windows 11 (64-bit) and the [.NET SDK](https://dotnet.microsoft.com/download) 8 or newer.

```powershell
git clone https://github.com/cbsajan/skyland.git
cd skyland
dotnet run --project DynamicIsland
```

To build the installer you also need WiX Toolset 5. See [Build from source](README.md#build-from-source) in the README.

A map of the code, the design decisions and every feature are described in [docs/SkyLand.md](docs/SkyLand.md). Read the "Code map" section first.

## Testing your change

There are no automated UI tests, so please run the app and check the feature by hand. Several features have test seams (environment variables) so you do not need real hardware or a real Claude Code session:

| Variable | Use |
|---|---|
| `SKYLAND_HOT_LEVEL` | Replaces the 85% load threshold, to trigger the load alert |
| `SKYLAND_POWER_FAKE=charging\|unplugged` | Shows the charging pop-up |
| `SKYLAND_DEVICE_FAKE=1` | Reads fake Bluetooth/USB events from `%TEMP%\skyland-device-fake.txt` |
| `SKYLAND_PRIVACY_FAKE=1` | Reads fake microphone/camera use from `%TEMP%\skyland-privacy-fake.txt` |
| `SKYLAND_HIDE_MEDIA=1` | Ignores the media player (no song on screen) |
| `ISLAND_PIPE_NAME` | Uses another pipe, so a test island can be fed simulated Claude sessions |

Details for each are in [docs/SkyLand.md](docs/SkyLand.md). If you add a feature that needs hardware to test, please add a seam like these.

Before opening a pull request, check that:

- `dotnet build` finishes with no new warnings.
- The feature works at 100% and at a higher display scale (125% or 150%).
- Hovering, opening cards, minimizing and restoring still behave.

## Branches

- `main` is what is released. It only changes when a new version is published.
- `develop` is where work comes together. **Every pull request targets `develop`, not `main`.**
- Do your work on your own branch, created from an up-to-date `develop`: `feature/<name>` for a feature, `fix/<name>` for a bug fix, `docs/<name>` for documentation.

## Pull requests

1. Open an issue first for anything bigger than a small fix, so we agree on the idea before you spend time on it.
2. Fork the repo and create a branch from `develop` (`fix/usb-label`, `feature/clock-on-idle`).
3. Keep the change focused: one fix or feature per pull request.
4. Follow the style of the surrounding code (C# 12, nullable enabled, small methods, comments that explain *why*).
5. Update [docs/SkyLand.md](docs/SkyLand.md) (and the README if users will notice) when behavior changes.
6. For visible changes, add a before/after screenshot or a short recording to the pull request.
7. Write a clear commit message that says what changed and why.
8. Open the pull request against `develop`. If GitHub proposes `main`, change the base branch to `develop` before you submit.

## Rules that protect users

These are firm, because SkyLand runs on people's own PCs:

- **Privacy.** The Claude Code hook sends only the event type, project folder name, tool name and a short detail. Never send prompts, file contents or anything personal. Nothing in SkyLand sends data to a server except the update check against GitHub.
- **Claude Code settings.** SkyLand never edits `~/.claude/settings.json` except through the explicit Connect and Disconnect actions, only touches its own entries, makes a backup first and leaves invalid JSON alone.
- **No secrets.** Never commit certificates, passwords, tokens or personal paths. Code signing uses a prompt or the `SKYLAND_CERT_PASSWORD` variable only.
- **Dependencies.** Add a new package only when it is really needed, say why in the pull request, and add its credit to [NOTICE](NOTICE).
- **Performance.** SkyLand sits on screen all day. Avoid looping animations and polling that keep the CPU busy; gate them and cap the frame rate.

## Ideas that are open

- A clock and date on the idle island
- Wi-Fi and VPN pop-ups, and network speed on the System card
- A quick timer started from the island
- A file shelf (drop files on the island)
- Volume and brightness pop-ups
- Next calendar meeting
- CPU temperature

Comment on an issue before you start one of these, so two people do not build the same thing.

## License

SkyLand is licensed under the [Apache License 2.0](LICENSE). By sending a contribution you agree that it is licensed under the same terms. The license does not give you the right to use the SkyLand or SkyZack names or logos for your own product (see the README).

## Be kind

Be respectful and patient, assume good intent, and give feedback on the code, not the person. Maintainers may close issues or pull requests that are abusive or off-topic.
