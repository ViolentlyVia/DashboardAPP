# SysDash .NET Core Migration

This project is the ASP.NET Core migration of your Flask-based SysDash server.

## What was migrated

- API endpoints from `server.py` to ASP.NET Core minimal APIs:
  - `/api/report`
  - `/api/status`
  - `/api/ping/{ip}`
  - `/api/system_status`
  - `/api/services`, `/api/services_legacy`, `/api/services/debug`
  - `/api/assets` and asset update/delete/move routes
  - `/api/unraid`, `/api/unraid/debug`, `/api/unraid/refresh`
  - `/api/summary`, `/api/mobile/summary`
  - `/api/version`, `/api/routes`
- SQLite persistence against the same `hosts.db` file in the workspace root.
- Background monitors for service checks and Unraid snapshot refresh.
- Dashboard UI and assets page as static files in `wwwroot`.

## Run

From this folder:

```powershell
dotnet restore
dotnet run
```

Default URL is shown by ASP.NET Core in terminal output (typically `http://localhost:5000` or `http://localhost:50xx`).

Open with access key query parameter:

```text
http://localhost:5000/?key=herpderp
```

## Configuration

These environment variables are supported:

- `SYSDASH_REQUIRED_KEY` (default: `herpderp`)
- `GUACAMOLE_RDP_URL_TEMPLATE`
- `UNRAID_HOST`
- `UNRAID_API_KEY_ID`
- `UNRAID_API_KEY`
- `UNRAID_BEARER_TOKEN`
- `UNRAID_SESSION_COOKIE`

## Notes

- Existing Python agent can continue posting to `/api/report` without changes.
- Flask/Jinja server-side template rendering was replaced with static HTML + API polling.
- Original Flask/Python implementation remains untouched in the workspace.
