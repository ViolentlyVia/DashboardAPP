#!/usr/bin/env pwsh
# Build PulsePoint Agent and compile installer
# Requires: .NET 10 SDK, Inno Setup (iscc.exe in PATH or default install location)

$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot

Write-Host ">>> Publishing PulsePoint.Agent..." -ForegroundColor Cyan
dotnet publish "$root\PulsePoint.Agent\PulsePoint.Agent.csproj" `
    -c Release -r win-x64 --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -o "$root\PulsePoint.Agent\bin\Release\net10.0-windows\win-x64\publish"

if ($LASTEXITCODE -ne 0) { Write-Error "dotnet publish failed"; exit 1 }

Write-Host ">>> Compiling installer..." -ForegroundColor Cyan
$iscc = Get-Command iscc -ErrorAction SilentlyContinue
if (-not $iscc) {
    $iscc = "C:\Program Files (x86)\Inno Setup 6\iscc.exe"
    if (-not (Test-Path $iscc)) {
        Write-Error "iscc.exe not found. Install Inno Setup 6 from https://jrsoftware.org/isinfo.php"
        exit 1
    }
}

& $iscc "$root\PulsePoint.Agent.iss"

if ($LASTEXITCODE -eq 0) {
    Write-Host ">>> Done! Installer: $root\Installer\PulsePoint-Agent-Setup.exe" -ForegroundColor Green
} else {
    Write-Error "Inno Setup compile failed"
}
