# SPT Launcher

A native WPF launcher for [SPT](https://sp-tarkov.com/) (Single Player Tarkov) with Fika co-op support.

## Features

- **Play-first Launcher** — Launch / Stop up front, with a compact readiness checklist for SPT, live Tarkov, and Fika
- **Guided first-run setup** — Install SPT first, then auto-detect or browse for `SPT.Launcher.exe` (including modern `SPT_Runtime` installs)
- **Safer SPT updates** — Preflight checks, download details, cancel, installer-only mode, and backup/restore recovery
- **Live Tarkov / patcher awareness** — Detects your live EFT version and checks the SPT patcher CDN; only warns when no downgrade patcher exists for your build
- **Fika co-op** — Install/update Fika, enable co-op, and set host IP
- **Servers & Tools** — Local/remote server helpers and developer utilities
- **Self-update** — Checks GitHub releases and replaces the installed launcher in place

> **Note:** The Mods tab is hidden for now (still under development).

## Requirements

- Windows 10/11
- .NET 8.0 (included in self-contained releases)
- A legitimate Escape From Tarkov install (for SPT install / updates that need the downgrader)
- An SPT install (or use the in-app installer on first run)

## Installation

1. Download the latest release from [Releases](https://github.com/bblair321/spt-launcher/releases)
2. Extract anywhere you like
3. Run `SPTLauncher.exe`

## Building from Source

### Prerequisites

- .NET 8.0 SDK
- Visual Studio 2022 (or another IDE / `dotnet` CLI)

### Build & run

```powershell
dotnet build
dotnet run
```

### Publish a self-contained exe

```powershell
dotnet publish -c Release -r win-x64 --self-contained true
```

## Usage

### First-time setup

1. Start the launcher
2. If SPT isn’t set up yet, the **Get set up** wizard appears:
   1. **Install SPT** (or choose “I already have SPT”)
   2. **Auto-detect** / **Browse** for `SPT.Launcher.exe`
   3. Continue to the main launcher
3. Optionally enable Fika under **Show setup**

### Day-to-day play

1. Open the **Launcher** tab
2. Check **Readiness** (SPT / Tarkov / Fika)
3. Click **Launch SPT**
4. Use **Stop** to shut down related SPT processes

### Tarkov / patcher status

- Live Tarkov is read from your BSG install (registry / exe), not the already-downpatched SPT copy
- If a matching CDN patcher exists for your live version, install/update can proceed
- The “no patcher” warning only appears when no downgrade patcher is found for your live Tarkov → SPT target
- After SPT is already installed, Tarkov shows as a live install — patcher details are hidden unless something is wrong

### Updates

- **SPT** — Update / install actions on the readiness row; advanced recovery under **Show setup**
- **Fika** — Install or Update when a newer client/server build is available
- **This launcher** — Banner prompt when a newer GitHub release is available

### Servers

Use the **Servers** tab for local/remote server configuration and related helpers.

## Configuration

Stored under:

- **Settings:** `%APPDATA%\SPT Launcher WPF\settings.json`
- **Server configs:** `%APPDATA%\SPT Launcher WPF\servers.json`

SPT’s own launcher config (including Fika IP / dev mode) lives next to your SPT install (e.g. under `SPT_Runtime` / launcher user data).

## Troubleshooting

**App won’t start**

- Use a release build of `SPTLauncher.exe`, or install the [.NET 8 runtime](https://dotnet.microsoft.com/download/dotnet/8.0) for framework-dependent builds
- Try running as administrator only if Windows blocks the exe

**SPT path / auto-detect fails**

- Newer SPT builds put the launcher at `...\SPT_Runtime\SPT.Launcher.exe`
- Use **Auto-detect** after install, or **Browse** to that file
- Desktop / install-folder `SPT.Launcher.lnk` shortcuts are also resolved

**“No patcher for this Tarkov version yet”**

- BSG updated live Tarkov and SPT hasn’t published a matching `Patcher_{live}_to_{target}.7z` yet
- Wait for the patcher, then click **Recheck**
- Keep live Tarkov updated — the official SPT installer uses the newest matching patcher when available

**Launch / Stop issues**

- Confirm `SPT.Launcher.exe` under **Show setup**
- Check antivirus isn’t blocking SPT or this launcher
- **Stop** targets SPT-related processes; it won’t close this app

**Fika**

- Install Fika into your SPT folder when prompted
- Enable Fika co-op and save the host IP under **Show setup**

## Contributing

1. Fork the repository
2. Create a feature branch
3. Make your changes
4. Test thoroughly (fresh install, existing install, Tarkov/patcher cases, Fika, self-update)
5. Open a pull request

## License

MIT — see [LICENSE](LICENSE).

## Acknowledgments

- SPT team and community
- Fika co-op team
- Everyone who’s filed issues and feedback on this launcher

## Support

1. Check [Issues](https://github.com/bblair321/spt-launcher/issues)
2. Search for an existing report
3. Open a new issue with OS, launcher version, SPT version, and steps to reproduce
