@REM This script compiles the InnoSetup installer script into a single-file .exe installer.
@REM 
@REM Prerequisites: InnoSetup 6+ must be installed
@REM   Download from: https://jrsoftware.org/isdl.php
@REM
@REM Usage: 
@REM   .\build-installer.bat
@REM
@REM Output:
@REM   Installer\SysDash-EndpointAgent-Setup.exe

@echo off
setlocal enabledelayedexpansion

set INNO_PATH=C:\Program Files (x86)\Inno Setup 6\iscc.exe

if not exist "!INNO_PATH!" (
    echo Error: InnoSetup not found at !INNO_PATH!
    echo.
    echo Please install InnoSetup 6+ from: https://jrsoftware.org/isdl.php
    echo.
    pause
    exit /b 1
)

if not exist "SysDash.EndpointAgent.iss" (
    echo Error: SysDash.EndpointAgent.iss not found in current directory.
    echo Please run this script from the root sysdash folder.
    exit /b 1
)

echo Building SysDash Endpoint Agent installer...
"!INNO_PATH!" "SysDash.EndpointAgent.iss"

if errorlevel 1 (
    echo.
    echo Error: Failed to compile installer.
    pause
    exit /b 1
)

echo.
echo ===================================================
echo Success! Installer created:
echo Installer\SysDash-EndpointAgent-Setup.exe
echo ===================================================
echo.
pause
