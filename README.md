# 🐐 GOAT CLIENT

GOAT CLIENT is a Windows Minecraft: Java Edition launcher (C#, .NET 8, WPF, MVVM).
Phase 2 turns it into a real launcher: Microsoft sign-in, official version list, installation from
Mojang's servers with hash verification, managed Java runtimes and a real Minecraft process start.

> **Status:** The code has not yet been compiled or run by the author of this phase (no Windows/.NET
> environment was available). The first GitHub Actions run is the first real build – see
> [Known limitations](#known-limitations).

---

## Installation (users)

1. Download `GoatClient-Setup.exe` (GitHub Actions artifact / release).
2. Run it – install path defaults to `C:\Program Files\GoatClient`; desktop shortcut is optional, a Start menu entry is always created.
3. Start **GOAT CLIENT** from the desktop or the Start menu.

No .NET runtime and no Java installation are required: the app is published self-contained and
downloads the Java runtime Minecraft needs by itself.

Updating: run a newer `GoatClient-Setup.exe` – the existing installation is detected (fixed AppId) and updated in place.
Uninstalling: *Windows Settings → Apps → Installed apps → GOAT CLIENT → Uninstall*. The uninstaller **asks**
whether `%APPDATA%\GoatClient` (Minecraft files, instances/worlds, settings, logs) should be deleted; the default is *No*.

## Launch modes

GOAT CLIENT has two ways to start Minecraft (*Settings → Launcher → How PLAY starts Minecraft*):

**1. Official Minecraft Launcher (default, recommended)**
PLAY writes a profile **"GOAT CLIENT - <profile name>"** into `%APPDATA%\.minecraft\launcher_profiles.json`
(version, game directory, RAM as `-Xmx`, JVM arguments, GOAT CLIENT logo as icon) and opens the official launcher.
You sign in and press *Play* there; downloads and Java are handled by the official launcher.
No Azure app is needed. This is the same mechanism the Fabric installer uses.

Safety:
- Only profiles whose key starts with `goatclient-` are created, updated or removed. Other profiles are never touched.
- A backup `launcher_profiles.json.goatbackup` is written before every change; the file is replaced atomically.
- The official launcher's account files are never read or modified. GOAT CLIENT never sees your login.

Requirement: the official Minecraft Launcher (minecraft.net installer or Microsoft Store / Xbox app).
If it is already open when you press PLAY, a newly created profile may only appear after restarting it.

**2. Direct launch (advanced)**
GOAT CLIENT installs and starts Minecraft itself (sections below). Needs your own Azure app ID approved for Minecraft.

## Build (developers)

Requirements: Windows 10/11 x64, Visual Studio 2022 17.8+ or the .NET 8 SDK.

```powershell
dotnet restore GoatClient/GoatClient.csproj -r win-x64
dotnet build   GoatClient/GoatClient.csproj -c Release -r win-x64
dotnet publish GoatClient/GoatClient.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=false -o publish
# installer (Inno Setup 6):
& "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe" /DAppVersion=0.2.0 "/DSourceDir=..\publish" installer\GoatClient.iss
```

Output: `artifacts\GoatClient-Setup.exe`.

**Single-file:** deliberately *not* used. WPF single-file needs native-library self-extraction and brings
no benefit when an installer is used anyway; the multi-file self-contained layout is the more stable choice.

## Architecture

```
GoatClient/
├── Core/            ObservableObject, commands, converters, AppPaths, AppInfo/AppLinks
├── Models/          settings, profiles, versions, Java runtimes, transfers, auth models
├── Services/
│   ├── Auth/        MicrosoftAuthService, WindowsCredentialStore, SkinTextureLoader
│   ├── Downloads/   DownloadService (parallel, retry, resume, SHA-1)
│   ├── Integrity/   IntegrityService (SHA-1)
│   ├── Java/        JavaService (managed Mojang runtimes)
│   ├── Minecraft/   MojangVersionManifestProvider, InstalledVersionProvider, MinecraftVersionService,
│   │                VersionManifest (version JSON + rules), MinecraftInstallationService, LocalVersionStore
│   ├── Launch/      MinecraftLauncherService (arguments, process, launch log)
│   ├── Profiles/ Settings/ Logging/ Status/ Navigation/ Notifications/ Dialogs/ Platform/ Startup/
├── ViewModels/      one per page + GameController (shared install/repair/launch workflow)
├── Views/ Controls/ Themes/
installer/           Inno Setup script + wizard images generated from the official logo
.github/workflows/   build → publish → installer → artifacts
```

Services are composed manually in `App.xaml.cs` (no DI container). One shared `HttpClient` is used.

## Microsoft Authentication (direct launch only)

Flow: **Microsoft identity (OAuth 2.0 device code) → Xbox Live → XSTS → Minecraft Services → Minecraft profile.**

1. *Account → Login with Microsoft* shows a code and opens `microsoft.com/link` in the browser (code is copied to the clipboard).
2. After approval, GOAT CLIENT exchanges the tokens and loads the real profile (name, UUID, skin).

Security:
- No password is ever seen or stored.
- Only the Microsoft **refresh token** is persisted, in the **Windows Credential Manager** (`GoatClient/msa-refresh-token/*`).
- Minecraft access tokens live in memory only; they are redacted (`***`) in the launch log and never written to the app log.
- `config\account.json` contains only non-secret display data (name, UUID, skin URL).
- Logout deletes the Credential Manager entries and the cached profile.

**Required one-time setup – Azure application (client) ID.** Microsoft requires every third-party launcher
to use its own app registration; GOAT CLIENT ships none (and no secrets):

1. Azure Portal → *App registrations* → *New registration*; supported account types: **Personal Microsoft accounts** (or "any org + personal").
2. *Authentication* → enable **Allow public client flows** (needed for the device code flow).
3. Apply for Minecraft API access for this app (Mojang/Microsoft review for new third-party apps). Until approved,
   `login_with_xbox` is rejected and the app shows *"Minecraft services rejected the sign-in…"*.
4. Enter the **Application (client) ID** (a GUID) in *Settings → Launcher → Microsoft application (client) ID*.

The client ID is public, not a secret; it is stored in `config\settings.json`.

## Managed Java

- Source: Mojang's official runtime index (`launchermeta.mojang.com/v1/products/java-runtime/…/all.json`), platform `windows-x64`.
- The required version is read from the version JSON (`javaVersion.majorVersion` / `component`) – e.g. Java 21 for 1.20.5+, Java 17 for 1.18–1.20.4. Versions without that field use Mojang's `jre-legacy` (Java 8).
- Installed to `%APPDATA%\GoatClient\runtime\java-<major>\`, every file SHA-1-verified. Multiple runtimes side by side.
- Profiles can force Java 17 / Java 21 or use **Automatic** (recommended).
- *Settings → Java* can install/verify runtimes; *Repair* re-verifies the runtime of the selected profile.

## Minecraft Installation

Source: `piston-meta.mojang.com/mc/game/version_manifest_v2.json` (cached for offline use). Releases are shown;
snapshots can be enabled in *Settings → Minecraft*. Installing a version downloads, with SHA-1 verification:
version JSON, client JAR, libraries (OS rules evaluated), legacy natives, asset index, assets, logging configuration
– and then the Java runtime. Existing valid files are skipped (size check; *Repair* does a full hash check).
Partial downloads are resumed (HTTP range) from the download directory. A marker file
`versions\<id>\.goatclient-installed` is written only after everything succeeded.

## Minecraft Launch

PLAY: refresh sign-in → load installed version JSON → ensure Java → extract legacy natives → build arguments
from `arguments.jvm/game` (or legacy `minecraftArguments`) with rule evaluation and placeholder substitution
(`${auth_player_name}`, `${classpath}`, `${natives_directory}`, …) → `javaw.exe` process in the profile's game directory.
Memory: `-Xmx<profile RAM>`. Process states: Not Running, Starting, Running, Exited, Crashed (exit code ≠ 0).

## Profile System

Profiles (`profiles\profiles.json`): name, Minecraft version, RAM, Java preference, game directory, JVM arguments,
last launch time (set only after a real process start). Installation state and the selected runtime are derived from disk, never stored.
Each profile gets its own instance folder, e.g. `instances\goat-survival-1.21.11`.

## Configuration & AppData

```
%APPDATA%\GoatClient\
├── config\      settings.json, account.json (no secrets)
├── profiles\    profiles.json
├── instances\   one game directory per profile (worlds, options, resource packs)
├── versions\    <id>\<id>.json, <id>.jar, natives\, version_manifest_v2.json (cache)
├── libraries\   Maven-style library tree
├── assets\      indexes\, objects\, virtual\, log_configs\
├── downloads\   partial downloads (*.part)
├── runtime\     java-17\, java-21\, …
└── logs\        goatclient-*.log, minecraft-*.log
```

Phase 1 files (`settings.json`, `profiles.json` in the root) are migrated automatically; profiles that used the data root as game directory get their own instance folder.

## Logs

- `logs\goatclient-*.log` – launcher log.
- `logs\minecraft-<date>.log` – one file per launch: command line (token redacted) and game output. Also shown on the Play page (*VIEW LOG* opens the folder).

## Troubleshooting

| Problem | Solution |
|---|---|
| "Official Minecraft Launcher was not found" | Install it from minecraft.net (or Microsoft Store / Xbox app) and start it once. |
| GOAT profile missing in official launcher | Close the official launcher completely and press PLAY in GOAT CLIENT again. |
| "Microsoft sign-in is not configured" | Enter your Azure client ID (see above). |
| "Minecraft services rejected the sign-in" | The Azure app is not (yet) approved for Minecraft. |
| "no Xbox profile" / "child account" | Create an Xbox profile at xbox.com / add the account to a Microsoft family. |
| "does not own Minecraft: Java Edition" | The account has no Java Edition profile. |
| "Please sign in again" | Refresh token expired or revoked – log in again on the Account page. |
| "Required library is missing" | *Repair Installation* (Play page). |
| Java errors | *Settings → Java → Install / verify Java NN* or *Repair*. |
| Crash (exit code ≠ 0) | Check `logs\minecraft-*.log`; lower RAM or remove custom JVM arguments. |
| Version list empty | No internet on first start – the list is cached after the first successful load. |

## GitHub Actions

`.github/workflows/build.yml` (windows-latest, .NET 8): checkout → setup .NET → restore → build → self-contained
publish (win-x64) → verify output (exe present, no 0-byte files) → Inno Setup → artifacts
**`GoatClient-Setup`** (primary) and **`GoatClient-Portable`** (ZIP).

## Installer

Inno Setup 6 (`installer/GoatClient.iss`): modern wizard with the official logo, English/German,
Program Files install (per-user install selectable), optional desktop shortcut, Start menu shortcut,
upgrade detection, refuses to overwrite while GOAT CLIENT runs (mutex), uninstaller in Windows Apps,
user data deleted only on explicit confirmation.

## Known limitations

- **Not built or tested yet**: restore/build/publish, the installer, sign-in, installation and launch have not been executed
  (no Windows/.NET/network in the development environment). Expect compile fixes in the first CI run.
- Direct launch requires your own Azure app registration **approved for Minecraft**; the default mode does not.
- Official-launcher mode: GOAT CLIENT cannot see whether Minecraft is running or crashed (the official launcher starts the game).
- Starting the Microsoft Store / Xbox-app version of the launcher uses its app ID `Microsoft.4297127D64EC6_8wekyb3d8bbwe!Minecraft` – not tested.
- Vanilla only: no Fabric/Forge/NeoForge, `inheritsFrom` versions are rejected (planned for later phases).
- No offline/demo mode – a Minecraft-owning Microsoft account is required to play.
- Skins are shown read-only; upload comes later. Only a dark theme exists.
- The Discord button uses a neutral chat icon (not the Discord trademark logo).
