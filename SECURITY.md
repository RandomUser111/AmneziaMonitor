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
