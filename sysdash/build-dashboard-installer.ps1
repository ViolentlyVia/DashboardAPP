$ErrorActionPreference = "Stop"

$innoExe = "C:\Program Files (x86)\Inno Setup 6\iscc.exe"
if (-not (Test-Path $innoExe)) {
    Write-Host "Error: Inno Setup not found at $innoExe" -ForegroundColor Red
    exit 1
}

Write-Host "Publishing SysDash.NetCore (Release, win-x64 self-contained)..." -ForegroundColor Cyan
dotnet publish "SysDash.NetCore\SysDash.NetCore.csproj" -c Release -r win-x64 --self-contained true
if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed"
}

Write-Host "Building dashboard installer..." -ForegroundColor Cyan
& $innoExe "SysDash.Dashboard.iss"
if ($LASTEXITCODE -ne 0) {
    throw "Inno Setup compile failed"
}

Write-Host "Dashboard installer created at Installer\SysDash-Dashboard-Setup.exe" -ForegroundColor Green
