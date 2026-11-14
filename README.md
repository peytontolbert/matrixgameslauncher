# Matrix Games Launcher

An open-source Windows launcher for **game mods and online servers**.

Choose a game, find a server, and join. Each game has its own integration for installation detection, saved settings, required content, and launching multiplayer sessions. Assetto Corsa is the first integration; GTA, Halo, and other games can be added through separate adapters.

## Status

The [hosted server directory](https://matrixgames.app/servers) and launcher account API are live. Download the [Windows launcher](https://matrixgames.app/driving/MatrixGamesLauncher.exe), or build it from source below. Assetto Corsa is the released integration. This development branch adds a separate Matrix GTA client; it has not been released or validated inside GTA yet. Halo remains planned.

The GTA preview targets the Steam edition of GTA V Legacy 1.0.3889.0. Join starts GTA Story Mode through Steam and connects a Matrix networking plugin to the selected Matrix server. It does not use FiveM or GTA Online. The initial shared session covers Legion Square, player/vehicle poses and chat; collisions, shared seats, combat and voice are disabled. The native game's ownership and login checks still apply.

On first join, the launcher reviews missing script-runtime files, downloads pinned versions from the authors, verifies SHA-256, backs up replacements, and installs its bundled Matrix plugin. Script Hook V is downloaded directly from its author and is not redistributed. Runtime archives are cached in the selected Matrix library. Game map assets come from the user's GTA installation. GTA is launched with Story Mode and mod-compatible launch flags for this session; game executables and account settings are not replaced.

## How joining works

1. Set your Steam folder, Matrix Games library folder, and the selected game's installation and documents folders once. Assetto Corsa can be found across the selected Steam folder's library list. An optional existing mods folder points to content you already downloaded elsewhere.
2. Sign in through the Matrix Games website using Google, then approve the code shown by the launcher. Your Google password and Google tokens never enter the launcher.
3. Click **Join** on an online server in the launcher or [matrixgames.app/servers](https://matrixgames.app/servers). The server list shows the game, track and offered cars. For Assetto Corsa, the launcher then opens a car chooser with the complete required-asset list, installed/reusable/download status, and actual download size.
4. The launcher checks the server's required content against files already installed. Matching files are reused in place. It also checks known content layouts in your saved installation, documents, mods, and library folders; compatible folders can be linked without copying, and verified existing archives can be installed without downloading again.
5. If content needs installation or linking, review the exact local sources or server owner's download URLs, archive hashes, actual download size, and game destinations. Failed installed-file checks explain what differs. Approve the installation to continue.
6. The launcher downloads and verifies the approved content, links it into the game, obtains a fresh join authorization, and starts the game.

Steam and the game still verify ownership. You need a legitimate installation and any prerequisites required by the server, including Custom Shaders Patch when applicable. The launcher does not install Steam, CSP, paid DLC, or base games.

## Your library, each server's assets

**The launcher does not supply a CDN or bundle game assets.** Server owners provide manifests with their own HTTPS download URLs, archive SHA-256 hashes, expanded sizes, and installed-file checks. Owners are responsible for permission to distribute their content.

Choose a local NTFS library folder, for example `D:\MatrixGames`:

```text
MatrixGames/
  objects/<archive-sha256>/<content-root-key>/content/  # shared extracted content
  servers/<server-id>/                              # approvals and asset references
  downloads/                                       # resumable partial downloads
  staging/                                         # temporary extraction
```

The main window shows the active library. Use **Settings / library** to select another folder or a drive root such as `K:\`. Changing it controls where new assets are installed; previous libraries remain available for reuse and existing game links stay intact. Files are not moved automatically.

Matching content in an existing game installation is left in place. Missing content is stored once in the library and connected to the game's normal content folder using Windows directory junctions. Servers requiring the same archive and content root reuse the same payload. Completed compressed downloads are removed; interrupted downloads can resume.

The optional existing mods folder can contain `content/cars/<id>`, `content/tracks/<id>`, `cars/<id>`, `tracks/<id>`, or `<id>` directly. It can also be an individual asset folder. Archives named `<id>.7z`, `<id>.zip`, or `<id>.tar` are checked directly in selected roots, their `cars`/`tracks` folder, or their `downloads` folder. The launcher checks declared file requirements and archive hashes; it does not recursively scan whole disks or trust folder names alone. Keep externally linked source folders in place. Local archives are retained, and a changed local source stops installation instead of silently starting a network download.

When a server requires a different version of a content folder, the previous folder is moved to `.matrixgames-backups` in the game installation before a new link is created. Backups are retained. Do not delete library objects that your games still link to. Automatic library garbage collection, uninstall, and repair are not implemented yet. Modified cached content is preserved and joining stops with an explanation.

## Build and run

Requirements: 64-bit Windows with .NET Framework 4.8 and the Windows `tar.exe` tool supporting the archive format supplied by the server. Content linking needs a local NTFS volume; administrator access is not normally required.

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\build.ps1 -Test
.\build\MatrixGamesLauncher.exe
```

To include the GTA plugin, obtain the official ScriptHookVDotNet v3.7.0-nightly.204 SDK and pass its extracted directory with `-GtaSdkDirectory`. CI verifies its archive SHA-256 before compiling `native/gta/MatrixGtaClient.cs` and embedding the result. No game SDK binaries are committed. The normal build without that parameter remains usable for Assetto and explains that its GTA plugin is absent if a GTA join is requested.

Open **Settings**, save your folders, and select **Enable browser joins**. This copies the executable to `%LocalAppData%\MatrixGames\Launcher` and registers `matrixgames://` links for your Windows account. Updating the executable requires running the new build and enabling browser joins again. Automatic updates and a signed installer are not included in this first version.

Settings are stored in that same directory. The account session is protected using Windows DPAPI for the current Windows user. Sign out to revoke the device's current session. Browser joins carry a supported game ID and server ID; they never carry executable paths or arbitrary commands.

## Host a server

Run your own supported game server and register it with Matrix Games. Registration requires a Matrix Games account, connection details, and an asset manifest. A private heartbeat token keeps the listing current. Hosting is separate from listing: Matrix Games Launcher does not provision game servers or host their downloads.

See [server integration](docs/server-integration.md) for the API contract and manifest format, and [adding games](docs/adding-games.md) for adapter boundaries.

## Development and tests

`build.ps1 -Test` compiles the launcher and runs filesystem, archive, junction, reuse, backup, settings, protocol, and launch-validation tests. Tests create unique fixtures under the output directory and retain them for inspection. They never launch a real game or alter an installed game. Do not recursively delete test folders without checking their junctions.

Network services and game servers are separate deployments. This repository contains the desktop client, public integration documentation, and build workflow. It contains no account-service credentials, game archives, or server-specific CDN catalog.

## License

Launcher source is licensed under the [MIT License](LICENSE). Game software, mods, server content, and third-party trademarks retain their respective owners' rights.
