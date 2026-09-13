# Distribution

Amnezia Monitor 1.4.0 adds a complete release-distribution pipeline for Windows and Linux.

## Release assets

A tag such as `v1.4.0` produces:

- `AmneziaMonitor-v1.4.0-win-x64.zip`
- `AmneziaMonitor-v1.4.0-win-arm64.zip`
- `AmneziaMonitor-v1.4.0-linux-x64.tar.gz`
- `AmneziaMonitor-v1.4.0-linux-arm64.tar.gz`
- `AmneziaMonitor-v1.4.0-win-x64-setup.exe`
- `amnezia-monitor_1.4.0_amd64.deb`
- `amnezia-monitor_1.4.0_arm64.deb`
- `amnezia-monitor-1.4.0-1.x86_64.rpm`
- `amnezia-monitor-1.4.0-1.aarch64.rpm`

The portable and installer packages are framework-dependent compact builds and therefore require the .NET 10 Runtime.

## Windows Authenticode signing

The release workflow can Authenticode-sign both the portable application executable and the x64 setup executable.

A trusted code-signing certificate is required. A self-signed certificate is useful only for development and does not establish public trust or remove SmartScreen reputation warnings.

Configure these GitHub Actions secrets:

- `WINDOWS_CERTIFICATE_PFX_BASE64` — Base64 representation of the PFX file.
- `WINDOWS_CERTIFICATE_PASSWORD` — PFX password.

Optional repository variables:

- `WINDOWS_SIGNING_REQUIRED=true` — fail Windows release jobs when signing credentials are missing.
- `WINDOWS_TIMESTAMP_URL` — RFC3161 timestamp server. If omitted, `http://timestamp.digicert.com` is used.

To convert a PFX file to Base64 in PowerShell:

```powershell
$pfx = [IO.File]::ReadAllBytes("C:\path\to\codesign.pfx")
[Convert]::ToBase64String($pfx) | Set-Clipboard
```

Add the copied value as the `WINDOWS_CERTIFICATE_PFX_BASE64` Actions secret. Never commit the PFX file or its password to the repository.

The signing helper verifies every signature with `signtool verify /pa /v` after signing.

## Windows installer

The installer is generated with Inno Setup 6.

It is a per-user installer and installs to:

```text
%LOCALAPPDATA%\Programs\Amnezia Monitor
```

It creates a Start menu shortcut and offers an optional desktop shortcut. It does not require administrator rights.

To build the installer locally on Windows:

```powershell
winget install JRSoftware.InnoSetup
./scripts/publish-release.ps1 -Version 1.4.0 -BuildWindowsInstaller
```

If the signing environment variables are present, the application and installer are signed automatically.

## Debian and RPM packages

Linux packages are produced with `fpm` from the same framework-dependent publish directory used by the portable tarball.

Files are installed to:

```text
/opt/amnezia-monitor
/usr/bin/amnezia-monitor
/usr/share/applications/amnezia-monitor.desktop
/usr/share/icons/hicolor/256x256/apps/amnezia-monitor.png
```

The packages declare a dependency on `dotnet-runtime-10.0`. The distribution must therefore have a package source that provides the .NET 10 runtime (for many distributions this is the Microsoft package repository).

Local package generation requires Ruby and fpm:

```bash
gem install --no-document fpm
./scripts/package-linux.sh 1.4.0 linux-x64 artifacts/publish/compact/linux-x64 dist
```

## Automatic update checks

The application checks the public GitHub Releases API at startup when automatic update checks are enabled.

Behavior:

- English/Russian UI supported.
- The check runs at most once every 12 hours unless the user clicks **Check now**.
- Only the latest stable GitHub Release is considered.
- If a newer version exists, a banner is shown in the main window and a desktop notification may be displayed.
- The update action opens the GitHub Release page; Amnezia Monitor does not silently download or execute installers.
- No GitHub account, authentication token, device identifier, or telemetry is sent.

The preference and last-check timestamp are stored in the normal local `settings.json` file.

## Release procedure

1. Update project version and changelog.
2. Push and verify the normal Build workflow.
3. Create and push a release tag:

```bash
git tag -a v1.4.0 -m "Amnezia Monitor 1.4.0"
git push origin v1.4.0
```

4. Verify all portable, installer, and Linux-package jobs.
5. Verify Authenticode signatures on Windows release assets when signing is enabled.
6. Verify the generated GitHub Release assets before announcing the release.
