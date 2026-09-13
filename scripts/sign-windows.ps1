param(
    [Parameter(Mandatory = $true)]
    [string[]]$Files,
    [switch]$RequireSignature
)

$ErrorActionPreference = "Stop"

function Find-SignTool {
    $roots = @(
        "${env:ProgramFiles(x86)}\Windows Kits\10\bin",
        "$env:ProgramFiles\Windows Kits\10\bin"
    ) | Where-Object { $_ -and (Test-Path $_) }

    foreach ($root in $roots) {
        $candidate = Get-ChildItem -Path $root -Filter signtool.exe -Recurse -ErrorAction SilentlyContinue |
            Where-Object { $_.FullName -match '\\x64\\signtool\.exe$' } |
            Sort-Object FullName -Descending |
            Select-Object -First 1
        if ($candidate) { return $candidate.FullName }
    }

    throw "signtool.exe was not found. Install the Windows SDK signing tools."
}

$base64 = $env:WINDOWS_CERTIFICATE_PFX_BASE64
$password = $env:WINDOWS_CERTIFICATE_PASSWORD

if ([string]::IsNullOrWhiteSpace($base64) -or [string]::IsNullOrWhiteSpace($password)) {
    $message = "Windows signing certificate secrets are not configured. Expected WINDOWS_CERTIFICATE_PFX_BASE64 and WINDOWS_CERTIFICATE_PASSWORD."
    if ($RequireSignature) { throw $message }
    Write-Warning $message
    exit 0
}

$tempRoot = if ([string]::IsNullOrWhiteSpace($env:RUNNER_TEMP)) { [IO.Path]::GetTempPath() } else { $env:RUNNER_TEMP }
$tempPfx = Join-Path $tempRoot "amnezia-monitor-signing.pfx"
try {
    [IO.File]::WriteAllBytes($tempPfx, [Convert]::FromBase64String($base64))
    $signtool = Find-SignTool
    $timestamp = if ([string]::IsNullOrWhiteSpace($env:WINDOWS_TIMESTAMP_URL)) {
        "http://timestamp.digicert.com"
    } else {
        $env:WINDOWS_TIMESTAMP_URL
    }

    foreach ($file in $Files) {
        if (-not (Test-Path $file)) { throw "File to sign was not found: $file" }
        & $signtool sign /fd SHA256 /f $tempPfx /p $password /tr $timestamp /td SHA256 $file
        if ($LASTEXITCODE -ne 0) { throw "Signing failed for $file." }

        & $signtool verify /pa /v $file
        if ($LASTEXITCODE -ne 0) { throw "Signature verification failed for $file." }
    }
}
finally {
    Remove-Item $tempPfx -Force -ErrorAction SilentlyContinue
}
