# GOAT CLIENT

**Built for the next level.**

GOAT CLIENT ist ein eigener Minecraft Client & Launcher für Windows. Dieses Repository enthält **Phase 1: die professionelle Launcher-Basis**.

> **Wichtig:** Phase 1 enthält noch **keinen vollständigen Minecraft-Launcher**. Es gibt noch keinen Microsoft-Login, keinen Download, keine Installation und keinen Start von Minecraft. Die UI sagt das an jeder betroffenen Stelle ehrlich.

---

## Features (Phase 1)

| Bereich | Status |
|---|---|
| Custom Window (eigene Titelleiste, abgerundete Ecken, Schatten, Min/Max/Restore/Close, Drag, Doppelklick-Maximieren) | implementiert |
| Sidebar-Navigation (Home, Play, Profiles, Skins, Settings, Account) | implementiert |
| Profile: erstellen, bearbeiten, löschen, duplizieren, auswählen | implementiert |
| Profil-Speicherung in `profiles.json` (atomar, beschädigte Dateien werden gesichert) | implementiert |
| Settings (General, Minecraft, Java, Appearance) mit Auto-Save in `settings.json` | implementiert |
| „Launch with Windows“ (HKCU\…\Run, keine Adminrechte) | implementiert |
| Minecraft-Versionsauswahl & Filter (lokale Entwicklungsdaten, alle „Not Installed“) | implementiert |
| RAM-Auswahl mit Erkennung des System-RAMs (ungültige Werte gesperrt) | implementiert |
| Java-Runtime-Architektur (Scan von `runtime\java-*`, Auswahl pro Version) | vorbereitet |
| Status-System, Logging, globale Fehlerbehandlung | implementiert |
| Microsoft Login, Skins, Download, Installation, Launch | **nicht implementiert** (spätere Phasen) |

## Technologien

C# 12 · .NET 8 · WPF · MVVM · keine externen NuGet-Pakete.

## Voraussetzungen

- Windows 10 (1809+) oder Windows 11
- Visual Studio 2022 (17.8 oder neuer) mit Workload **„.NET-Desktopentwicklung“**
- .NET 8 SDK (wird mit Visual Studio installiert)

## Installation & Visual Studio Setup

1. `GoatClient.zip` entpacken.
2. `GoatClient.sln` in Visual Studio 2022 öffnen.
3. `GoatClient` als Startprojekt verwenden (einziges Projekt).
4. **F5** – Build & Start.

## Build (Kommandozeile)

```bash
dotnet restore
dotnet build -c Release
dotnet run --project GoatClient
```

Ausgabe: `GoatClient\bin\Release\net8.0-windows\GoatClient.exe`

## Projektstruktur

```text
GoatClient/
├── GoatClient.sln
├── README.md
└── GoatClient/
    ├── GoatClient.csproj · App.xaml(.cs) · app.manifest
    ├── Assets/Logo/          Offizielles Logo (Original + zugeschnittene Version) + App-Icon (.ico)
    ├── Controls/             SidebarButton, StatCard, AccountCard, ProfileCard
    ├── Core/
    │   ├── Commands/         RelayCommand, AsyncRelayCommand
    │   ├── Constants/        AppPaths, AppInfo
    │   ├── Converters/       Value Converter
    │   ├── Helpers/          JsonFileStore, SystemMemoryInterop, WindowMaximizeHelper
    │   └── ObservableObject.cs
    ├── Models/               LauncherProfile, LauncherSettings, MinecraftVersionInfo, JavaRuntime(Info), LogEntry, …
    ├── Services/
    │   ├── Dialogs/          In-Window-Dialoge + Ordnerauswahl
    │   ├── Future/           Interfaces für spätere Phasen (ohne Implementierung)
    │   ├── Java/             IJavaService, JavaService, JavaRequirements
    │   ├── Logging/          ILogger, FileLogger
    │   ├── Minecraft/        IMinecraftVersionService, Provider (LocalDevelopmentVersionProvider)
    │   ├── Navigation/       INavigationService
    │   ├── Notifications/    Toasts
    │   ├── Platform/         System-RAM, Windows-Autostart, Explorer
    │   ├── Profiles/         IProfileService
    │   ├── Settings/         ISettingsService
    │   ├── Startup/          LauncherBootstrapper (Startsequenz)
    │   └── Status/           IStatusService
    ├── Themes/               Colors.xaml, Icons.xaml, Controls.xaml (alle Styles)
    ├── ViewModels/           Main, Home, Play, Profiles (+Editor/Item), Skins, Settings, Account
    └── Views/                MainWindow + Seiten
```

## AppData-Struktur

```text
%APPDATA%\GoatClient\
├── settings.json
├── profiles.json
├── logs\          goatclient-YYYY-MM-DD.log (14 Tage Aufbewahrung)
└── runtime\       verwaltete Java-Runtimes (java-17, java-21, …)
```

Es werden nur Ordner angelegt, die Phase 1 tatsächlich benutzt. `versions`, `assets`, `libraries` usw. entstehen erst mit der Installationsfunktion.

## Java Runtime Architektur

Ziel: **Der Benutzer installiert nie selbst Java.** GOAT CLIENT verwaltet eigene Runtimes unter `%APPDATA%\GoatClient\runtime\java-<major>\`.

- `JavaRequirements` bildet die Mojang-Vorgaben ab (1.20.5+ → Java 21, 1.18–1.20.4 → Java 17).
- `JavaService.ScanAsync` erkennt Runtimes in diesem Ordner über `bin\javaw.exe` + `release`-Datei. Es wird **keine Version erfunden**.
- Profile haben eine Java-Präferenz (Automatisch / Java 21 / Java 17); `ResolveTargetMajor` wählt die passende Runtime.
- Eine systemweite Java-Installation wird höchstens **informativ** angezeigt und nie vorausgesetzt.
- **Phase 1 lädt nichts herunter.** `SupportsAutomaticProvisioning` ist `false`. Die automatische Bereitstellung (Download, Hash-Prüfung, Updates) folgt über `IDownloadService`/`IMinecraftInstallationService`.

## Sicherheit

- Keine Passwörter, Tokens, API-Keys oder Client-Secrets im Projekt.
- Keine Account-Daten werden gespeichert – es gibt in Phase 1 keinen Login.
- Autostart schreibt nur in `HKEY_CURRENT_USER` (keine Adminrechte, `asInvoker`).

## Nicht implementierte Funktionen

Microsoft Login · Xbox/Minecraft Authentication · Minecraft Download/Installation/Launch · Fabric/Forge/NeoForge · Mods · Cosmetics/Capes · Client-Mods (FPS, CPS, Keystrokes, Waypoints, HUD Editor) · Discord RPC · Replay · Shop.

## Zukünftige Phasen

1. **Phase 2:** Microsoft/Xbox/Minecraft-Login, Skins, Account-Seite.
2. **Phase 3:** Offizielles Versions-Manifest, Downloads, Installation, automatische Java-Bereitstellung, Minecraft-Start.
3. **Danach:** Mod-Loader, GOAT-CLIENT-Features, Updates.
