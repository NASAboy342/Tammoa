# Tammoa — frontend (Vue 3 + TypeScript + Tailwind v4)

Vite, Pinia (setup stores), vue-router (hash history), Vitest, Playwright. UI template: **extra-minimal** (colourless, outline-only; status by icon and line style, never colour). Tokens live in `src/assets/main.css` (`:root` + `[data-theme='dark']`, mapped in Tailwind `@theme`).

## Structure
```
src/main.ts  App.vue            bootstrap + shell (header, theme toggle)
src/views/                      FlowsView (list), FlowEditView (/flows/:id)
src/components/icons/           AppIcon.vue + icons.ts (+ __tests__)
src/stores/                     appStore, flowStore, cardTypeStore (catalogue from the backend), runStore (mirrors pushed run snapshots), themeStore
src/components/canvas/          FlowCanvas (pan/zoom/drag/connect), CardPalette, CardInspector, ParamField, RunPanel
src/components/                 FlowSwitch, FlowFormDialog, DeleteFlowDialog, ModalShell, forms/IconField
src/helpers/                    flowHelpers, canvasMath, flowDraft (pure draft logic), runHelpers (+ specs)
src/services/                   eventsApi (EventSource), cardApi, runApi, flowApi, httpClient.ts (requestJson<T>) + <feature>Api.ts mirroring Api/*Endpoints.cs
src/router/index.ts             lazy routes
e2e/                            Playwright: app.spec.ts, flows.spec.ts, icons.spec.ts (icon audit incl. dialogs)
```

## Conventions
- Every clickable has an `AppIcon` (hard rule 7); icon-only tools carry `title` + `aria-label`. No emoji/glyphs. `e2e/icons.spec.ts` enforces it — add routes/states as the app grows.
- No `fetch` outside `services/httpClient.ts`; relative `/api/...` URLs (Vite proxies to :8000).
- Every interactive element has a `data-testid`.
- When a native `<select>` is added, also add `native-fidelity.spec.ts` (WebKit project) from `UI templates/components/app-icon/` in the Foundation repo, plus a webkit project in `playwright.config.ts`.
- Reusable components catalog (Foundation): `UI templates/components/` (network-board, icon-form-fields, …) — check before building a new control.

## Card canvas (the UI is a display)
- **No execution and no card logic in the frontend.** Card types, their icons and their parameter forms come from `GET /api/cards/types`; run state arrives as complete snapshots over server-sent events (`services/eventsApi`) into `runStore`; the UI only sends commands (save, run, cancel). A run keeps going if you leave the screen.
- `runStore.upsert` keeps the snapshot with the **highest `revision`** (the reply to "start" is the oldest snapshot and arrives after pushed events: without this it overwrote live state).
- `FlowEditView` holds the editable draft (cards + connections) and saves it with the flow (`PUT /api/flows/{id}`); **Run uses the saved flow**, so it is disabled while there are unsaved changes or no trigger card.
- Canvas: drag the background to pan, scroll to pan, Ctrl/Cmd+scroll or the zoom buttons to zoom; drag a card to move it; drag from a card's right handle onto another card to connect (refused with a notice for self links, duplicates, triggers as targets and loops); click a card or connection to select; `Delete` or the buttons remove. Cards show status by icon and line style (dashed + spinner = running, check = succeeded, heavy outline + x = failed, dimmed = skipped/cancelled).
- Connection handles are not buttons (mouse only for now; `data-icon-exempt`). `TODO`: keyboard way to connect cards.

## Feature log
| Date | Feature | Notes |
|---|---|---|
| 2026-11-20 | Scaffold | Backend version pill + theme toggle in the header |
| 2026-11-20 | Card canvas | `/flows/:id` is now the editor: toolbar (name, description, enable, delete, save, add card, run input, run), canvas, side panel (card inspector with the last run's output/error, run list with cancel). Fully wired to the backend engine. No WebKit run of the canvas yet. |
| 2026-11-20 | Flows | `/` lists flows (name, description, card count, updated, enable switch, duplicate, delete); `New flow` dialog → opens `/flows/:id`; edit page: name, description, enable switch, delete, read-only cards/connections count (definition is round-tripped untouched; no canvas yet). Delete always goes through `DeleteFlowDialog`: type the exact flow name to unlock the button (backend re-checks). Fully wired to `/api/flows`. Export/import: API client (`flowApi.exportDocument/importDocument`) exists, **no UI yet**. |
