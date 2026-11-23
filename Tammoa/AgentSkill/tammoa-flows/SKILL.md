---
name: tammoa-flows
description: Build, check and (when the user allows it) run Tammoa automation flows by talking to the running Tammoa app over its local API. Use when the user asks to create, change or explain a Tammoa flow, or gives you a Tammoa address and token.
---

# Tammoa flows

Tammoa is a desktop app where an automation is a **flow**: **cards** (steps) joined by **connections**. You control the running app through its local HTTP API, so the flow appears in the user's window. This skill tells you how.

The user gives you two things (Tammoa > **Agent access** > *Copy for the agent*): the **address** (for example `http://localhost:8000`) and a **token**. Send the token on every request: `Authorization: Bearer <token>`. If you have no address and token, ask for them; do not guess a port.

The last part of this file ("This Tammoa") is added by the running app: the exact routes you may call and the live card catalog. **Trust it over this page**, and fetch a fresh copy with `GET <address>/api/agent/skill` whenever the app may have been updated.

## Rules

1. **Never run a flow unless the user asked you to run it.** Running starts scripts, web requests and AI models on their computer. Creating and editing flows is safe; running is a separate permission (a switch in the app) and a separate decision.
2. **Never put secrets in a flow.** AI cards refer to a saved **connection** by its id; you list connections with `GET /api/connections` and never see keys. If the connection you need does not exist, tell the user to add it under *Connections*.
3. **Do not invent what the user did not say.** Where something is unclear (a folder, a model, a URL), ask, or leave an obvious placeholder and say so.
4. **Say what you did** in plain words: the flow name, what each card does, what the user must do (open it, pick a connection, press Run).
5. If a request answers 401 or 403, read the `error` text: it says what the user must switch on. Do not try to work around it.

## Workflow

1. `GET /api/health` to check the address works.
2. `GET /api/cards/types` (or the catalog below): the card `type` strings, their parameters and outputs. Use only these.
3. If a card needs a connection or a model: `GET /api/connections`, then `GET /api/cards/options/ollama.models?connectionId=<id>` for the model names. Do not invent model names.
4. Draft the definition (see "The definition").
5. `POST /api/flows/validate` with `{ "definition": … }`. Fix what it says until it answers `{ "valid": true }`. (It checks the shape: known card types, unique ids, connections between existing cards, valid outputs. A wrong parameter value shows up at run time.)
6. `POST /api/flows` with `{ "name": "…", "description": "…" }` to create the flow, then `PUT /api/flows/<id>` with `{ "name", "description", "definition" }`. (To change an existing flow: `GET` it, change the definition, `PUT` it back; keep the cards you were not asked to touch.)
7. Tell the user the flow is ready and where to find it (*Flows* list).
8. Only if they asked you to run it and the app allows it: `POST /api/flows/<id>/runs` (optional body `{ "input": … }`), then poll `GET /api/runs/<runId>` until `status` is not `running`. Report each card's output or error.

## The definition

```json
{
  "nodes": [
    { "id": "start", "type": "trigger.manual", "name": "Start", "position": { "x": 0, "y": 0 }, "params": {} },
    { "id": "ask", "type": "ai.ollama", "name": "Summarize", "position": { "x": 280, "y": 0 },
      "params": { "connection": "<connection id>", "model": "<a model name>", "prompt": "Summarize in one sentence:\n{{run.input}}", "think": "off" } }
  ],
  "edges": [
    { "id": "e1", "source": "start", "target": "ask" }
  ]
}
```

- **A flow needs one trigger card** (`trigger.manual`) to start; a trigger has no incoming connection.
- **Card `id`**: 1 to 64 characters, unique in the flow. Use readable ones (`start`, `ask`, `save`). `name` is what the user sees (max 100 characters).
- **Layout**: `position` is the card's top-left in pixels. Put cards left to right about 280 px apart (`x` = 0, 280, 560 …); a second branch goes below (`y` += 160). Leave the flow tidy: it opens exactly like this.
- **Connections** (`edges`): `source` and `target` are card ids; a card cannot connect to itself; no duplicates. A card with several outputs (IF has `true` and `false`) needs `"sourceHandle": "true"`; others use the default `main`.
- **Loops**: a connection that goes back to an earlier card makes a loop (typically from an IF output). The engine stops a loop at the loop limit (default 50 rounds per card).
- **Optional**: `"settings": { "loopLimit": 50 }` in the definition (1 to 1000). Skip `groups` and `size`.

## Passing data between cards

Settings can contain `{{ … }}` references, replaced when the card runs:
- `{{input.<field>}}` — what the previous card produced (a Script card printing JSON gives `{{input.json.x}}`; plain text is `{{input.stdout}}`; an AI card's answer is `{{input.text}}`).
- `{{run.input}}` — what the user typed in *Run input* (or the `input` you pass when starting a run).
- `{{data.Name}}` — the text of a **Data** card called `Name`, readable by every card without a connection.
- Paths use dots and `[n]` (`input.items[0].name`). A missing path is empty text.
- Filters: `{{input.text | json}}` (compact JSON text), `{{input.text | sh}}` (safe inside a shell command: always use it for values put in shell code).
- Script cards read their input from code: JavaScript `input` and `runInput`, Python `input_data` and `run_input`, shell `$TAMMOA_INPUT`.

## AI (Ollama) cards

- Needs a **connection** and a **model** that exists on it. Reasoning (`think`) `off` is much faster for simple tasks.
- **Tools are off by default** (`tools` empty): the card only answers. Tick tools only when the user asks for an AI that acts. Then `toolRoot` (an absolute existing folder) limits the file and script tools, `allowedHosts` limits web access, and an optional `approverModel` must approve every call. Choose a model that supports tool calling (for example `qwen3`); tell the user the folder you set.
- A card that writes web pages can use the `check_page` tool to find its own errors.
- Local models are slow: give long tasks a generous `timeoutSeconds` (silence timeout) and mention it to the user.

## Good practice

- Keep flows small and named for what they do. One idea per card.
- Prefer a **Set value** card to hold fixed settings (URLs, names) at the start, instead of repeating them.
- Put a **Wait** or an **IF** only when the user wants it.
- After you create or change a flow, re-read it with `GET /api/flows/<id>` if anything failed; do not assume.
- If an API answer is an error, show the user the message; do not retry blindly.
