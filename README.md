# Amnezia Monitor

<p align="center">
  <img src="src/AmneziaDashboard.App/Assets/amnezia-monitor-icon.png" width="128" alt="Amnezia Monitor icon" />
</p>

**Amnezia Monitor** is a cross-platform desktop application for monitoring and managing self-hosted Amnezia VPN servers over SSH.

The application is an independent project and is **not affiliated with or endorsed by Amnezia**. The public Amnezia source code and documentation are used only as compatibility references.

> Version: **1.0.2**  
> Platforms: **Windows / Linux**  
> License: **GPL-3.0**

## Features

### Server monitoring

- Multiple saved VPS/server profiles.
- Quick switching between servers.
- Optional auto-connect on startup.
- CPU, RAM, disk usage and uptime.
- Network RX/TX speed with automatic refresh.
- Automatic discovery of Amnezia Docker containers.
- Start / Stop / Restart container operations.

### VPN clients

Detailed client management currently targets **WireGuard and AmneziaWG**:

- peer discovery;
- online/offline state based on latest handshake;
- VPN IP, endpoint, transferred data and live speed;
- search and online/offline filters;
- rename through Amnezia `clientsTable`;
- revoke access with server-side backup and rollback;
- create a new client;
- automatic free IPv4 allocation;
- export a ready `.conf` profile;
- QR code for phone import;
- recover a lost client configuration by rotating that client's key pair while preserving its name and VPN IP.

### History and diagnostics

- Local SQLite monitoring history.
- 1 hour / 6 hours / 24 hours / 7 days views.
- CPU/RAM, network speed and online-client charts.
- Interactive chart values and mouse-wheel zoom.
- Application event log with search and filters.
- Docker log viewer for containers whose logging driver supports `docker logs`.

Official Amnezia containers are often launched with Docker `--log-driver none`; in that case Docker logs do not exist and Amnezia Monitor reports this as an informational limitation rather than an application error.

### Desktop experience

- Light, dark and system themes.
- Theme preference persistence.
- Native application icon.
- Secure password storage:
  - **Windows:** Windows Credential Manager;
  - **Linux:** Secret Service through `secret-tool` / libsecret.

SSH passwords are never stored in `servers.json`.

## Screens / sections

The main application sections are:

- **Overview** — current server and VPN state;
- **History** — stored monitoring charts;
- **Servers** — saved VPS profiles and switching;
- **Clients** — WireGuard / AmneziaWG peers;
- **Protocols** — detected Amnezia containers and Docker controls;
- **Journal** — local application events and Docker logs;
- **Settings** — appearance and local-storage information.

## Download

Download the archive for your platform from the GitHub **Releases** page.

The primary release packages are **compact framework-dependent builds**. This keeps the application package much smaller and avoids the native-library extraction issues that can occur with single-file bundles.

Install the **.NET 10 Runtime** on the target computer before launching Amnezia Monitor. Optional standalone builds can still be created locally if a machine must run without a separately installed runtime.

Expected release assets:

| Platform | Archive |
|---|---|
| Windows x64 | `AmneziaMonitor-v1.0.2-win-x64.zip` |
| Windows ARM64 | `AmneziaMonitor-v1.0.2-win-arm64.zip` |
| Linux x64 | `AmneziaMonitor-v1.0.2-linux-x64.tar.gz` |
| Linux ARM64 | `AmneziaMonitor-v1.0.2-linux-arm64.tar.gz` |

### Windows

1. Install .NET 10 Runtime if it is not already installed.
2. Extract the ZIP archive completely. Do not run the EXE directly from inside the ZIP.
3. Run `AmneziaMonitor.exe`.
3. Windows SmartScreen may warn about an unsigned application until code signing is configured for future releases.

### Linux

1. Install .NET 10 Runtime.
2. Extract the archive.
3. Make the executable runnable if necessary:

```bash
chmod +x AmneziaMonitor
```

4. Run:

```bash
./AmneziaMonitor
```

For secure SSH password storage, install the package that provides `secret-tool`. On Debian/Ubuntu this is commonly:

```bash
sudo apt install libsecret-tools
```

The desktop session must also provide a compatible Secret Service implementation (for example GNOME Keyring or KDE Wallet integration).

## Startup diagnostics

Release 1.0.2 writes a small startup log so silent startup failures can be diagnosed.

- Windows: `%LOCALAPPDATA%\AmneziaMonitor\startup.log`
- Linux: usually `~/.local/share/AmneziaMonitor/startup.log`

If the process starts but the window does not appear, attach this file to a bug report. If the log file is not created at all, the problem occurred before managed application startup (for example a missing .NET runtime or OS loader failure).

## Server requirements

- Linux VPS/server accessible by SSH.
- Docker installation used by self-hosted Amnezia VPN.
- SSH account with permission to run required Docker commands. The application falls back to `sudo docker` where supported.

Amnezia Monitor performs real server-side configuration changes for client creation, revocation, recovery and container operations. Use a server account you control and keep independent backups of important VPN configurations.

## Local data

Non-secret application data is stored under the OS application-data directory in an `AmneziaMonitor` folder.

Typical files include:

- `servers.json` — saved server profiles, without SSH passwords;
- `settings.json` — UI preferences such as theme;
- `monitoring.db` — SQLite monitoring history;
- `events.jsonl` — local event journal.

Monitoring history older than the configured retention period is cleaned automatically by the application.

## Security model

- SSH passwords are stored only when the user explicitly enables secure storage.
- Passwords are kept in the operating system's credential store, not in project JSON files.
- Newly generated client private keys are not retained by Amnezia Monitor after the configuration window is closed.
- A lost WireGuard/AmneziaWG private key cannot be reconstructed from the server. The recovery operation therefore rotates only that client's key pair and generates a replacement profile.
- Client-changing operations create server-side backups and use rollback where implemented.

See [SECURITY.md](SECURITY.md) for reporting security issues.

## Build from source

### Requirements

- .NET SDK 10.x
- Windows 10/11 or a modern Linux distribution

Clone the repository, then:

```bash
dotnet restore
dotnet build -c Release
dotnet run --project src/AmneziaDashboard.App -c Release
```

The internal project/namespace names still use `AmneziaDashboard.*` for compatibility with the development history; the product and published executable are named **Amnezia Monitor**.

## Create release binaries locally

### Windows PowerShell

```powershell
./scripts/publish-release.ps1
```

### Linux/macOS shell

```bash
chmod +x scripts/publish-release.sh
./scripts/publish-release.sh
```

By default, the scripts publish compact framework-dependent builds for:

- `win-x64`
- `win-arm64`
- `linux-x64`
- `linux-arm64`

and place packaged archives in `dist/`.

To create the larger standalone packages with the .NET runtime included:

```powershell
./scripts/publish-release.ps1 -Standalone
```

or on Linux:

```bash
./scripts/publish-release.sh 1.0.2 standalone
```

Standalone output is intentionally larger because it contains the .NET runtime and platform native libraries. It is not the recommended default GitHub download.

## GitHub Actions

Two workflows are included:

- **Build** — restores and builds the solution on Windows and Linux for pushes and pull requests.
- **Release** — when a tag such as `v1.0.2` is pushed, builds all four compact platform archives and attaches them to a GitHub Release.

Example release tag:

```bash
git tag v1.0.2
git push origin v1.0.2
```

## Project structure

```text
src/
  AmneziaDashboard.App/            Avalonia UI and view models
  AmneziaDashboard.Core/           models and interfaces
  AmneziaDashboard.Infrastructure/ SSH, Docker, storage and security
scripts/                            local release scripts
.github/workflows/                  CI and release automation
```

## Current scope / limitations

Version 1.0 intentionally focuses on reliable server monitoring and WireGuard/AmneziaWG management.

Not yet implemented as full management features:

- OpenVPN client creation/revocation;
- XRay client creation/revocation;
- SSH public-key authentication UI;
- full-server Amnezia backup/restore wizard;
- tray/background notifications;
- automatic application updates;
- signed Windows binaries and packaged Linux `.deb` / `.rpm` installers.

These are suitable post-1.0 features rather than blockers for the first stable release. See [ROADMAP.md](ROADMAP.md).

## Compatibility note

The server-side layout and behavior of Amnezia may change between upstream releases. Amnezia Monitor does not copy upstream implementation code; it independently communicates with SSH, Docker, WireGuard/AmneziaWG and compatible Amnezia configuration files.

If an upstream Amnezia update changes container names, paths or formats, compatibility updates may be required here as well.

## License

Amnezia Monitor is distributed under the **GNU General Public License v3.0**. See [LICENSE](LICENSE).

Third-party dependencies retain their own licenses. See [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md).

---

## Русский

Amnezia Monitor — настольная программа для мониторинга и управления собственными серверами Amnezia VPN через SSH. Версия 1.0 поддерживает несколько серверов, мониторинг ресурсов и трафика, историю, управление Docker-контейнерами, а также расширенное управление клиентами WireGuard/AmneziaWG с созданием `.conf` и QR-кодов.

Для обычного использования скачайте компактный архив со страницы **Releases** и установите .NET 10 Runtime. Большие standalone-сборки с включённым runtime можно собрать отдельно.
