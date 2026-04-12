# SysDash

SysDash is a self-hosted dashboard suite for monitoring endpoints and infrastructure from a single web UI.

The repository includes:

- `SysDash.NetCore`: ASP.NET Core dashboard + API
- `SysDash.EndpointAgent`: Windows tray agent that reports host metrics
- Inno Setup scripts for packaging dashboard and agent installers

## Table of Contents

- [Features](#features)
- [Solution Layout](#solution-layout)
- [Tech Stack](#tech-stack)
- [Prerequisites](#prerequisites)
- [Quick Start](#quick-start)
- [Configuration](#configuration)
- [Architecture](#architecture)
- [Deployment Model](#deployment-model)
- [API Overview](#api-overview)
- [Building Installers](#building-installers)
- [Operations Runbook](#operations-runbook)
- [Release Workflow](#release-workflow)
- [Troubleshooting](#troubleshooting)
- [Security Notes](#security-notes)
- [License](#license)

## Features

- Real-time host check-ins (uptime, CPU, memory, ping)
- Service status panel and cached background checks
- Unraid integration via GraphQL snapshot endpoints
- Dell iDRAC overview page (system, thermal, power, disk summaries)
- Asset management view (friendly names, IPs, launch URLs, ordering)
- Windows tray endpoint agent with first-run setup, NIC/IP selection, and manual reporting

## Solution Layout

```text
sysdash/
  sysdash.sln
  hosts.db
  SysDash.NetCore/          # Dashboard/API server
  SysDash.EndpointAgent/    # Windows tray agent
  Installer/                # Built installer outputs
  SysDash.Dashboard.iss
  SysDash.EndpointAgent.iss
  build-dashboard-installer.ps1
  build-installer.ps1
  build-installer.bat
```

## Tech Stack

- .NET 10 (`net10.0`) for the web dashboard/API
- .NET 10 Windows Forms (`net10.0-windows`) for the endpoint tray agent
- SQLite (`hosts.db`) for host and asset persistence
- Inno Setup 6+ for Windows installer packaging

## Prerequisites

- Windows 10/11 (for agent and installer workflows)
- .NET 10 SDK
- (Optional) Visual Studio 2022+ or VS Code with C# tooling
- Inno Setup 6+ if building installers

## Quick Start

### 1) Clone and open

```powershell
git clone <your-fork-or-repo-url>
cd sysdash
```

### 2) Run the dashboard server

```powershell
cd SysDash.NetCore
dotnet restore
dotnet run
```

By default, the server binds to `http://0.0.0.0:5000` when no `urls` value is configured.

Open in a browser:

- Dashboard: `http://localhost:5000/?key=herpderp`
- Assets page: `http://localhost:5000/assets?key=herpderp`
- iDRAC page: `http://localhost:5000/idrac?key=herpderp`

### 3) Run the Windows endpoint agent

In a second terminal:

```powershell
cd SysDash.EndpointAgent
dotnet restore
dotnet run
```

On first launch, the agent prompts for:

- Server URL (example: `http://192.168.1.10:5000`)
- Report interval in seconds
- Preferred report IP/NIC behavior

The agent posts host reports to:

- `<ServerUrl>/api/report`

### 4) Build both projects from solution root (optional)

```powershell
dotnet restore .\sysdash.sln
dotnet build .\sysdash.sln -c Release
```

## Configuration

### Dashboard/server environment variables

Set these before running `SysDash.NetCore`:

| Variable | Purpose | Default |
| --- | --- | --- |
| `SYSDASH_REQUIRED_KEY` | API/UI access key for protected routes | `herpderp` |
| `GUACAMOLE_RDP_URL_TEMPLATE` | RDP launch template for assets | empty |
| `UNRAID_HOST` | Unraid host/IP | `192.168.0.101` |
| `UNRAID_API_KEY_ID` | Unraid API key identifier | set in code |
| `UNRAID_API_KEY` | Unraid API key secret | set in code |
| `UNRAID_BEARER_TOKEN` | Optional Unraid bearer token | empty |
| `UNRAID_SESSION_COOKIE` | Optional Unraid session cookie | empty |
| `IDRAC_HOST` | iDRAC host/IP | `192.168.0.120` |
| `IDRAC_USERNAME` | iDRAC username | `admin` |
| `IDRAC_PASSWORD` | iDRAC password | set in code |

Example (PowerShell):

```powershell
$env:SYSDASH_REQUIRED_KEY = "change-me"
$env:UNRAID_HOST = "192.168.0.101"
dotnet run --project .\SysDash.NetCore\SysDash.NetCore.csproj
```

### Endpoint agent config location

Stored per-user at:

```text
%APPDATA%\SysDashEndpointAgent\config.json
```

Fields:

- `ServerUrl`
- `IntervalSeconds`
- `PreferredIp` (`null` means auto-select)

### Endpoint agent command-line seeding

You can seed/update agent config at launch:

```text
--server-url http://192.168.1.10:5000 --interval 30 --report-ip auto
```

`--report-ip` accepts either:

- `auto`
- An IPv4 address

Alternatively, run:

```powershell
powershell -ExecutionPolicy Bypass -File .\SysDash.EndpointAgent\install-agent.ps1
```

## Architecture

### Components

- `SysDash.NetCore`
  - Serves web UI pages (`/`, `/assets`, `/idrac`)
  - Exposes API endpoints for reporting, status, assets, and infrastructure integrations
  - Runs background hosted services for service checks and snapshot refresh
- `SysDash.EndpointAgent`
  - Runs in Windows tray on managed endpoints
  - Collects local machine metrics and sends periodic check-ins to dashboard API
- `hosts.db`
  - Shared SQLite datastore at repository/deployment root
  - Stores host reports and asset metadata used by UI/API

### Data Flow

1. Endpoint agent posts payloads to `POST /api/report`.
2. Dashboard reads and enriches host state (including ping and service cache).
3. UI pages poll protected endpoints with `?key=...` and render current state.
4. Unraid and iDRAC snapshots are refreshed and exposed through dedicated API routes.

## Deployment Model

### Recommended Internal Topology

- One dashboard host on trusted LAN/VPN segment
- One agent install per monitored Windows endpoint
- Firewall allows endpoint egress to dashboard `:5000`
- Admin/operator clients access dashboard over LAN/VPN only

### Runtime Profiles

- Dev/test:
  - `dotnet run` from source
  - local/default credentials and key only for isolated lab use
- Internal production:
  - Release build or packaged installer
  - environment-injected secrets
  - restricted network access and key rotation process

### Persistent Data

- `hosts.db` should be treated as operational state.
- Include `hosts.db` in backup schedule.
- Keep backup retention aligned with your team's incident recovery policy.

## API Overview

Most read/update APIs require `?key=<SYSDASH_REQUIRED_KEY>`.

`POST /api/report` is intended for agent check-ins and does not require the key.

- Host reporting and status:
  - `POST /api/report`
  - `GET /api/status`
  - `GET /api/ping/{ip}`
  - `GET /api/system_status`
- Services:
  - `GET /api/services`
  - `GET /api/services_legacy`
  - `GET /api/services/debug`
- Assets:
  - `GET /api/assets`
  - `PUT /api/assets/{hostname}`
  - `DELETE /api/assets/{hostname}`
  - `POST /api/assets/{hostname}/move-up`
  - `POST /api/assets/{hostname}/move-down`
- Unraid:
  - `GET /api/unraid`
  - `GET /api/unraid/refresh`
  - `GET /api/unraid/debug`
- iDRAC:
  - `GET /api/idrac`
  - `GET /api/idrac/refresh`
- Summary/meta:
  - `GET /api/summary`
  - `GET /api/mobile/summary`
  - `GET /api/version`
  - `GET /api/routes`

## Building Installers

Installer outputs are generated under `Installer/`.

### Endpoint agent installer

PowerShell:

```powershell
powershell -ExecutionPolicy Bypass -File .\build-installer.ps1
```

Batch alternative:

```batch
build-installer.bat
```

Output:

- `Installer/SysDash-EndpointAgent-Setup.exe`

### Dashboard installer

```powershell
powershell -ExecutionPolicy Bypass -File .\build-dashboard-installer.ps1
```

Output:

- `Installer/SysDash-Dashboard-Setup.exe`

## Operations Runbook

### Start/Stop

- Dashboard (source run):

```powershell
dotnet run --project .\SysDash.NetCore\SysDash.NetCore.csproj
```

- Agent (source run):

```powershell
dotnet run --project .\SysDash.EndpointAgent\SysDash.EndpointAgent.csproj
```

- Installer deployments:
  - Manage via standard Windows app lifecycle (install/uninstall, startup behavior).

### Health Checks

- Verify API process:
  - `GET /api/version`
- Verify route registration:
  - `GET /api/routes`
- Verify endpoint ingest:
  - Ensure recent host updates visible via `GET /api/status?key=...`
- Verify integration freshness:
  - Check `GET /api/unraid?key=...`
  - Check `GET /api/idrac?key=...`

### Config Change Process

1. Apply env var changes to dashboard host.
2. Restart dashboard process/service.
3. Validate `api/version` and one business endpoint (`api/status`).
4. For agent-side changes, update `%APPDATA%\SysDashEndpointAgent\config.json` or relaunch with CLI seeding args.

### Backup/Recovery

- Backup target:
  - `hosts.db`
- Recovery steps:
  1. Stop dashboard process.
  2. Restore `hosts.db` from backup.
  3. Start dashboard and verify via `api/status` and `/assets`.

## Release Workflow

### Internal Build Sequence

1. Validate build:

```powershell
dotnet restore .\sysdash.sln
dotnet build .\sysdash.sln -c Release
```

2. Build installers:

```powershell
powershell -ExecutionPolicy Bypass -File .\build-installer.ps1
powershell -ExecutionPolicy Bypass -File .\build-dashboard-installer.ps1
```

3. Smoke test:
  - Install dashboard package on staging host
  - Install/launch agent package on one test endpoint
  - Confirm host ingestion + services + Unraid/iDRAC panels

### Suggested Change Control

- Use versioned internal release notes per installer output.
- Record config/env var changes with each release.
- Keep a rollback-ready previous installer + database backup.

## Troubleshooting

- `dotnet` command not found:
  - Install .NET 10 SDK and reopen the terminal.
- Installer script fails with Inno Setup missing:
  - Install Inno Setup 6+ to `C:\Program Files (x86)\Inno Setup 6` or update the script path.
- Dashboard loads but no hosts appear:
  - Confirm the agent `ServerUrl` points to the dashboard host and that `api/report` is reachable.
- `401/403` responses from API:
  - Confirm your `?key=` query value matches `SYSDASH_REQUIRED_KEY`.
- Unraid or iDRAC panels show stale or error data:
  - Verify environment variables and network connectivity to those systems.

## Security Notes

- Change the default `SYSDASH_REQUIRED_KEY` before using on a real network.
- Move all Unraid/iDRAC credentials to environment variables before production use.
- Restrict inbound access to the dashboard host (firewall + trusted subnet/VPN).
- Avoid committing production credentials or tokens.

## License

Add your preferred license to this repository (for example, MIT) and update this section.