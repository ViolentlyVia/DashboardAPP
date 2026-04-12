# SysDash Endpoint Agent

Windows tray agent that reports machine status to the SysDash server.

## Behavior

- On first launch, prompts for server URL/port and report interval if config does not exist.
- Runs in the system tray (notification area).
- Settings includes NIC/IP selection for the IP reported to the server.
- Right-click tray icon to open menu:
  - Settings
  - Pick NIC/IP
  - Send Report Now
  - Exit

## Configuration

Config is stored at:

%APPDATA%\\SysDashEndpointAgent\\config.json

Fields:

- ServerUrl (example: http://192.168.1.10:5000)
- IntervalSeconds
- PreferredIp (optional; null means auto-select primary active NIC)

## Installer-Time Seeding

You can pre-seed config before first launch in two ways:

1. Run the included script:

```powershell
powershell -ExecutionPolicy Bypass -File .\install-agent.ps1
```

2. Pass startup arguments to the app executable:

```text
--server-url http://192.168.1.10:5000 --interval 30 --report-ip auto
```

`--report-ip` accepts either an IPv4 address (for a specific NIC/IP) or `auto`.

The install script enables Windows startup registration by default, and uses:

- Published exe when available.
- dotnet + debug dll fallback for local/dev runs.

Use `-DisableAutoStart` to skip startup registration.

## Build/Run

```powershell
dotnet restore
dotnet run
```

The server endpoint used by the agent is:

<ServerUrl>/api/report
