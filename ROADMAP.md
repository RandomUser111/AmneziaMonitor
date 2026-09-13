# Roadmap

Version 1.0 is feature-complete for the initial goal: convenient monitoring of self-hosted Amnezia servers plus WireGuard/AmneziaWG client management.

Potential post-1.0 work:

## High value

- System tray mode and background monitoring.
- Desktop notifications for server disconnects, protocol failures and unusual client state changes.
- Full server-side Amnezia configuration backup/restore workflow.
- SSH private-key authentication and agent support.
- Connection diagnostics page: SSH, Docker, ports, DNS, interfaces, routes and protocol health checks.

## Protocol coverage

- OpenVPN client lifecycle management.
- XRay client lifecycle management.
- Improved statistics for non-WireGuard protocols where upstream data is available.

## Distribution

- Signed Windows releases.
- Windows installer (MSIX/installer executable).
- Linux `.deb` and `.rpm` packages.
- Automatic update checking.

## UX

- System tray quick server switch.
- Custom alert thresholds for CPU/RAM/disk and client counts.
- Export/import of non-secret application settings.
- Localization beyond Russian UI where useful.
