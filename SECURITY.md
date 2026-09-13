# Security Policy

## Supported version

Security fixes are currently targeted at the latest stable release of Amnezia Monitor.

## Reporting a vulnerability

Please do not publish credentials, private VPN keys, server addresses that should remain private, or exploitable security details in a public issue.

Report security problems privately to the repository maintainer through an appropriate private contact channel or GitHub private vulnerability reporting if it is enabled for the repository.

Include:

- affected Amnezia Monitor version;
- operating system;
- concise reproduction steps;
- expected and observed behavior;
- logs with passwords, private keys, tokens and private infrastructure details removed.

## Secrets handled by the application

- SSH passwords are never written to `servers.json`.
- Password persistence is opt-in and uses Windows Credential Manager or Linux Secret Service.
- Newly generated WireGuard/AmneziaWG private keys are intentionally not retained after the user exports/copies/scans the client profile.
- Event logs should not contain passwords or private VPN keys. Please still review logs before sharing them publicly.

## Server-side operations

Amnezia Monitor can modify VPN peer configuration and control Docker containers on servers the user connects to. Use least-privilege SSH credentials where practical and maintain independent backups.

## Full backup files

`.ambackup` files created by Amnezia Monitor contain sensitive server-side VPN material, including private keys and configuration data from `/opt/amnezia`, Docker volumes, container metadata and images. Treat these files like server root credentials:

- store them only on trusted encrypted storage;
- do not upload them to issues, public cloud links or source repositories;
- delete obsolete copies securely where practical;
- verify the generated `.sha256` sidecar before restore.

Version 1.2.0 does not encrypt `.ambackup` files itself. Backup encryption is planned for a later release.

## Release signing and update checks

Windows code-signing credentials must be stored only as GitHub Actions secrets. Never commit a PFX file or signing password to the repository. The release workflow materializes the PFX only in the temporary runner directory, deletes it after signing, timestamps signatures, and verifies signed files with `signtool`.

Automatic update checking queries only the public GitHub Releases API for `RandomUser111/AmneziaMonitor`. It does not send stored server profiles, SSH credentials, VPN keys, traffic statistics, or other application data.
