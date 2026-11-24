# Tammoa

A desktop app for building AI-agent automations from modular **cards** (inspired by n8n): wire triggers, model cards, script cards and loops into flows, and customise how the agent loop works from the UI. Built so a small local model (e.g. Qwen3 14B via Ollama) can run a lean, hand-designed loop instead of a heavy CLI agent with a big system prompt and tool list.

## Using it now
- **Flows** (home): create with *New flow*, open a flow to rename it or change its description, switch it *Enabled/Disabled* (a flag for now; nothing runs yet), duplicate, or delete. Deleting asks you to type the flow's exact name.
- Flows are stored in a local SQLite file in your user data folder (`~/Library/Application Support/Tammoa` on macOS, `%APPDATA%\Tammoa` on Windows). Set `TAMMOA_DATA_DIR` to use another folder (handy for testing).
- Flows can be exported/imported as a portable JSON document through the API (`GET /api/flows/{id}/export`, `POST /api/flows/import`); UI buttons for a single flow come later. To move or protect everything at once, use **Backup** in Settings.

## First time here
- **No flows yet?** The Flows page greets you and offers **Start from a template**. Pick *Hello, flow* (no AI, nothing to set up), press **Run** in the flow, then click a card to see what it received and produced. **From a template** in the header is always there: *Choose a path (IF)*, *Call a web API*, and two AI ones (*Summarize text*, *Summarize a file*) that need an Ollama connection (the dialog lets you choose the connection and the model).
- **Ollama:** install it (ollama.com), download a model (`ollama pull qwen3:14b`), then add the connection under **Connections** (usually `http://localhost:11434`) and press **Test**.
- **Settings** (top bar): **Check for updates** asks GitHub for the latest release (nothing about you is sent; you can switch on *Check when the app opens*, it is off by default). **Back up now** writes one file (flows, their Data texts, connections **without keys**, shared texts) into the `backups` folder of your data folder; **Restore** puts back what is missing and never overwrites anything. API keys, run history and the agent token are not in a backup: type the keys again after a restore. Copy the file to another computer and use *Restore from a file…* to move everything.
- Where the app looks for updates: `RepositoryUrl` in `Tammoa/Tammoa.csproj`.

## Cards
| Card | Use it to |
|---|---|
| Manual trigger | start a flow (you can pass text or JSON when you press Run) |
| IF | split the flow in two (true / false output) |
| Wait | pause for a while |
| Set value, JSON, Merge | build, parse, stringify and combine data |
| Script | run **Shell, JavaScript (needs Node) or Python (needs python3)**; read the input from `$TAMMOA_INPUT` (JSON); print JSON to use it in the next card as `{{input.json.x}}` |
| HTTP Request | call a web API |
| Read file, Write file | read or write a text file. Read file passes on what it received as `{{input.input}}`. A missing file is read as empty text (`exists` is false); switch on *Create the file* to create it and its folders |
| Ollama | ask a local or remote Ollama model (create a connection first). *Context size* (optional) sets how much the model can read at once: Ollama's default is small (about 2048-4096 tokens) and quietly forgets the start of a long prompt, so set it (for example 16384) for long tasks and tools |

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

## Let an AI assistant build flows (Agent access)
Open **Agent** in the top bar. Switch **Agent access** on, press **Copy for the agent** and paste the text to your assistant (Claude Code, for example). It contains the address of this app, a secret token and where to read the *skill*, a guide the app serves about how to build flows for this exact version, with the live list of cards. The assistant then creates and edits flows through the local API and they appear in your list. You can also **Copy the skill** and save it as `.claude/skills/tammoa-flows/SKILL.md` in another project, so an assistant knows Tammoa before you give it the address.
- It **cannot run flows** unless you switch on *Let the agent run flows* (running starts scripts, web requests and AI models on your computer). It cannot delete flows, change connections or see their keys, and it cannot change these switches.
- *Make a new token* cuts off an assistant that has the old one. The address changes when Tammoa starts on another port: copy again.
- Tammoa's local API stays reachable by programs on your computer, as it always was; the token scopes an assistant, it is not a lock on the app.
- **For developers:** when you add or change an API route, update `Services/AgentApi.cs` (a test fails if a route is missing) and, if it changes how flows are built, `Tammoa/AgentSkill/tammoa-flows/SKILL.md`.

## Building and running a flow
1. Open a flow. **Add card** lists the card types: *Manual trigger*, *Script*, *Set value*.
2. Drag cards where you like, drag from a card's right dot onto another card to connect them, click a card to edit its settings on the right. **Save**.
3. Type something in *Run input* (text or JSON, optional) and press **Run**. The backend executes the flow; cards show running / succeeded / failed / skipped live, and clicking a card shows its output or error. **Cancel** stops a run. You can start several runs at once, even of the same flow.
- Data moves from card to card: use `{{input.stdout}}` (the previous card's output) and `{{run.input}}` in card settings. Put values in shell commands with `{{input.text | sh}}` so quotes and spaces cannot break the command.
- Everything runs in the backend, the window only displays the state. Run history is kept in memory (lost when the app closes); **Clear** in the run list removes finished runs, and each flow keeps its newest 50.
- The side panel can be dragged wider; code and long text open in a larger editor or viewer. Hover shortened names to read them in full.
- **AI tools (Ollama card):** tick tools the model may use (read file, write file, HTTP request, run script, check web page). File and script tools only work inside the card's *Tools folder*; HTTP only to its *Allowed hosts*; an optional *Approver model* must say yes to every call (it only judges safety; you can rewrite its instructions under *Approver instructions*; tiny models such as qwen3:0.6b are weak judges, prefer 4B or larger). Refused calls are explained to the model (it is told the call was NOT done and who refused), which can try something else; the output lists every call (`toolCalls`). Needs a model with tool support (e.g. qwen3). *Run script* is a best-effort guard, not a sandbox. *Check web page* opens an `.html` file of the Tools folder in a headless Chrome / Edge / Chromium / Brave (must be installed; `TAMMOA_BROWSER` picks one) and returns its console errors, so the model can fix what it wrote; the page may reach the internet (so a CDN script loads); tick *Limit web pages to the allowed hosts* to restrict it to the *Allowed hosts* (then add `cdn.jsdelivr.net` for a Phaser page). An identical file write repeated right after itself is skipped and the model is told to finish. In the card's settings, tick the tools; the folder, hosts, steps and approver fields appear only when they matter. The Output tab lists every tool call (ran / refused / failed) with its arguments and result, live while the card runs: first "Waiting for the model" with a timer (a model sends nothing while it writes a tool call; turn Reasoning on to watch it think), then each call as it arrives, with its stage (queued, asking approver, running) until it is done.
- Not built yet: schedule and webhook triggers, OpenAI / Anthropic connections.

## Loops and card size
- **Loop:** drag from a card's right dot back onto a card that comes before it. The line is dashed and labelled *loop*; the target runs again with the new output as its input, and the cards after the loop's exit (the IF *false* side) wait until it is over. A card hung off a normal card inside the loop (e.g. Write file after a JSON card) runs every round. End a loop with an **IF** card (its *true* branch goes back, *false* goes on). A loop with no end is stopped at the **loop limit** (field in the toolbar, default 50 runs per card) with a clear message.
- Cards that ran more than once show `×N`; in the side panel use the round arrows to read what each earlier round got and produced (last 10 kept).
- **Card size:** cards grow to show their whole title. Drag the small handle at a card's bottom-right corner to resize it; double-click the handle to return to automatic.

## Selecting, grouping and copying cards
- **Select several:** Shift-click cards, or hold Shift and drag a rectangle on the empty canvas (Ctrl/Cmd+A selects all). Drag any selected card to move them all together.
- **Group:** select two or more cards and press the group button in the toolbar; type a name for the group there. Click the group's name bar to select the group (drag it to move all its cards). *Ungroup* keeps the cards.
- **Copy:** the copy button (or Ctrl/Cmd+D) duplicates the selected cards **without** their connections. Copying a **group** also copies the connections inside it. A Data card's stored text is not copied.
- **Delete:** deleting a card, several cards or a group (with its cards) always asks first. Nothing is final until you press Save.

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
| backend  | `Tammoa` | `dotnet run` | 8000, or a random free port | `http://localhost:<port>/api/health`; the port is printed (`API: …`) and written to `$TMPDIR/tammoa-api-port` |

First time: `npm install` in `Tammoa/UserInterface`.
Logs: `$TMPDIR/tammoa-frontend.log`, `$TMPDIR/tammoa-backend.log`
If 8000 is taken (another app), the Debug backend tries up to 5 random free ports and gives up with a message if none works (`TAMMOA_PORT` sets the preferred port). The Vite proxy follows the port file, so nothing else needs configuring; release builds bind no fixed port. `dotnet run` (Debug) opens the native window pointed at the Vite dev server, so start the frontend first.

## Stop locally
Use the `stop-test` skill. Ports: 5173, 8000 or the one in `$TMPDIR/tammoa-api-port`. Process match: `bin/Debug/net8.0/Tammoa`.

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
TBD — run `setup-deploy`. Ready for it: the app icon (`Tammoa/AppIcon/app.ico`, `app.icns`), the update source (`RepositoryUrl` in `Tammoa/Tammoa.csproj`: the update check reads the latest **GitHub release** of that repository, so the version of a release tag, for example `v0.2.0`, must be higher than `<Version>`).

## Versioning
Single source of truth: `<Version>` in `Tammoa/Tammoa.csproj` — frontend `package.json` version is not used.
