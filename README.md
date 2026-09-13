# Amnezia Monitor

<p align="center">
  <img src="src/AmneziaDashboard.App/Assets/amnezia-monitor-icon.png" width="128" alt="Amnezia Monitor icon" />
</p>

**Amnezia Monitor** is a cross-platform desktop application for monitoring and managing self-hosted Amnezia VPN servers over SSH.

The application is an independent project and is **not affiliated with or endorsed by Amnezia**. The public Amnezia source code and documentation are used only as compatibility references.

> Version: **1.4.0**  
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

### Per-client traffic statistics

Amnezia Monitor records WireGuard / AmneziaWG traffic-counter deltas for every client approximately every 30 seconds. The **Client traffic** page can show usage for the last 1 hour, 6 hours, 24 hours, 7 days, or 30 days.

For each client it displays:

- downloaded traffic;
- uploaded traffic;
- total traffic;
- share of all VPN client traffic in the selected period;
- protocol and VPN IP;
- search by name, VPN IP, protocol, or public key.

Statistics are stored locally in `monitoring.db`. Per-client history starts accumulating after version 1.3.0 is installed; older aggregate history cannot be reconstructed per client.

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
- English and Russian interface; English is used by default.
- Language can be switched instantly in Settings and the preference is saved between launches.
- Native application icon.
- System tray mode with background monitoring.
- Desktop notifications for SSH disconnect/reconnect events and VPN container state changes.
- Automatic stable-release update checks with a persistent preference and GitHub Release link.
- Secure password storage:
  - **Windows:** Windows Credential Manager;
  - **Linux:** Secret Service through `secret-tool` / libsecret.

SSH passwords are never stored in `servers.json`.

## Screens / sections

The main application sections are:

- **Overview** — current server and VPN state;
- **History** — stored monitoring charts;
- **Client traffic** — per-client usage statistics by period;
- **Servers** — saved VPS profiles and switching;
- **Clients** — WireGuard / AmneziaWG peers;
- **Protocols** — detected Amnezia containers and Docker controls;
- **Journal** — local application events and Docker logs;
- **Backup & migration** — portable full backup and restore/migration workflow;
- **Settings** — appearance, interface language, tray and notification options.

## Download

Download the archive for your platform from the GitHub **Releases** page.

The primary release packages are **compact framework-dependent builds**. This keeps the application package much smaller and avoids the native-library extraction issues that can occur with single-file bundles.

Install the **.NET 10 Runtime** on the target computer before launching Amnezia Monitor. Optional standalone builds can still be created locally if a machine must run without a separately installed runtime.

Expected release assets:

| Platform | Archive |
|---|---|
| Windows x64 | `AmneziaMonitor-v1.4.0-win-x64.zip` |
| Windows ARM64 | `AmneziaMonitor-v1.4.0-win-arm64.zip` |
| Linux x64 | `AmneziaMonitor-v1.4.0-linux-x64.tar.gz` |
| Linux ARM64 | `AmneziaMonitor-v1.4.0-linux-arm64.tar.gz` |
| Windows x64 installer | `AmneziaMonitor-v1.4.0-win-x64-setup.exe` |
| Debian/Ubuntu x64 | `amnezia-monitor_1.4.0_amd64.deb` |
| Debian/Ubuntu ARM64 | `amnezia-monitor_1.4.0_arm64.deb` |
| RPM x64 | `amnezia-monitor-1.4.0-1.x86_64.rpm` |
| RPM ARM64 | `amnezia-monitor-1.4.0-1.aarch64.rpm` |

### Windows

1. Install .NET 10 Runtime if it is not already installed.
2. Extract the ZIP archive completely. Do not run the EXE directly from inside the ZIP.
3. Run `AmneziaMonitor.exe`.
3. Prefer the signed installer when available. Windows release executables are Authenticode-signed when repository signing credentials are configured.

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

## Distribution and updates

Version 1.4.0 adds a complete distribution pipeline:

- Authenticode signing support for Windows application and installer executables;
- per-user Windows x64 installer generated with Inno Setup;
- `.deb` packages for amd64/arm64;
- `.rpm` packages for x86_64/aarch64;
- automatic update checks against the latest stable GitHub Release;
- update banner and manual **Check now** action in Settings.

Windows signing requires a trusted code-signing certificate configured through GitHub Actions secrets. See [docs/DISTRIBUTION.md](docs/DISTRIBUTION.md) for the signing secrets, package layout, local installer build instructions and release procedure.

The application only checks release metadata and opens the release page. It does not silently download or execute updates.

## Startup diagnostics

Release 1.4.0 writes a small startup log so silent startup failures can be diagnosed.

- Windows: `%LOCALAPPDATA%\AmneziaMonitor\startup.log`
- Linux: usually `~/.local/share/AmneziaMonitor/startup.log`

If the process starts but the window does not appear, attach this file to a bug report. If the log file is not created at all, the problem occurred before managed application startup (for example a missing .NET runtime or OS loader failure).

## Full backup and server migration

Version 1.2.0 added a portable full-backup workflow. A backup contains:

- `/opt/amnezia` configuration and keys;
- Amnezia Docker container metadata;
- custom Docker network metadata;
- named Docker volumes used by Amnezia containers;
- Docker images required to recreate the backed-up containers.

Backups use the `.ambackup` extension and are stored on the local computer. Restore uploads the backup to the currently connected target server, restores the Amnezia configuration, volumes and images, recreates networks/containers, verifies the expected containers and performs a best-effort rollback if restoration fails. A target-side safety archive of the previous `/opt/amnezia` is created before destructive changes.

> **Endpoint limitation:** preserving all server keys, client identities, VPN addresses and ports is not enough to make an old client configuration discover a different public IP. Existing client configs continue to work without changes only when the endpoint remains valid — for example, the same floating/public IP is moved to the new VPS, or the client config already uses a DNS name and that DNS record is updated to the new server. If a client profile contains the old literal IP address, that endpoint must be changed on the client.

For the safest migration, restore to a clean Linux server with Docker installed and SSH/root (or passwordless sudo) access.

## Server requirements

- Linux VPS/server accessible by SSH.
- Docker installation used by self-hosted Amnezia VPN.
- SSH account with permission to run required Docker commands. The application falls back to `sudo docker` where supported.

Amnezia Monitor performs real server-side configuration changes for client creation, revocation, recovery and container operations. Use a server account you control and keep independent backups of important VPN configurations.

## Local data

Non-secret application data is stored under the OS application-data directory in an `AmneziaMonitor` folder.

Typical files include:

- `servers.json` — saved server profiles, without SSH passwords;
- `settings.json` — UI preferences such as theme and language;
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
./scripts/publish-release.sh 1.4.0 standalone
```

Standalone output is intentionally larger because it contains the .NET runtime and platform native libraries. It is not the recommended default GitHub download.

## GitHub Actions

Two workflows are included:

- **Build** — restores and builds the solution on Windows and Linux for pushes and pull requests.
- **Release** — when a tag such as `v1.4.0` is pushed, builds portable archives, the Windows x64 installer, Linux `.deb`/`.rpm` packages, and attaches them to a GitHub Release.

Example release tag:

```bash
git tag v1.4.0
git push origin v1.4.0
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

Version 1.4 includes multi-server monitoring, WireGuard/AmneziaWG client management, per-client traffic statistics, system tray/background monitoring, desktop notifications, portable full-server backup/migration, packaged Windows/Linux distribution, and automatic update checks.

Not yet implemented as full management features:

- OpenVPN client creation/revocation;
- XRay client creation/revocation;
- SSH public-key authentication UI;
- configurable alert thresholds and notification rules.

See [ROADMAP.md](ROADMAP.md).

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
