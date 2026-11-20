# Tammoa — frontend (Vue 3 + TypeScript + Tailwind v4)

Vite, Pinia (setup stores), vue-router (hash history), Vitest, Playwright. UI template: **extra-minimal** (colourless, outline-only; status by icon and line style, never colour). Tokens live in `src/assets/main.css` (`:root` + `[data-theme='dark']`, mapped in Tailwind `@theme`).

## Structure
```
src/main.ts  App.vue            bootstrap + shell (header, theme toggle)
src/views/                      one component per route
src/components/icons/           AppIcon.vue + icons.ts (+ __tests__)
src/stores/                     appStore (backend version), themeStore
src/services/                   httpClient.ts (requestJson<T>) + <feature>Api.ts mirroring Api/*Endpoints.cs
src/router/index.ts             lazy routes
e2e/                            Playwright: home.spec.ts, icons.spec.ts (icon audit)
```

## Conventions
- Every clickable has an `AppIcon` (hard rule 7); icon-only tools carry `title` + `aria-label`. No emoji/glyphs. `e2e/icons.spec.ts` enforces it — add routes/states as the app grows.
- No `fetch` outside `services/httpClient.ts`; relative `/api/...` URLs (Vite proxies to :8000).
- Every interactive element has a `data-testid`.
- When a native `<select>` is added, also add `native-fidelity.spec.ts` (WebKit project) from `UI templates/components/app-icon/` in the Foundation repo, plus a webkit project in `playwright.config.ts`.
- Reusable components catalog (Foundation): `UI templates/components/` (network-board, icon-form-fields, …) — check before building a new control.

## Feature log
| Date | Feature | Notes |
|---|---|---|
| 2026-11-20 | Scaffold | Home view showing backend version, theme toggle |
