# PulsePoint

A self-hosted infrastructure monitoring suite featuring a Carbon dark theme, SQLite-backed service config, a password-protected management page, and Inno Setup installers. 

---

## Repository Layout

```text
PulsePoint/
├── PulsePoint.Server/       ASP.NET Core dashboard + REST API
├── PulsePoint.Agent/        Windows Forms tray agent
├── Installer/               Built EXE installers (git-ignored)
├── PulsePoint.Server.iss    Inno Setup script — server
├── PulsePoint.Agent.iss     Inno Setup script — agent
├── build-server.ps1         Publish + compile server installer
├── build-agent.ps1          Publish + compile agent installer
└── pulsepoint.sln
```

---

## Quick Start

### Prerequisites

- Windows 10/11
- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- Inno Setup 6+ (only needed to build installers)

### Run from source

**1 — Start the server**

```powershell
cd PulsePoint.Server
dotnet run
```

The dashboard is available at:

```text
http://localhost:5000/?key=change-me
```

The API key is set in `appsettings.json` (`"ApiKey": "change-me"`). Change it before use.

**2 — Start the agent** (separate terminal, same or different machine)

```powershell
cd PulsePoint.Agent
dotnet run
```

On first launch the Settings dialog opens. Enter your server URL, report interval, and NIC. The tray icon turns green when reports are reaching the server.

**3 — Open the management page**

Navigate to `http://localhost:5000/manage`. On first visit you will be prompted to set a management password. From there you can add monitored services and edit asset friendly names.

### Install from pre-built EXEs

Both installers are produced by the build scripts and placed in `Installer/`.

| Installer | What it does |
|---|---|
| `PulsePoint-Server-Setup.exe` | Installs server, writes `appsettings.json` with your API key and port, optionally registers as a Windows Service |
| `PulsePoint-Agent-Setup.exe` | Installs agent, seeds config with server URL / interval / NIC, optionally registers Windows startup entry |

Run the **server installer first**, then deploy the **agent installer** on each machine you want to monitor.

### Build installers yourself

Requires the .NET 10 SDK and Inno Setup 6 installed to the default path.

```powershell
# Server
.\build-server.ps1

# Agent
.\build-agent.ps1
```

Outputs land in `Installer/`.

---

## Configuration

`PulsePoint.Server/appsettings.json` is the only file you need to edit for a basic deployment.

| Key | Description | Default |
|---|---|---|
| `ApiKey` | Required query parameter (`?key=`) for all dashboard and API routes | `change-me` |
| `DbPath` | Path to the SQLite database file | `pulsepoint.db` |
| `Urls` | Kestrel listen address (added by server installer) | `http://0.0.0.0:5000` |

Services are managed at runtime via the Management page — no `appsettings.json` edit needed.

Agent config is stored per-user at `%APPDATA%\PulsePointAgent\config.json`:

```json
{
  "ServerUrl": "http://192.168.1.10:5000",
  "IntervalSeconds": 30,
  "PreferredIp": null
}
```

---

## Pages

| URL | Auth | Description |
|---|---|---|
| `/?key=<key>` | API key | Live host grid — CPU / RAM / disk bars, ping, uptime |
| `/assets?key=<key>` | API key | Edit friendly names, IPs, RDP URLs, display order |
| `/services?key=<key>` | API key | Service health cards — updated every 30 s by background poller |
| `/manage` | Session cookie | Add/remove services, edit asset names |
| `/managesetup` | — | First-time password setup (auto-redirects here on first visit) |
| `/managelogin` | — | Management login form |
| `/managelogout` | — | Clears session cookie, redirects to login |

---

## Tech Stack

| Layer | Technology |
|---|---|
| Server runtime | ASP.NET Core Minimal API + Razor Pages, net10.0 |
| Agent runtime | Windows Forms, net10.0-windows |
| Database | SQLite via `Microsoft.Data.Sqlite` (no ORM) |
| Auth — dashboard | Query-parameter API key |
| Auth — management | SHA-256 + random salt password; in-memory session tokens; `HttpOnly` cookie |
| Installers | Inno Setup 6 |
| Frontend | Vanilla JS + CSS (no framework, no npm) |

---

## Security Notes

- Change `ApiKey` and the management password before exposing the dashboard on a real network.
- Restrict dashboard access to a trusted LAN segment or VPN — there is no per-user login for the main dashboard.
- Management sessions expire after 8 hours and are cleared on server restart.
- The agent does not require an API key to post check-ins (`POST /api/report` is intentionally open so agents can register without pre-configuration).
- SSL certificate validation is disabled for service health checks to support LAN services with self-signed certificates.

---

## License

Add your preferred license here.