# Publishing Amnezia Monitor

## Why release 1.4.0 uses compact builds by default

A self-contained .NET desktop application includes the .NET runtime and native platform libraries. Even a small UI application can therefore exceed 100 MB before compression. That size is mostly the runtime, not Amnezia Monitor itself.

The previous release script also bundled everything into one single-file executable. Amnezia Monitor depends on native desktop and SQLite libraries, so single-file extraction adds another startup failure point.

The default 1.4.0 packages are therefore:

- framework-dependent;
- RID-specific;
- not single-file;
- no debug symbols;
- no ReadyToRun duplication.

This produces a much smaller and more predictable package. The target computer must have .NET 10 Runtime installed.

## Windows

```powershell
./scripts/publish-release.ps1
```

Output is written to `dist/`.

For standalone packages that include .NET:

```powershell
./scripts/publish-release.ps1 -Standalone
```

Standalone packages are expected to be much larger.

## Linux

Compact:

```bash
./scripts/publish-release.sh 1.4.0 compact
```

Standalone:

```bash
./scripts/publish-release.sh 1.4.0 standalone
```

## Startup troubleshooting

Amnezia Monitor writes startup milestones and fatal managed exceptions to:

- Windows: `%LOCALAPPDATA%\AmneziaMonitor\startup.log`
- Linux: normally `~/.local/share/AmneziaMonitor/startup.log`

If no startup log is created, verify that .NET 10 Runtime is installed and that the downloaded archive was extracted fully before launch.


For code signing, the Windows installer, Linux native packages and automatic update behavior, see [DISTRIBUTION.md](DISTRIBUTION.md).
