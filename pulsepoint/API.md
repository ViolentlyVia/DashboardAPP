# PulsePoint — Technical Reference & API Specification

This document covers the full internal architecture, database schema, authentication model, and every API endpoint exposed by PulsePoint.Server. It is intended for integration work, custom agent development, and reverse-engineering the protocol.

---

## Table of Contents

- [Architecture Overview](#architecture-overview)
- [Authentication](#authentication)
- [Database Schema](#database-schema)
- [Agent Check-In Protocol](#agent-check-in-protocol)
- [REST API — Dashboard Routes](#rest-api--dashboard-routes)
- [REST API — Integration Routes](#rest-api--integration-routes)
- [REST API — Management Routes](#rest-api--management-routes)
- [Service Health Check Behaviour](#service-health-check-behaviour)
- [Integration Polling Behaviour](#integration-polling-behaviour)
- [Session Management](#session-management)
- [Data Types Reference](#data-types-reference)

---

## Architecture Overview

```
┌──────────────────────────────────────────────────────────────────┐
│  PulsePoint.Server  (ASP.NET Core, net10.0)                      │
│                                                                  │
│  ┌─────────────┐   ┌─────────────┐   ┌────────────────────────┐ │
│  │ Razor Pages │   │ Minimal API │   │ Background Services    │ │
│  │  (UI)       │   │ /api/*      │   │  ServiceMonitor (30s)  │ │
│  └──────┬──────┘   └──────┬──────┘   │  IntegrationMonitor    │ │
│         │                 │          │   (60s: Unraid/iDRAC/  │ │
│         └────────────┬────┘          │    Omada)              │ │
│                      ▼               └───────────┬────────────┘ │
│               ┌─────────────┐                    │              │
│               │  AppState   │◄───────────────────┘              │
│               │ (singleton) │                                    │
│               └──────┬──────┘                                    │
│                      │                                           │
│         ┌────────────┼──────────────────────────┐               │
│         ▼            ▼                          ▼               │
│  ┌────────────┐ ┌──────────────────────────────────────────┐    │
│  │  Database  │ │  Integration Services                    │    │
│  │  (SQLite)  │ │  UnraidService / IdracService /          │    │
│  └────────────┘ │  OmadaService                            │    │
│                 └──────────────────────────────────────────┘    │
└──────────────────────────────────────────────────────────────────┘
          ▲                        ▲                  ▲
          │ POST /api/report       │ GET /api/*?key=  │ LAN HTTP/HTTPS
          │                        │                  │
┌─────────┴──────┐       ┌─────────┴──────┐  ┌───────┴──────────────┐
│ PulsePoint     │       │  Browser /      │  │ Unraid / iDRAC /     │
│ .Agent         │       │  third-party    │  │ Omada / Grow device  │
│ (tray app)     │       │  client         │  └──────────────────────┘
└────────────────┘       └────────────────┘
```

### Key classes

| Class | File | Role |
|---|---|---|
| `AppState` | `AppState.cs` | Singleton; owns service health cache, integration snapshots, password/session logic, ping helper |
| `Database` | `Data/Database.cs` | All SQLite read/write; no ORM — raw `SqliteCommand` helpers |
| `Endpoints` | `Api/Endpoints.cs` | Maps all `/api/*` routes; contains `ApiKeyFilter` and `ManageSessionFilter` |
| `ServiceMonitor` | `Services/ServiceMonitor.cs` | `BackgroundService`; polls services every 30 s |
| `IntegrationMonitor` | `Services/IntegrationMonitor.cs` | `BackgroundService`; polls Unraid, iDRAC, and Omada every 60 s |
| `UnraidService` | `Integrations/UnraidService.cs` | Fetches Unraid array, disks, Docker, VMs, and shares via GraphQL |
| `IdracService` | `Integrations/IdracService.cs` | Fetches iDRAC 8 system, thermal, fans, PSUs, and storage via Redfish |
| `OmadaService` | `Integrations/OmadaService.cs` | Fetches Omada SDN sites, devices, and clients via OpenAPI v1 with OAuth |
| `JsonExtensions` | `Integrations/JsonExtensions.cs` | Safe `JsonElement` helpers used across all integration services |

---

## Authentication

PulsePoint has two independent auth systems that apply to different route groups.

### API key (dashboard + integration routes)

All `/api/*` routes except `POST /api/report` require the API key.

**How to provide it** — either method works:

```
GET /api/hosts?key=<your-api-key>
```

```
GET /api/hosts
X-Api-Key: <your-api-key>
```

The key is compared with `appsettings.json → ApiKey`. If the key is empty or missing from config, all requests are rejected with `401`.

**Failure response:**

```json
HTTP 401
{ "error": "unauthorized" }
```

### Session cookie (management routes)

All `/api/manage/*` routes and the `/manage` Razor page require a valid session cookie.

**Cookie name:** `pp_session`
**Cookie flags:** `HttpOnly`, `SameSite=Strict`
**Expiry:** 8 hours from creation; sessions are stored in-memory and lost on server restart

**How sessions are created:**

1. `POST /managesetup` (first time only) — sets the password and immediately creates a session
2. `POST /managelogin` — verifies the password and creates a session

The response sets the `pp_session` cookie. Include that cookie on all subsequent management requests.

**Failure response (management API):**

```json
HTTP 401
{ "error": "not authenticated" }
```

### Agent check-in

`POST /api/report` requires **no authentication**. This is intentional so agents can register without pre-configuration.

---

## Database Schema

SQLite database at the path configured in `appsettings.json → DbPath` (default: `pulsepoint.db`, relative to the working directory).

### `hosts` table

Stores the latest check-in data for each reporting endpoint.

```sql
CREATE TABLE hosts (
    hostname      TEXT PRIMARY KEY,
    ip            TEXT    NOT NULL DEFAULT '',
    uptime        REAL    NOT NULL DEFAULT 0,   -- seconds since boot
    last_seen     REAL    NOT NULL DEFAULT 0,   -- unix timestamp (UTC)
    ping          REAL,                          -- ms, nullable
    cpu           REAL    NOT NULL DEFAULT 0,   -- percent 0–100
    memory        REAL    NOT NULL DEFAULT 0,   -- percent 0–100
    disk          REAL,                          -- percent 0–100, nullable
    friendly_name TEXT,                          -- display override
    sort_order    INTEGER NOT NULL DEFAULT 0,
    rdp_url       TEXT,
    tags          TEXT                           -- comma-separated
);
```

**Notes:**
- `hostname` is the agent's `Environment.MachineName` — it is the natural primary key.
- A host is considered **online** if `last_seen >= (now - 120)` (i.e. checked in within the last 2 minutes).
- `ping` is updated lazily by `GET /api/hosts/{hostname}/ping` and is not part of the agent payload.
- `uptime` is seconds as a float (`Environment.TickCount64 / 1000.0`).

### `services` table

Stores monitored service definitions managed via the Management page.

```sql
CREATE TABLE services (
    id    INTEGER PRIMARY KEY AUTOINCREMENT,
    name  TEXT    NOT NULL,
    url   TEXT    NOT NULL   -- may be bare IP:port or full URL
);
```

**URL normalisation:** If `url` does not start with `http://` or `https://`, the server prepends `http://` before making the health check request.

### `settings` table

Key-value store for server-side configuration and integration credentials that persists across restarts.

```sql
CREATE TABLE settings (
    key   TEXT PRIMARY KEY,
    value TEXT NOT NULL
);
```

**Current keys:**

| Key | Value format | Description |
|---|---|---|
| `mgmt_password` | `<hex-salt>:<hex-sha256>` | Management password (16-byte random salt + SHA-256 hash) |
| `unraid_host` | IP or hostname | Unraid server address |
| `unraid_api_key` | string | Unraid Connect API key (`x-api-key` header) |
| `unraid_api_key_id` | string | Optional key ID for Unraid versions that require it |
| `unraid_bearer_token` | string | Optional bearer token |
| `idrac_host` | IP or hostname | iDRAC 8 address |
| `idrac_username` | string | Redfish basic-auth username |
| `idrac_password` | string | Redfish basic-auth password (stored in plaintext) |
| `omada_base_url` | URL | Omada controller base URL (e.g. `https://192.168.1.10:8043`) |
| `omada_omadac_id` | string | Omada controller ID (`omadacId`) |
| `omada_client_id` | string | OAuth client ID |
| `omada_client_secret` | string | OAuth client secret (stored in plaintext) |
| `omada_site_id` | string | Preferred site ID — auto-selected on load |
| `grow_url` | URL | Grow device base URL (e.g. `http://192.168.1.x`) — used by the proxy endpoint |

---

## Agent Check-In Protocol

The agent posts a JSON payload to the server on a configurable interval. No authentication header is required.

### Request

```
POST /api/report
Content-Type: application/json
```

```json
{
  "hostname": "DESKTOP-ABC123",
  "ip":       "192.168.1.55",
  "uptime":   345600.0,
  "cpu":      12.5,
  "memory":   67.3,
  "disk":     42.1
}
```

**Field definitions:**

| Field | Type | Required | Description |
|---|---|---|---|
| `hostname` | string | Yes | Machine name used as primary key |
| `ip` | string | Yes | IP address to display in the dashboard |
| `uptime` | float | Yes | Seconds since last boot (`TickCount64 / 1000.0`) |
| `cpu` | float | Yes | CPU usage percent (0–100) |
| `memory` | float | Yes | RAM usage percent (0–100) |
| `disk` | float | No | System drive usage percent (0–100); omit if unavailable |

### Response

```json
HTTP 200
{ "ok": true }
```

```json
HTTP 400
"hostname required"
```

### Upsert behaviour

The server performs an `INSERT … ON CONFLICT(hostname) DO UPDATE`. On first check-in a new row is inserted with `sort_order = 0`. On subsequent check-ins only `ip`, `uptime`, `last_seen`, `cpu`, `memory`, and `disk` are updated — `friendly_name`, `sort_order`, `rdp_url`, and `tags` are preserved.

---

## REST API — Dashboard Routes

All routes below require `?key=<ApiKey>` or `X-Api-Key: <ApiKey>` header.

---

### GET /api/hosts

Returns all hosts ordered by `sort_order ASC, hostname ASC`.

**Response:** `200 OK` — array of [Host](#host) objects.

```json
[
  {
    "hostname":     "SERVER-01",
    "ip":           "192.168.1.10",
    "uptime":       864000.0,
    "lastSeen":     1714200000,
    "ping":         1.4,
    "cpu":          8.2,
    "memory":       55.0,
    "disk":         34.7,
    "friendlyName": "Web Server",
    "sortOrder":    0,
    "rdpUrl":       null,
    "tags":         "production"
  }
]
```

---

### GET /api/hosts/{hostname}

Returns a single host by hostname.

**Response:** `200 OK` — single [Host](#host) object, or `404 Not Found`.

---

### GET /api/hosts/{hostname}/ping

Sends an ICMP ping to the host's stored IP, updates the `ping` column, and returns the result.

**Response:**

```json
HTTP 200
{
  "ip":      "192.168.1.10",
  "ping_ms": 1.4,
  "online":  true
}
```

`ping_ms` is `null` and `online` is `false` if the host did not respond.

---

### PUT /api/assets/{hostname}

Updates asset metadata. All fields are optional — only non-null values are applied.

**Request body:**

```json
{
  "friendlyName": "Web Server",
  "ip":           "192.168.1.10",
  "rdpUrl":       "rdp://192.168.1.10",
  "tags":         "production,linux"
}
```

**Response:** `200 OK` — `{ "ok": true }`

---

### DELETE /api/assets/{hostname}

Removes a host record entirely.

**Response:** `200 OK` — `{ "ok": true }`

---

### POST /api/assets/{hostname}/move-up

Swaps the host's `sort_order` with the host immediately above it in the sorted list.

**Response:** `200 OK` — `{ "ok": true }`

---

### POST /api/assets/{hostname}/move-down

Swaps the host's `sort_order` with the host immediately below it.

**Response:** `200 OK` — `{ "ok": true }`

---

### GET /api/services

Returns the cached service health results. The cache has a 60-second TTL; the background poller refreshes every 30 seconds.

**Response:** `200 OK` — array of [ServiceStatus](#servicestatus) objects.

```json
[
  {
    "name":         "Nginx",
    "url":          "http://192.168.1.5",
    "online":       true,
    "statusCode":   200,
    "responseMs":   14.3,
    "offlineSince": null,
    "error":        null
  }
]
```

---

### GET /api/services/refresh

Forces an immediate re-check of all services, bypassing the cache.

**Response:** same schema as `GET /api/services`.

---

### GET /api/summary

Aggregate payload used by the dashboard UI. Combines hosts and services in a single call.

**Response:**

```json
HTTP 200
{
  "hosts": {
    "total":   4,
    "online":  3,
    "offline": 1,
    "list":    [ /* Host objects */ ]
  },
  "services": {
    "total":   2,
    "online":  2,
    "offline": 0,
    "list":    [ /* ServiceStatus objects */ ]
  },
  "generatedAt": 1714200000
}
```

`online` threshold for hosts: `last_seen >= now - 120` (2 minutes).

---

### GET /api/version

Returns server build metadata.

**Response:**

```json
HTTP 200
{
  "version":  "1.0.0",
  "dotnet":   "10.0.0",
  "pid":      12345,
  "uptime_s": 3600
}
```

---

## REST API — Integration Routes

All routes below require `?key=<ApiKey>` or `X-Api-Key: <ApiKey>` header.

Integration snapshots are cached in memory by `AppState` and refreshed every 60 seconds by `IntegrationMonitor`. The first `GET` to a data endpoint triggers an immediate fetch if no cached snapshot exists.

---

### Unraid

#### GET /api/unraid

Returns the cached Unraid snapshot (fetches immediately if no cache exists).

**Response:** `200 OK` — [UnraidSnapshot](#unraidsnapshot)

#### GET /api/unraid/refresh

Forces a live re-fetch from the Unraid GraphQL API and returns the new snapshot.

**Response:** `200 OK` — [UnraidSnapshot](#unraidsnapshot)

#### POST /api/unraid/docker/{id}/start

Sends a `start` mutation to the Unraid GraphQL API for the given container ID.

**Response:** `200 OK` — `{ "ok": true|false }`

#### POST /api/unraid/docker/{id}/stop

Stops a Docker container by ID.

**Response:** `200 OK` — `{ "ok": true|false }`

#### POST /api/unraid/docker/{id}/restart

Restarts a Docker container by ID.

**Response:** `200 OK` — `{ "ok": true|false }`

#### POST /api/unraid/vm/{name}/start

Starts a VM by name via the Unraid GraphQL API.

**Response:** `200 OK` — `{ "ok": true|false }`

#### POST /api/unraid/vm/{name}/stop

Stops a VM by name.

**Response:** `200 OK` — `{ "ok": true|false }`

#### POST /api/unraid/vm/{name}/restart

Restarts a VM by name.

**Response:** `200 OK` — `{ "ok": true|false }`

---

### iDRAC 8 / Redfish

#### GET /api/idrac

Returns the cached iDRAC snapshot (fetches immediately if no cache exists).

**Response:** `200 OK` — [IdracSnapshot](#idracsnapshot)

#### GET /api/idrac/refresh

Forces a live re-fetch from the iDRAC Redfish API and returns the new snapshot.

**Response:** `200 OK` — [IdracSnapshot](#idracsnapshot)

---

### Omada SDN

#### GET /api/omada

Returns the cached Omada snapshot for the preferred site (fetches immediately if no cache exists). If no preferred site is configured, defaults to the first site returned by the controller.

**Response:** `200 OK` — [OmadaSnapshot](#omedasnapshot)

#### GET /api/omada/refresh

Forces a live re-fetch of all sites and the preferred site's data.

**Response:** `200 OK` — [OmadaSnapshot](#omedasnapshot)

#### GET /api/omada/site/{siteId}

Fetches live data for a specific site by its site ID. Also re-fetches the full sites list to populate site names. Used by the site-selector dropdown.

**Response:** `200 OK` — [OmadaSnapshot](#omedasnapshot)

#### PUT /api/omada/preferred-site/{siteId}

Saves the given `siteId` as the preferred site in the `settings` table (`omada_site_id`). Called automatically when the user selects a site in the UI.

**Response:** `200 OK` — `{ "ok": true }`

---

### Grow

#### GET /api/grow/info

Server-side proxy that fetches the URL stored in `settings → grow_url` and returns the HTML response. Injects `<base href="{grow_url}/">` into the `<head>` if not already present so that relative CSS, image, and script paths resolve correctly when rendered in a sandboxed iframe.

The Grow device URL is configured via the Management page (`PUT /api/manage/integrations/grow`).

**Response:**
- `200 OK` — `text/html` — the proxied page HTML
- `200 OK` — `text/html` — error page in the Carbon theme if the device is unreachable or the URL has not been configured (never returns a non-200 so the iframe always renders something)

**Timeout:** 10 seconds.

---

## REST API — Management Routes

All routes below require the `pp_session` cookie (set after a successful login via `/managelogin`).

Base path: `/api/manage/`

---

### GET /api/manage/services

Returns all service definitions stored in the database.

**Response:** `200 OK`

```json
[
  { "id": 1, "name": "Nginx",  "url": "http://192.168.1.5" },
  { "id": 2, "name": "Sonarr", "url": "http://192.168.1.8:8989" }
]
```

---

### POST /api/manage/services

Adds a new service. Automatically invalidates the service health cache.

**Request body:**

```json
{ "name": "Radarr", "address": "192.168.1.8:7878" }
```

`address` can be a bare `IP:port`, a hostname, or a full `http[s]://` URL.

**Response:** `200 OK` — `{ "ok": true }`
**Error:** `400 Bad Request` if `name` or `address` is empty.

---

### DELETE /api/manage/services/{id}

Removes a service by integer ID. Automatically invalidates the service health cache.

**Response:** `200 OK` — `{ "ok": true }`

---

### PUT /api/manage/assets/{hostname}/name

Updates only the `friendly_name` for a host.

**Request body:** `{ "name": "My Web Server" }`

**Response:** `200 OK` — `{ "ok": true }`
**Error:** `400 Bad Request` if `name` is empty.

---

### GET /api/manage/assets

Returns all hosts (same data as `GET /api/hosts` but via management auth).

**Response:** same schema as `GET /api/hosts`.

---

### GET /api/manage/integrations

Returns current Unraid and iDRAC settings. Passwords are never returned — only a boolean `hasPassword` flag.

**Response:**

```json
{
  "unraid": {
    "host":        "192.168.1.10",
    "apiKey":      "abc123",
    "apiKeyId":    "",
    "bearerToken": ""
  },
  "idrac": {
    "host":        "192.168.1.20",
    "username":    "root",
    "hasPassword": true
  }
}
```

---

### PUT /api/manage/integrations/unraid

Saves Unraid credentials.

**Request body:**

```json
{
  "host":        "192.168.1.10",
  "apiKey":      "abc123",
  "apiKeyId":    "",
  "bearerToken": ""
}
```

`apiKeyId` and `bearerToken` are optional — send empty string to clear.

**Response:** `200 OK` — `{ "ok": true }`

---

### PUT /api/manage/integrations/idrac

Saves iDRAC credentials. If `password` is empty/omitted the existing stored password is preserved.

**Request body:**

```json
{
  "host":     "192.168.1.20",
  "username": "root",
  "password": "calvin"
}
```

**Response:** `200 OK` — `{ "ok": true }`

---

### GET /api/manage/integrations/omada

Returns current Omada settings. The client secret is never returned — `hasSecret` indicates whether one is stored.

**Response:**

```json
{
  "baseUrl":      "https://192.168.1.10:8043",
  "omadacId":     "abc123",
  "clientId":     "my-client",
  "hasSecret":    true,
  "preferSiteId": "6850e16b2ce95e2390aa3ed3"
}
```

---

### PUT /api/manage/integrations/omada

Saves Omada credentials. If `clientSecret` is empty/omitted the existing stored secret is preserved.

**Request body:**

```json
{
  "baseUrl":      "https://192.168.1.10:8043",
  "omadacId":     "abc123",
  "clientId":     "my-client",
  "clientSecret": "s3cr3t",
  "preferSiteId": "6850e16b2ce95e2390aa3ed3"
}
```

**Response:** `200 OK` — `{ "ok": true }`

---

### GET /api/manage/integrations/grow

Returns the configured Grow device URL.

**Response:**

```json
{
  "url":        "http://192.168.1.x",
  "configured": true
}
```

`configured` is `false` and `url` is `""` if no URL has been saved yet.

---

### PUT /api/manage/integrations/grow

Saves the Grow device URL. This URL is used by `GET /api/grow/info` to proxy the device's web interface.

**Request body:**

```json
{ "url": "http://192.168.1.x" }
```

**Response:** `200 OK` — `{ "ok": true }`

---

## Service Health Check Behaviour

Services are checked by `AppState.CheckServiceAsync`. Key behaviours:

| Behaviour | Detail |
|---|---|
| **HTTP method** | `GET` |
| **Timeout** | 5 seconds |
| **SSL** | Certificate validation disabled — self-signed LAN certs accepted |
| **Redirects** | Not followed — the raw status code of the first response is reported |
| **Online threshold** | `statusCode < 500` — any 1xx/2xx/3xx/4xx counts as online |
| **HttpClient** | Single shared static instance |
| **Cache TTL** | 60 seconds |
| **Background poll** | Every 30 seconds via `ServiceMonitor` |

---

## Integration Polling Behaviour

`IntegrationMonitor` (`BackgroundService`) starts after a 10-second delay then polls every 60 seconds.

| Integration | Transport | Auth | SSL |
|---|---|---|---|
| **Unraid** | HTTPS POST to `https://{host}/graphql` | `x-api-key` header (+ optional `x-api-key-id`) | Validation disabled |
| **iDRAC 8** | HTTPS GET to `https://{host}/redfish/v1/…` | HTTP Basic auth per-request | Validation disabled |
| **Omada** | HTTPS to `{baseUrl}/openapi/…` | OAuth2 `client_credentials` → `Authorization: AccessToken={token}` | Validation disabled |

### Omada token caching

- Token fetched via `POST {baseUrl}/openapi/authorize/token`
- Cached for 7080 seconds (2 hours minus a 120-second safety buffer)
- Automatically invalidated and re-fetched on `errorCode -44112` or `-44113` (token expired)
- Four auth body variants are tried in sequence to handle firmware API differences

### iDRAC storage fetching

iDRAC 8 Redfish drive enumeration uses a two-phase parallel strategy to handle the slow Redfish API (30-second timeout per request):
1. Collect all drive `@odata.id` paths across all storage controllers in parallel
2. Fetch all drive details in parallel via `Task.WhenAll`

Individual drive fetch failures are silently skipped (`TryGetAsync` returns `null`) so a single unresponsive drive path does not block the rest.

---

## Session Management

Sessions are stored in a `Dictionary<string, DateTime>` inside the `AppState` singleton (in-memory, not persisted).

**Token generation:** `RandomNumberGenerator.GetBytes(32)` → hex-encoded (64-character string).

**Expiry:** 8 hours. Expired tokens are evicted on access.

**Logout:** `GET /managelogout` revokes the session and deletes the cookie.

**Server restart:** All sessions are lost.

### Password storage

```
settings table, key = "mgmt_password"
value format: <hex-encoded-16-byte-salt>:<hex-encoded-sha256-hash>
```

Hash input: `salt_bytes || UTF8(password)` — salt is prepended before hashing.

---

## Data Types Reference

### Host

```typescript
{
  hostname:     string,
  ip:           string,
  uptime:       number,        // seconds since boot (float)
  lastSeen:     number,        // unix timestamp UTC
  ping:         number | null, // ms
  cpu:          number,        // percent 0–100
  memory:       number,        // percent 0–100
  disk:         number | null, // percent 0–100
  friendlyName: string | null,
  sortOrder:    number,
  rdpUrl:       string | null,
  tags:         string | null  // comma-separated
}
```

> **Online check:** `Date.now()/1000 - lastSeen < 120`

### ServiceStatus

```typescript
{
  name:         string,
  url:          string,
  online:       boolean,
  statusCode:   number | null,
  responseMs:   number | null,
  offlineSince: number | null,
  error:        string | null
}
```

### UnraidSnapshot

```typescript
{
  connected: boolean,
  error:     string | null,
  fetchedAt: string,           // ISO 8601 UTC
  array: {
    state:      string,        // e.g. "STARTED"
    usedBytes:  number,
    freeBytes:  number,
    totalBytes: number
  },
  disks: DiskInfo[],
  parities: DiskInfo[],
  containers: DockerContainer[],
  vms: VmDomain[],
  shares: ShareInfo[]
}
```

**DiskInfo:**
```typescript
{
  name:   string,
  device: string,
  status: string,
  temp:   number,  // Celsius
  size:   number,  // bytes
  type:   string   // e.g. "DATA", "CACHE"
}
```

**DockerContainer:**
```typescript
{
  id:      string,
  names:   string,
  image:   string,
  state:   string,   // "running" | "stopped" etc.
  status:  string,
  running: boolean   // true if state == "running" (case-insensitive)
}
```

**VmDomain:**
```typescript
{
  name:    string,
  state:   string,   // "started" | "stopped" etc.
  running: boolean   // true if state == "started"
}
```

**ShareInfo:**
```typescript
{
  name:   string,
  freeKb: number,
  sizeKb: number,
  usedKb: number   // computed: sizeKb - freeKb
}
```

### IdracSnapshot

```typescript
{
  connected: boolean,
  error:     string | null,
  fetchedAt: string,
  system: {
    manufacturer:   string,
    model:          string,
    serviceTag:     string,
    biosVersion:    string,
    powerState:     string,
    healthStatus:   string,
    processorCount: number,
    totalMemoryGiB: number,
    idracFirmware:  string
  },
  temperatures: ThermalSensor[],
  fans:         FanInfo[],
  powerSupplies: PowerSupplyInfo[],
  drives:       StorageDrive[]
}
```

**ThermalSensor:**
```typescript
{
  name:                   string,
  readingCelsius:         number,
  upperThresholdCritical: number | null,
  status:                 string
}
```

**FanInfo:**
```typescript
{ name: string, rpm: number, status: string }
```

**PowerSupplyInfo:**
```typescript
{
  name:               string,
  model:              string,
  lastOutputWatts:    number | null,
  powerCapacityWatts: number | null,
  status:             string
}
```

**StorageDrive:**
```typescript
{
  name:          string,
  model:         string,
  manufacturer:  string,
  serialNumber:  string,
  mediaType:     string,   // "HDD" | "SSD" | "SMR" etc.
  protocol:      string,   // "SAS" | "SATA" | "NVMe" etc.
  capacityBytes: number,
  health:        string,
  state:         string
}
```

### OmadaSnapshot

```typescript
{
  connected:    boolean,
  error:        string | null,
  fetchedAt:    string,
  sites:        OmadaSite[],
  selectedSite: OmadaSite | null,
  devices:      OmadaDevice[],
  clients:      OmadaClient[]
}
```

**OmadaSite:**
```typescript
{ siteId: string, name: string, scenario: string }
```

**OmadaDevice:**
```typescript
{
  mac:             string,
  name:            string,
  type:            string,   // "ap" | "switch" | "gateway" | "eap" | "router"
  ip:              string,
  model:           string,
  firmwareVersion: string,
  status:          number,   // 0 = offline, 1 = online (connStatus preferred if present)
  online:          boolean,  // computed: status > 0
  uptime:          number,   // seconds (uptimeLong ms ÷ 1000 if available)
  clientCount:     number,
  download:        number,   // bps
  upload:          number    // bps
}
```

**OmadaClient:**
```typescript
{
  mac:            string,
  name:           string,
  ip:             string,
  networkName:    string,
  ssid:           string,
  wireless:       boolean,
  signalLevel:    number,   // dBm (wireless only)
  rxRate:         number,   // bps current download rate
  txRate:         number,   // bps current upload rate
  wiredLinkSpeed: number,   // Mbps (wired only)
  uptime:         number,   // seconds
  active:         boolean,
  trafficDown:    number,   // cumulative bytes downloaded (session/24h window)
  trafficUp:      number,   // cumulative bytes uploaded
  trafficTotal:   number    // computed: trafficDown + trafficUp
}
```

> **Sort order:** The Omada clients table is sorted by `trafficTotal` descending. If no firmware returns traffic data (all zeros), falls back to `rxRate + txRate` descending.

> **Traffic field names:** The service tries six field name variants per direction to cover Omada firmware differences: `trafficDown`, `trafficDownload`, `downTraffic`, `downTrafficBytes`, `totalDownload` (and upload equivalents).

### CheckInPayload (agent → server)

```typescript
{ hostname: string, ip: string, uptime: number, cpu: number, memory: number, disk: number | null }
```

### AddServicePayload

```typescript
{ name: string, address: string }
```

### AssetUpdatePayload

```typescript
{ friendlyName: string | null, ip: string | null, rdpUrl: string | null, tags: string | null }
```

---

## Writing a Custom Agent

Any HTTP client can post check-ins. Minimal example in Python:

```python
import requests, socket, time, psutil

SERVER = "http://192.168.1.10:5000"

while True:
    payload = {
        "hostname": socket.gethostname(),
        "ip":       "192.168.1.55",
        "uptime":   time.monotonic(),
        "cpu":      psutil.cpu_percent(interval=1),
        "memory":   psutil.virtual_memory().percent,
        "disk":     psutil.disk_usage("/").percent,
    }
    requests.post(f"{SERVER}/api/report", json=payload, timeout=8)
    time.sleep(30)
```

No API key, no auth header — `POST /api/report` is intentionally unauthenticated.
