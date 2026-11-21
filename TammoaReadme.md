# Tammoa

A desktop app for building AI-agent automations from modular **cards** (inspired by n8n): wire triggers, model cards, script cards and loops into flows, and customise how the agent loop works from the UI. Built so a small local model (e.g. Qwen3 14B via Ollama) can run a lean, hand-designed loop instead of a heavy CLI agent with a big system prompt and tool list.

## Using it now
- **Flows** (home): create with *New flow*, open a flow to rename it or change its description, switch it *Enabled/Disabled* (a flag for now; nothing runs yet), duplicate, or delete. Deleting asks you to type the flow's exact name.
- Flows are stored in a local SQLite file in your user data folder (`~/Library/Application Support/Tammoa` on macOS, `%APPDATA%\Tammoa` on Windows). Set `TAMMOA_DATA_DIR` to use another folder (handy for testing).
- Flows can be exported/imported as a portable JSON document through the API (`GET /api/flows/{id}/export`, `POST /api/flows/import`); UI buttons come later.

## Cards
| Card | Use it to |
|---|---|
| Manual trigger | start a flow (you can pass text or JSON when you press Run) |
| IF | split the flow in two (true / false output) |
| Wait | pause for a while |
| Set value, JSON, Merge | build, parse, stringify and combine data |
| Script | run **Shell, JavaScript (needs Node) or Python (needs python3)**; read the input from `$TAMMOA_INPUT` (JSON); print JSON to use it in the next card as `{{input.json.x}}` |
| HTTP Request | call a web API |
| Read file, Write file | read or write a text file |
| Ollama | ask a local or remote Ollama model (create a connection first) |

## Reusable data: the Data card
Need a piece of text that many cards use (a style guide, settings, a template) **without** triggering anything? Add a **Data** card.
1. Name it (the name is how you refer to it, for example `Style`), save the flow once, then type the text in the card and press **Save text**. It is stored as a plain `.txt` file (the path is shown in the card).
2. **Any card can read it by name**, without a wire: `{{data.Style}}` in prompts, URLs, JSON and shell commands; in **JavaScript** `data.Style`, in **Python** `data["Style"]`. If the text is JSON (`{"city":"Phnom Penh"}`) its fields work too: `{{data.Settings.city}}`.
3. **A flow can write to it:** connect a card into the Data card, choose *Replace* or *Add as a new line* and what to save (for example `{{input.text}}`). The default is *Keep*, so connecting never overwrites your text by accident. Cards after it in the same run already see the new text.
4. *Shared with: All flows* makes every flow with a Data card of the same name use the same text. Names: unique in the flow, no `. [ ] | { }`.
5. It follows its flow: deleting a flow deletes its texts, duplicating copies them, export/import carries them (shared texts stay on your computer). The Input tab of every card lists the Data cards with a copy button.

## Watching the AI write
Select an Ollama card and press Run: its **Thinking** (if the model thinks) and its **Answer** appear live at the top of the card's panel, in any tab, and the final output replaces them when it is done. Cancel stops the model. The card's timeout is now *"stop if silent for N seconds"*, so long answers are fine.

## Using data from another card (read this first)
1. **Run once.** Then click a card and open its **Input** tab: it lists every field the card received, with its type and a preview. The **copy** button next to a field copies the exact reference, e.g. `{{input.json.name}}`.
2. **Paste it into a setting** (a prompt, URL, file path, JSON, shell command): it is replaced by the value when the card runs. `{{run.input}}` is what you typed in *Run input*, available in every card.
3. **In a Script card** the code reads the input directly: **JavaScript** `input` and `runInput` (for example `input.json.name`), **Python** `input_data` and `run_input` (for example `input_data["json"]["name"]`), **Shell** `{{input.stdout | sh}}` or `$TAMMOA_INPUT`. Print **JSON** (`console.log(JSON.stringify({ ok: true }))`) to hand data on: the next card reads `{{input.json.ok}}`. Plain printed text is `{{input.stdout}}`.
4. **Not run yet?** The Input tab shows the fields the connected card usually provides. The **Output** tab shows what a card produced, and what the next card can use.
5. **Every card has a Guide tab:** what it does, how its input arrives, its output fields and ready examples. **Use this example** fills in the card's settings. Under every script editor a hint line and a starter (**Insert starter**) show how to read the input in the language you picked.

## Connections (AI models)
Open **Connections**, add your Ollama address (default `http://localhost:11434`; an API key is optional and only needed behind a proxy), press **Test** to see its models, then pick the connection and a model in an Ollama card. The key is stored in plain text on this computer in a folder only you can open (`~/Library/Application Support/Tammoa`), never shown again and never put in exported flows.

## Building and running a flow
1. Open a flow. **Add card** lists the card types: *Manual trigger*, *Script*, *Set value*.
2. Drag cards where you like, drag from a card's right dot onto another card to connect them, click a card to edit its settings on the right. **Save**.
3. Type something in *Run input* (text or JSON, optional) and press **Run**. The backend executes the flow; cards show running / succeeded / failed / skipped live, and clicking a card shows its output or error. **Cancel** stops a run. You can start several runs at once, even of the same flow.
- Data moves from card to card: use `{{input.stdout}}` (the previous card's output) and `{{run.input}}` in card settings. Put values in shell commands with `{{input.text | sh}}` so quotes and spaces cannot break the command.
- Everything runs in the backend, the window only displays the state. Run history is kept in memory (lost when the app closes); **Clear** in the run list removes finished runs, and each flow keeps its newest 50.
- The side panel can be dragged wider; code and long text open in a larger editor or viewer. Hover shortened names to read them in full.
- Not built yet: schedule and webhook triggers, OpenAI / Anthropic connections, an agent loop with tool calling.

## Loops and card size
- **Loop:** drag from a card's right dot back onto a card that comes before it. The line is dashed and labelled *loop*; the target runs again with the new output as its input, and the cards after the loop wait until it is over. End a loop with an **IF** card (its *true* branch goes back, *false* goes on). A loop with no end is stopped at the **loop limit** (field in the toolbar, default 50 runs per card) with a clear message.
- Cards that ran more than once show `×N`; in the side panel use the round arrows to read what each earlier round got and produced (last 10 kept).
- **Card size:** cards grow to show their whole title. Drag the small handle at a card's bottom-right corner to resize it; double-click the handle to return to automatic.

## Vision (mostly not built yet)
- **Card canvas** with connections, like n8n; flows saved as JSON.
- **Trigger cards**: manual click, passed-in prompt/value, schedule.
- **Model cards**: Anthropic API, OpenAI API, local Ollama, remote Ollama server (API keys kept out of git).
- **Script cards**: run a script/command; a model card can call them as tools.
- **Loop control**: the user programs the loop (when the model runs, which tools it sees, how many iterations, when to stop) rather than a fixed agent loop.
- Desktop only; runs while the window is open or minimised.

Build these with the `enhance` skill, one slice at a time.

## Prerequisites
- .NET 8 SDK
- Node.js 20+
- macOS or Windows

## Run locally
<!-- run-test reads this section. One block per process; `ready` is the URL to poll. -->

| Process | Directory | Command | Port | Ready check |
|---|---|---|---|---|
| frontend | `Tammoa/UserInterface` | `npm run dev` | 5173 | `http://localhost:5173` |
| backend  | `Tammoa` | `dotnet run` | 8000 | `http://localhost:8000/api/health` |

First time: `npm install` in `Tammoa/UserInterface`.
Logs: `$TMPDIR/tammoa-frontend.log`, `$TMPDIR/tammoa-backend.log`
`dotnet run` (Debug) opens the native window pointed at the Vite dev server, so start the frontend first.

## Stop locally
Use the `stop-test` skill. Ports: 5173, 8000. Process match: `bin/Debug/net8.0/Tammoa`.

## Tests
- Backend: `dotnet test`
- Frontend unit: `npm run test:unit` (in `Tammoa/UserInterface`); e2e: `npx playwright test --project=chromium` (needs both dev servers running; start the backend with `TAMMOA_DATA_DIR=<temp folder>` so tests never touch your real flows or connections) and, for the macOS window's engine, `npx playwright install webkit` once, then `npx playwright test --project=webkit`. Node and python3 should be installed (the script tests use them).

## Build
```bash
cd Tammoa/UserInterface && npm install && npm run build   # type-check + dist/
cd .. && dotnet build
```
Release packaging copies `UserInterface/dist` into `Tammoa/Resources/wwwroot` (embedded) — wired up by `setup-deploy`.

## Deploy
<!-- deploy reads this section. Filled in by setup-deploy. -->
TBD — run `setup-deploy`.

## Versioning
Single source of truth: `<Version>` in `Tammoa/Tammoa.csproj` — frontend `package.json` version is not used.
