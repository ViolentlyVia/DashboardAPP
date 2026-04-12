# SysDash Endpoint Agent - Installer Build Guide

## Quick Start

The installer is built and ready to distribute. Double-click the resulting `.exe` to install on target machines.

## Build Steps

### Option 1: Using PowerShell (Recommended)

```powershell
# Run from the root sysdash folder
powershell -ExecutionPolicy Bypass -File build-installer.ps1
```

### Option 2: Using Command Prompt

```batch
cd c:\Users\Via\Desktop\sysdash
build-installer.bat
```

## Prerequisites

**InnoSetup 6+** must be installed on your build machine:
- Download: https://jrsoftware.org/isdl.php
- Install to default location: `C:\Program Files (x86)\Inno Setup 6`

## Installer Features

When users run `SysDash-EndpointAgent-Setup.exe`, they will:

1. **Configure Server URL** – Prompt for SysDash server address and port
2. **Set Report Interval** – Choose reporting frequency (minimum 5 seconds)
3. **Select Report IP** – Choose auto (primary NIC) or manual selection
4. **Enable Startup** – Option to auto-start agent on Windows login
5. **Complete** – Install to `C:\Program Files\SysDash Endpoint Agent`

## Output Files

- **SysDash-EndpointAgent-Setup.exe** – Single-file installer
- Launch with administrator privileges or UAC will prompt during install

## After Build

The installer is located in the `Installer\` folder:

```
Installer\
  SysDash-EndpointAgent-Setup.exe
```

Share this file with end users. They can simply double-click to install and configure.

## Troubleshooting

**"InnoSetup not found"**
- Ensure InnoSetup 6+ is installed
- Default path is `C:\Program Files (x86)\Inno Setup 6`
- Edit the `.bat` or `.ps1` script if InnoSetup is installed elsewhere

**Installer won't start after first install**
- Restart Windows to allow startup registration to take effect
- Or manually start from Start Menu → SysDash Endpoint Agent

## Uninstall

Users can uninstall via:
- Control Panel → Programs and Features → SysDash Endpoint Agent
- Or the shortcut: Start Menu → SysDash → Uninstall

Configuration is preserved in `%APPDATA%\SysDashEndpointAgent\config.json`.
