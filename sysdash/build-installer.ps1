# Build SysDash Endpoint Agent Installer
# This script compiles the InnoSetup installer script (SysDash.EndpointAgent.iss) 
# into a single-file .exe installer.
#
# Prerequisites: InnoSetup 6+ must be installed
#   Download from: https://jrsoftware.org/isdl.php
#
# Usage:
#   powershell -ExecutionPolicy Bypass -File build-installer.ps1
#
# Output:
#   Installer\SysDash-EndpointAgent-Setup.exe

$InnoExe = "C:\Program Files (x86)\Inno Setup 6\iscc.exe"

if (-not (Test-Path $InnoExe)) {
    Write-Host "Error: InnoSetup not found at $InnoExe" -ForegroundColor Red
    Write-Host ""
    Write-Host "Please install InnoSetup 6+ from: https://jrsoftware.org/isdl.php" -ForegroundColor Yellow
    exit 1
}

if (-not (Test-Path "SysDash.EndpointAgent.iss")) {
    Write-Host "Error: SysDash.EndpointAgent.iss not found in current directory." -ForegroundColor Red
    Write-Host "Please run this script from the root sysdash folder." -ForegroundColor Yellow
    exit 1
}

Write-Host "Building SysDash Endpoint Agent installer..." -ForegroundColor Cyan
& $InnoExe "SysDash.EndpointAgent.iss"

if ($LASTEXITCODE -ne 0) {
    Write-Host ""
    Write-Host "Error: Failed to compile installer." -ForegroundColor Red
    exit 1
}

Write-Host ""
Write-Host "===================================================" -ForegroundColor Green
Write-Host "Success! Installer created:" -ForegroundColor Green
Write-Host "Installer\SysDash-EndpointAgent-Setup.exe" -ForegroundColor Green
Write-Host "===================================================" -ForegroundColor Green
