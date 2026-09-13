# Changelog

All notable changes to Amnezia Monitor are documented in this file.

## [1.4.0] - 2026-09-13

### Added
- Automatic stable-release update checks using the public GitHub Releases API.
- Persistent automatic-update preference, manual update check, update banner and release-page action.
- Windows Authenticode signing helper and GitHub Actions signing integration.
- Per-user Windows x64 installer generated with Inno Setup.
- Debian `.deb` packages for amd64/arm64 and RPM packages for x86_64/aarch64.
- Linux desktop entry, application icon and `/usr/bin/amnezia-monitor` launcher in native packages.
- Distribution documentation covering signing secrets, installer generation, Linux packages and release procedure.

### Changed
- GitHub Release workflow now publishes portable archives, the Windows installer and Linux native packages in one release.
- Release version metadata updated to 1.4.0.

### Security
- Windows signing helper deletes the temporary PFX after signing and verifies Authenticode signatures with `signtool verify`.
- Automatic update checking sends no account credentials, device identifiers or telemetry.

## [1.3.0] - 2026-09-13

### Added
- Per-client VPN traffic accounting stored in the local SQLite monitoring database.
- Dedicated **Client traffic** page with 1 hour, 6 hour, 24 hour, 7 day and 30 day ranges.
- Download, upload, total usage and traffic share for every WireGuard / AmneziaWG client.
- Search by client name, VPN IP, protocol or public key.
- Traffic history survives client renaming and configuration-key recovery when the VPN IP remains unchanged.

### Changed
- Local monitoring-history retention increased to 90 days.
- Client traffic is sampled approximately every 30 seconds and counter resets after container/interface restarts are handled without producing negative usage.

### Notes
- Per-client historical usage starts accumulating after 1.3.0 is installed; earlier aggregate monitoring data cannot be split retroactively by client.

## [1.2.0] - 2026-09-13

### Added
- Cross-platform system tray integration with open/exit actions and background monitoring.
- Optional minimize-to-tray and close-to-tray behavior.
- Desktop notifications for lost/restored SSH connectivity, VPN container state changes and protocol-operation errors.
- Persistent notification/tray preferences.
- Backup & migration page.
- Portable `.ambackup` full-server backup containing `/opt/amnezia`, Docker container/network metadata, named volumes and required Docker images.
- Restore/migration workflow that restores configuration, keys, users, VPN subnets, volumes, networks and containers on another server.
- Target-side safety archive and best-effort rollback on restore failure.

### Notes
- Existing client profiles can remain unchanged after migration only when their endpoint remains valid (same floating/public IP or an unchanged DNS hostname updated to the new server). Profiles containing the old literal server IP cannot automatically discover the new IP.

## [1.1.0] - 2026-09-13

### Added
- English and Russian interface localization.
- English is the default language for new installations and existing settings without a language preference.
- Language selector in Settings with immediate switching.
- Persistent language preference in `settings.json`.

### Changed
- Runtime status messages, client state text, history summaries and event-log labels now follow the selected interface language.
- Release metadata and packaging defaults updated for 1.1.0.

## [1.0.2] - 2026-09-13

### Fixed
- Reworked release publishing to avoid single-file native extraction issues.
- Added startup diagnostics log for silent launch failures.

### Changed
- GitHub release packages are compact framework-dependent builds by default.
- Standalone packages remain available through the release scripts as an optional mode.
- Reduced bundled image asset size and excluded debug diagnostics from Release dependencies.

## [1.0.0] - 2026-09-13

First stable release.

### Added

- Multi-server profile manager and quick server switching.
- Optional secure SSH password storage using Windows Credential Manager or Linux Secret Service.
- Optional automatic connection on startup.
- Server CPU, RAM, disk, uptime and network monitoring.
- Amnezia Docker-container discovery and Start/Stop/Restart actions.
- WireGuard/AmneziaWG peer monitoring with handshake, traffic and live speeds.
- Client search and status filters.
- Client rename, safe revoke, creation and configuration recovery.
- `.conf` export and QR-code generation.
- SQLite monitoring history and interactive charts.
- Persistent light/dark/system theme preference.
- Application event journal.
- Docker logs viewer with graceful handling of Amnezia containers using `--log-driver none`.
- GitHub CI and release workflows for Windows/Linux x64/ARM64 self-contained builds.

### Security

- SSH passwords are not stored in `servers.json`.
- Generated client private keys are not persisted by the application.
- Destructive peer operations use server-side configuration backups and rollback where supported.
