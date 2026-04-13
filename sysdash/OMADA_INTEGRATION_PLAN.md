# Omada Northbound Integration Plan (SysDash)

## Goal
Integrate TP-Link Omada Northbound API data into SysDash so the dashboard can display Omada controller/site/device/client/network summaries using the existing AppState + monitor + summary payload architecture.

## Key Omada API Facts
- Auth is OAuth 2.0 and supports both authorization_code and client_credentials.
- Practical server-to-server choice for SysDash is client_credentials.
- Token endpoint: POST /openapi/authorize/token with grant_type=client_credentials.
- Token request body fields: omadacId, client_id, client_secret.
- API calls require header: Authorization: AccessToken=<token>.
- Access token lifetime is typically 7200s; refresh token is also returned.
- Common HTTP errors: 401, 403, 429, 500.
- Common result-level errors include invalid/expired token and invalid client credentials.
- API domain is region/controller specific and discovered from Omada Open API app details.

## Proposed SysDash Backend Design

### 1) Configuration
Add environment-backed Omada settings in AppState constructor:
- OMADA_BASE_URL
- OMADA_OMADAC_ID
- OMADA_CLIENT_ID
- OMADA_CLIENT_SECRET
- OMADA_SITE_ID (optional preferred site)
- OMADA_VERIFY_TLS (optional, default true)

Do not hardcode defaults for credentials.

### 2) New Omada State Cache
Add private members in AppState similar to Unraid/iDRAC:
- _omadaLock
- _omadaSnapshot (Dictionary<string, object?>)
- _omadaMonitorStarted
- _omadaToken + _omadaTokenExpiryUtc

Snapshot shape proposal:
- fetched_at
- connected
- error
- controller (base URL + id metadata)
- sites (list)
- selected_site
- gateways / switches / aps counts
- clients_total / clients_wired / clients_wireless
- health / utilization summary fields

### 3) Omada Auth + Fetch Methods
Add methods in a new partial file, for example Backend/AppState.Omada.cs:
- EnsureOmadaAccessTokenAsync()
- RefreshOmadaAccessTokenAsync() (or reacquire via client_credentials)
- FetchOmadaSnapshotAsync()
- GetOmadaSnapshot()
- SetOmadaSnapshot(...)
- PrimeOmadaCache()
- StartOmadaMonitor(CancellationToken)

Behavior:
- Reuse token until near expiry (for example refresh when <=120s remains).
- On 401/-44112/-44113, reacquire token once and retry request once.
- On 429, keep last good snapshot and expose rate-limit error in snapshot.

### 4) Endpoint Mapping
Extend ApiEndpointMappings:
- GET /api/omada
- GET /api/omada/refresh

Require existing access key like other protected endpoints.

### 5) Summary Payload Integration
Include Omada block in GetSummaryPayload() so mobile and dashboard consumers get one aggregate payload:
- omada: { fetched_at, connected, error, selected_site, stats... }

### 6) Hosted Service Integration
Start Omada cache lifecycle in AppStateHostedService:
- PrimeOmadaCache()
- StartOmadaMonitor(...)

Suggested poll interval: 30-60 seconds initially.

### 7) Frontend Integration
Use existing summary consumer (wwwroot/static/js/app.js) and render an Omada panel in Index page:
- Add Omada card block under side panels.
- Render site name, online devices, total clients, and top-level health status.
- Show stale/error state distinctly when snapshot not connected.

## Recommended Initial Endpoints to Call
Start with low-risk read-only endpoints:
- Site list endpoint (documented example): /openapi/v1/{omadacId}/sites
- Then selected site summary/device/client endpoints from docs sections used by your controller version.

Implementation note:
- Omada docs are versioned and controller-build dependent. Confirm exact endpoint paths from your released version page before final coding.

## Security Notes
- Keep client secret only in environment variables.
- Avoid logging tokens or secrets.
- Mask sensitive fields in debug responses.

## Testing Checklist
- Valid credentials returns connected snapshot.
- Expired token triggers automatic re-auth and recovery.
- Invalid credentials produces clear error snapshot.
- API 429 keeps previous snapshot and surfaces warning.
- /api/summary and /api/mobile/summary include omada section.

## Rollout Plan
1. Backend auth + snapshot only (no UI).
2. Add /api/omada endpoints and summary integration.
3. Add dashboard panel rendering.
4. Validate against live Omada controller in your region.
