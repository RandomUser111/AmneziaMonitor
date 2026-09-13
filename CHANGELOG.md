# Changelog

All notable changes to Amnezia Monitor are documented in this file.

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
