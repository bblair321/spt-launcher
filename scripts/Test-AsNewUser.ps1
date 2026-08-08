# Resets launcher settings and starts a fresh "new user" walkthrough.
# Usage (from repo root):
#   powershell -ExecutionPolicy Bypass -File .\scripts\Test-AsNewUser.ps1
#   powershell -ExecutionPolicy Bypass -File .\scripts\Test-AsNewUser.ps1 -NoBuild

[CmdletBinding()]
param(
    [switch]$NoBuild,
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Debug"
)

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot
$settingsDir = Join-Path $env:APPDATA "SPT-Launcher"
$exeCandidates = @(
    (Join-Path $repoRoot "bin\$Configuration\net8.0-windows\win-x64\SPTLauncher.exe"),
    (Join-Path $repoRoot "bin\$Configuration\net8.0-windows\SPTLauncher.exe")
)

Write-Host "Stopping SPTLauncher (if running)..."
Get-Process -Name "SPTLauncher" -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Milliseconds 400

if (Test-Path $settingsDir) {
    Write-Host "Deleting settings: $settingsDir"
    Remove-Item $settingsDir -Recurse -Force
} else {
    Write-Host "No settings folder found (already clean)."
}

if (-not $NoBuild) {
    Write-Host "Building ($Configuration)..."
    Push-Location $repoRoot
    try {
        dotnet build -c $Configuration --nologo
        if ($LASTEXITCODE -ne 0) {
            throw "Build failed with exit code $LASTEXITCODE"
        }
    }
    finally {
        Pop-Location
    }
}

$exe = $exeCandidates | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $exe) {
    throw "Could not find SPTLauncher.exe under bin\$Configuration\net8.0-windows. Build first or omit -NoBuild."
}

Write-Host "Starting as new user (first-run walkthrough):"
Write-Host "  $exe --force-first-run"
Start-Process -FilePath $exe -ArgumentList "--force-first-run" -WorkingDirectory (Split-Path $exe)
