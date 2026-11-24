#!/usr/bin/env pwsh
# Builds Tammoa into a distributable Windows installer (.exe via Inno Setup).
#
# Usage: pwsh scripts/package-windows.ps1
# Must run on an actual Windows host (or the windows-latest GitHub Actions
# runner) - Inno Setup's ISCC.exe has no macOS/Linux build. Output lands in
# Deployed/Windows/.
#
# This does NOT code-sign the installer or the exe - unsigned builds trigger
# Windows SmartScreen on first run. Same already-accepted tradeoff as the
# macOS build's ad-hoc signing (see package-macos.sh).

$ErrorActionPreference = "Stop"

$RootDir = Resolve-Path (Join-Path $PSScriptRoot "..")
$AppProjectDir = Join-Path $RootDir "Tammoa"
$UiDir = Join-Path $AppProjectDir "UserInterface"
$OutDir = Join-Path $RootDir "Deployed/Windows"
$Rid = "win-x64"

# Single source of truth is Tammoa.csproj's <Version> - read it live so this
# script can never drift from the app's own reported version.
$Version = (dotnet msbuild (Join-Path $AppProjectDir "Tammoa.csproj") -getProperty:Version -nologo).Trim()

Write-Host "==> Building frontend (type-check + build)"
Push-Location $UiDir
try {
    npm run build
    if ($LASTEXITCODE -ne 0) { throw "npm run build failed ($LASTEXITCODE)" }
} finally {
    Pop-Location
}

Write-Host "==> Syncing frontend build into wwwroot"
# Tammoa has no favicon/public files, so copy whatever the build produced instead of naming files.
$Wwwroot = Join-Path $AppProjectDir "wwwroot"
Remove-Item -Recurse -Force (Join-Path $Wwwroot "assets") -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force -Path $Wwwroot | Out-Null
Copy-Item -Path (Join-Path $UiDir "dist/*") -Destination $Wwwroot -Recurse -Force

Write-Host "==> Publishing self-contained .NET app for $Rid"
Push-Location $AppProjectDir
try {
    dotnet publish -c Release -r $Rid --self-contained true `
        -p:PublishSingleFile=true `
        -p:IncludeNativeLibrariesForSelfExtract=true
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed ($LASTEXITCODE)" }
} finally {
    Pop-Location
}

Write-Host "==> Building installer with Inno Setup"
New-Item -ItemType Directory -Force -Path $OutDir | Out-Null

$Iscc = Get-Command "ISCC.exe" -ErrorAction SilentlyContinue
if ($Iscc) {
    $IsccPath = $Iscc.Source
} else {
    $IsccPath = "C:\Program Files (x86)\Inno Setup 6\ISCC.exe"
    if (-not (Test-Path $IsccPath)) {
        throw "ISCC.exe not found on PATH or at the default Inno Setup 6 install location. Install Inno Setup (https://jrsoftware.org/isinfo.php) to build the Windows installer."
    }
}

& $IsccPath "/DAppVersion=$Version" (Join-Path $PSScriptRoot "windows-installer.iss")
if ($LASTEXITCODE -ne 0) {
    throw "ISCC.exe exited with code $LASTEXITCODE"
}

Write-Host "==> Done"
Write-Host "Installer: $OutDir/Tammoa-$Version-win-x64-setup.exe"
