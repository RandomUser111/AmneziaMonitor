param(
    [string]$Version = "1.0.2",
    [switch]$Standalone
)

$ErrorActionPreference = "Stop"
$Root = Split-Path -Parent $PSScriptRoot
$Project = Join-Path $Root "src/AmneziaDashboard.App/AmneziaDashboard.App.csproj"
$Dist = Join-Path $Root "dist"
$PublishRoot = Join-Path $Root "artifacts/publish"

function Assert-NativeCommandSuccess {
    param([string]$Step)

    if ($LASTEXITCODE -ne 0) {
        throw "$Step failed with exit code $LASTEXITCODE."
    }
}

function Write-PackageInfo {
    param(
        [string]$Directory,
        [string]$Rid,
        [bool]$IsStandalone
    )

    $infoPath = Join-Path $Directory "README-RUNTIME.txt"
    if ($IsStandalone) {
        @"
Amnezia Monitor $Version
Platform: $Rid

This is a standalone build. The .NET runtime is included.
The package is larger because it contains the runtime and native platform libraries.

If the application does not start, check:
%LOCALAPPDATA%\AmneziaMonitor\startup.log
"@ | Set-Content -Path $infoPath -Encoding UTF8
    }
    else {
        @"
Amnezia Monitor $Version
Platform: $Rid

This is the compact build.
It requires the .NET 10 Runtime to be installed on the computer.
Download: https://dotnet.microsoft.com/download/dotnet/10.0

Windows: run AmneziaMonitor.exe
Linux: run ./AmneziaMonitor

If the application does not start, check:
Windows: %LOCALAPPDATA%\AmneziaMonitor\startup.log
Linux:   ~/.local/share/AmneziaMonitor/startup.log (location may vary with XDG settings)
"@ | Set-Content -Path $infoPath -Encoding UTF8
    }
}

if (Test-Path $Dist) { Remove-Item $Dist -Recurse -Force }
if (Test-Path $PublishRoot) { Remove-Item $PublishRoot -Recurse -Force }
New-Item -ItemType Directory -Path $Dist | Out-Null
New-Item -ItemType Directory -Path $PublishRoot | Out-Null

$Rids = @("win-x64", "win-arm64", "linux-x64", "linux-arm64")
$ModeName = if ($Standalone) { "standalone" } else { "compact" }
$SelfContainedValue = if ($Standalone) { "true" } else { "false" }

Write-Host "Release mode: $ModeName" -ForegroundColor Yellow
if (-not $Standalone) {
    Write-Host "Compact packages require .NET 10 Runtime on the target machine." -ForegroundColor Yellow
}

Write-Host "Restoring and building the base project..." -ForegroundColor Cyan
dotnet restore $Project
Assert-NativeCommandSuccess "Base restore"

dotnet build $Project -c Release --no-restore -p:Version=$Version
Assert-NativeCommandSuccess "Release build"

foreach ($rid in $Rids) {
    Write-Host "Restoring runtime assets for $rid..." -ForegroundColor Cyan
    dotnet restore $Project -r $rid
    Assert-NativeCommandSuccess "Restore for $rid"

    Write-Host "Publishing $rid ($ModeName)..." -ForegroundColor Cyan
    $out = Join-Path $PublishRoot "$ModeName/$rid"

    dotnet publish $Project `
        -c Release `
        -r $rid `
        --self-contained $SelfContainedValue `
        --no-restore `
        -p:Version=$Version `
        -p:UseAppHost=true `
        -p:PublishSingleFile=false `
        -p:PublishReadyToRun=false `
        -p:DebugType=None `
        -p:DebugSymbols=false `
        -o $out
    Assert-NativeCommandSuccess "Publish for $rid"

    if (-not (Test-Path $out)) {
        throw "Publish directory was not created for ${rid}: $out"
    }

    $publishedFiles = @(Get-ChildItem $out -Force)
    if ($publishedFiles.Count -eq 0) {
        throw "Publish directory is empty for ${rid}: $out"
    }

    Write-PackageInfo -Directory $out -Rid $rid -IsStandalone $Standalone.IsPresent
    Copy-Item (Join-Path $Root "LICENSE") (Join-Path $out "LICENSE") -Force
    Copy-Item (Join-Path $Root "THIRD_PARTY_NOTICES.md") (Join-Path $out "THIRD_PARTY_NOTICES.md") -Force

    $suffix = if ($Standalone) { "-$rid-standalone" } else { "-$rid" }

    if ($rid.StartsWith("win-")) {
        $archive = Join-Path $Dist "AmneziaMonitor-v$Version$suffix.zip"
        Compress-Archive -Path (Join-Path $out "*") -DestinationPath $archive -Force
    }
    else {
        $archive = Join-Path $Dist "AmneziaMonitor-v$Version$suffix.tar.gz"
        Push-Location $out
        try {
            tar -czf $archive .
            Assert-NativeCommandSuccess "Archive for $rid"
        }
        finally {
            Pop-Location
        }
    }
}

Write-Host "Release archives created in: $Dist" -ForegroundColor Green
Get-ChildItem $Dist | Select-Object Name, @{N='SizeMB'; E={[math]::Round($_.Length / 1MB, 1)}}

if (-not $Standalone) {
    Write-Host "Tip: use -Standalone only if you need packages that include the .NET runtime." -ForegroundColor DarkGray
}
