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

## Conventions
DI always, no `new` services in endpoints, JSON via `JsonHelper`. No repository / storage method chosen yet — `backend-enhance` asks when the first persistence need appears (JSON file vs SQLite).

## Feature log
| Date | Feature | Notes |
|---|---|---|
| 2026-11-20 | Scaffold | `GET /api/health`, `GET /api/app/version` |
