# Tammoa — backend (Photino host + minimal APIs)

.NET 8, Photino window hosting the Vue UI (`UserInterface/`), Kestrel on port 8000 for `/api/*`.

## Structure
- `Program.cs` — composition root: static file server (sealed DI) + a separate `ServiceCollection` for app services, window creation. Debug loads Vite (5173); Release loads the embedded UI with a `?v=` cache-buster.
- `Api/` — `Map<Feature>Endpoints(this WebApplication, I<Feature>Service)` per feature.
- `Models/ Services/ Repositories/ Enum/ Helper/` — conventions in the Foundation repo (`backend-conventions.md`). `Enum/` types use namespace `Tammoa.Enums`.
- `Helper/JsonHelper` — Newtonsoft.Json (camelCase, nulls ignored, enums as strings). Never `System.Text.Json`.
- `Helper/EndpointHelpers` — `Try`/`TrySync`: `ArgumentException` → 400, `InvalidOperationException` → 409, else 500.
- `Resources/wwwroot/` — embedded built UI (keep the placeholder file, an empty folder crashes the static server).
- `AppIcon/` — placeholder icons (`app.ico`, `app.icns`); replace with the real artwork.

## Storage
**SQLite** (`Microsoft.Data.Sqlite`), chosen for flows (later: run history, logs). File: `<ApplicationData>/Tammoa/tammoa.db` (macOS `~/Library/Application Support/Tammoa`); override the folder with env var `TAMMOA_DATA_DIR` (dev runs and e2e tests use a temp folder). `Repositories/FlowRepository` creates the schema in its constructor (idempotent, WAL). The flow `definition` (cards + connections) is stored as a JSON text column and never queried inside.

## Conventions
DI always, no `new` services in endpoints, JSON via `JsonHelper`. `EndpointHelpers` maps `ArgumentException` → 400, `KeyNotFoundException` → 404, `InvalidOperationException` → 409. Time comes from an injected `TimeProvider`.

## Flow files (export / import)
A flow is portable: `GET /api/flows/{id}/export` returns a `FlowDocument` `{format: "tammoa.flow", version: 1, name, description, definition}` — no id, enabled state or timestamps. `POST /api/flows/import` creates a new, disabled flow from one (400 on wrong format or a newer `version`). `definition.schemaVersion` versions the card JSON itself for future migrations. **Never put secrets (API keys) in a definition**: cards must reference credentials by name, so exported files are safe to share.

## Feature log
| Date | Feature | Notes |
|---|---|---|
| 2026-11-20 | Scaffold | `GET /api/health`, `GET /api/app/version` |
| 2026-11-20 | Flows | `Api/FlowEndpoints`, `Services/FlowService`, `Repositories/FlowRepository` (SQLite). `GET/POST /api/flows`, `GET/PUT/DELETE /api/flows/{id}`, `POST /api/flows/{id}/enable|disable|duplicate`, `GET /api/flows/{id}/export`, `POST /api/flows/import`. Delete requires `{confirmName}` equal to the flow name exactly (400 otherwise). New/duplicated/imported flows start disabled. **Stub:** `enabled` is only a flag, nothing activates triggers until the run engine exists (`TODO(run engine)` in `IFlowService`). |
