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
- Native `<select>`s use `IconSelect` and are checked in WebKit: `npx playwright install webkit` once, then `npx playwright test --project=webkit` (`e2e/native-fidelity.spec.ts`). Add new selects to that spec.
- Reusable components catalog (Foundation): `UI templates/components/` (network-board, icon-form-fields, …) — check before building a new control.

## Card canvas (the UI is a display)
- **No execution and no card logic in the frontend.** Card types, their icons and their parameter forms come from `GET /api/cards/types`; run state arrives as complete snapshots over server-sent events (`services/eventsApi`) into `runStore`; the UI only sends commands (save, run, cancel). A run keeps going if you leave the screen.
- `runStore.upsert` keeps the snapshot with the **highest `revision`** (the reply to "start" is the oldest snapshot and arrives after pushed events: without this it overwrote live state).
- `FlowEditView` holds the editable draft (cards + connections) and saves it with the flow (`PUT /api/flows/{id}`); **Run uses the saved flow**, so it is disabled while there are unsaved changes or no trigger card.
- Canvas: drag the background to pan, scroll to pan, Ctrl/Cmd+scroll or the zoom buttons to zoom; drag a card to move it; drag from a card's right handle onto another card to connect (refused with a notice for self links, duplicates, triggers as targets and loops); click a card or connection to select; `Delete` or the buttons remove. Cards show status by icon and line style (dashed + spinner = running, check = succeeded, heavy outline + x = failed, dimmed = skipped/cancelled).
- Connection handles are not buttons (mouse only for now; `data-icon-exempt`). `TODO`: keyboard way to connect cards.

## Inspector: Settings · Input · Output · Guide
- `CardInspector` has four tabs (the chosen tab is remembered while you click from card to card; a dot marks Input/Output when real data from a run exists). The card's last run status and error stay visible above the tabs.
- **Input:** what the card received, from the selected run, or else the newest run in which it ran (`pickNodeRun`, labelled "earlier run" when it is not the selected one). Shown by `DataView`: **Fields** (`FieldExplorer`: path, type, preview, a button that copies the exact `{{input.json.name}}`; filter when there are many; keys a template cannot address are marked, not offered) or **JSON** (`OutputViewer`). Before any run it shows the fields the connected card usually provides (its guide's `outputExample`; several connections are numbered `input[0]`, `input[1]`); with nothing connected it says how to connect. The **Run input** (`{{run.input}}`) is listed too. Failed cards still show the input they were given. Pure logic: `helpers/fields.ts` (`flattenFields`, `tpl`), `helpers/runHelpers.ts` (`pickNodeRun`).
- **Output:** what the card produced as fields; copies give the reference for the NEXT card (`{{input.…}}`). Before a run: the kind of output from the guide.
- **Guide** (`GuidePanel`, all from the backend's `guide`): overview, how input arrives, what it outputs, examples (sample input as fields, highlighted code with Copy, the result in words, **Use this example** applies the settings and returns to Settings), tips, and a generic "How data moves between cards" lesson (`helpers/dataBasics.ts`).
- **Code fields:** a language-specific hint line under the editor (with an **Examples** button that opens the Guide) and a language-specific starter shown while empty; **Insert starter** turns the grey hint into real code (running a card whose code is only the grey hint says "Code is empty").
- Vue templates cannot hold a literal double brace inside `{{ }}`: use `tpl('input.x')` (`{{input.x}}`) in views.
- `App.vue` keys the router view by path: going straight from one flow to another builds a fresh editor (before, the old flow stayed on screen).

## Editor UX (long text, resizing, code)
- **Side panel:** drag the handle between canvas and panel (or ←/→, Shift for bigger steps, Home / double-click to reset). Width 280-900 px (never leaving < 320 px for the canvas), remembered in `localStorage` (`tammoa-side-width`). `helpers/layout.ts`.
- **Long text is never silently cut:** truncated labels carry the full text in `title` (card tooltip `name (type)`, flow list, run rows, URLs); multi-line text is clamped with Show more / Show less (`ExpandableText`); outputs use `OutputViewer` (scrollable preview, character count, Copy, "Full" dialog); code and prompts open in a large editor (`CodeField` expand button). Names are limited to 100 characters by the backend.
- **Code fields** (`components/forms/CodeEditor.vue` = CodeMirror 6, lazy-loaded ~470 KB chunk; `CodeField.vue` adds the header, drag-to-resize bottom edge and the expand dialog): line numbers, bracket matching and auto-close, indentation (Tab; Esc then Tab leaves the field), undo, syntax highlighting for shell/JavaScript/Python/JSON. Highlighting uses weight/italics/underline and the theme tokens, never hues (extra-minimal). No search panel (its buttons would break the icon rule). Language comes from the card's descriptor (`codeLanguage`, or `codeLanguageParam` = another param's value, e.g. the Script card's Language).
- **Param forms are drawn from the backend's descriptors** (`ParamField`): text, textarea, number, bool switch, select (`IconSelect`, WebKit-safe: own chevron, `appearance-none`), code, connection (picker + link to Connections), dynamic select (models of the chosen connection, reload button, shows a saved value that is no longer offered as "(not available)"). Unset params show the descriptor default, not a blank.
- **Palette:** grouped (Triggers, Logic, Data, Actions, AI) and searchable. **Canvas:** cards with several outputs (IF) show labelled handles; connections remember which handle they leave from.
- **Run panel:** Clear (finished runs only), delete one run, Cancel for running ones; removals pushed by the backend (`run-removed`) update every open window. Durations of running runs tick.
- **Connections page** (`/connections`): list with Test (version + models, or the reason it failed), create/edit dialog (API key never shown; empty = keep; "Remove the saved key" switch), delete confirmation (refused while cards use it).

## Feature log
| Date | Feature | Notes |
|---|---|---|
| 2026-11-20 | Scaffold | Backend version pill + theme toggle in the header |
| 2026-11-21 | Input visibility + tutorials | Inspector tabs Settings/Input/Output/Guide; field lists with copy-the-reference buttons, Fields/JSON, Run input, expected fields before a run; guides with examples that can be applied; per-language hint and starter under script code (+ Insert starter); router view keyed by path (fixes the old flow staying on screen). e2e: 22 specs in `input-and-guides.spec.ts` including following the JS/Python/shell tutorials end to end; WebKit check of the new tabs. |
| 2026-11-21 | Editor UX, cards, connections | Resizable side panel; tooltips, Show more, output viewer with Copy and full view; CodeMirror editors; select / code / connection / dynamic-options params; grouped searchable palette; IF with two handles; Connections page; run log Clear / delete with live removal; result shown first in the inspector. WebKit project added (`native-fidelity.spec.ts`: selects, editor, panel, SSE). |
| 2026-11-20 | Card canvas | `/flows/:id` is now the editor: toolbar (name, description, enable, delete, save, add card, run input, run), canvas, side panel (card inspector with the last run's output/error, run list with cancel). Fully wired to the backend engine. No WebKit run of the canvas yet. |
| 2026-11-20 | Flows | `/` lists flows (name, description, card count, updated, enable switch, duplicate, delete); `New flow` dialog → opens `/flows/:id`; edit page: name, description, enable switch, delete, read-only cards/connections count (definition is round-tripped untouched; no canvas yet). Delete always goes through `DeleteFlowDialog`: type the exact flow name to unlock the button (backend re-checks). Fully wired to `/api/flows`. Export/import: API client (`flowApi.exportDocument/importDocument`) exists, **no UI yet**. |
