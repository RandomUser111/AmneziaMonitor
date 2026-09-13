# Architecture

Amnezia Monitor is split into three .NET projects.

## Core

`AmneziaDashboard.Core`

Contains models and interfaces that describe server connections, monitoring snapshots, VPN peers, client operations, storage and protocol-management contracts.

## Infrastructure

`AmneziaDashboard.Infrastructure`

Contains implementations for:

- SSH connections through SSH.NET;
- Docker container discovery and control;
- WireGuard/AmneziaWG client operations;
- SQLite monitoring history;
- JSON server-profile persistence;
- operating-system credential stores.

The application talks to self-hosted Amnezia servers primarily through SSH and Docker commands. It does not require a custom agent or web API on the server.

## App

`AmneziaDashboard.App`

Avalonia desktop UI using MVVM patterns. It contains views, view models, local event logging, theme preferences and desktop-specific presentation behavior.

## Data flow

```text
Avalonia UI
    ↓
ViewModels
    ↓
Core interfaces
    ↓
Infrastructure services
    ↓
SSH → Docker / wg / awg / server configuration
```

Monitoring data is sampled periodically and optionally persisted to local SQLite for history charts.
