# Roadbed.Logging.Mcp

A **read-only** [Model Context Protocol](https://modelcontextprotocol.io) (MCP) server that lets an
AI "Log Analyst" agent triage and analyze the **Roadbed.Logging** activity + log data across a fleet
of applications that share a `logging` schema (MariaDB/MySQL).

It is read-only (only `SELECT`), token-efficient (aggregation-first), partition-pruning aware, and
safe: the agent gets curated query *capabilities*, never raw DB access or credentials.

See [docs/implementation-plan.md](docs/implementation-plan.md) for the design.

---

## Tool catalog

| Tool | Purpose |
| --- | --- |
| `catalog` | Per-application footprint: activity types, top keys, run count, first/last seen. |
| `fleet_overview` | Per-application (optionally per-type) health rollup. |
| `activities_recent_failures` | Newest failed/canceled runs across the fleet. |
| `activities_list` | Filtered, keyset-paginated run list. |
| `activity_get` | One full run (parsed JSON, log-level counts, lineage counts). |
| `activity_log_summary` | Cheap triage of a run's logs before pulling lines. |
| `activity_logs` | A run's raw log lines (keyset-paginated). |
| `activity_lineage` | Provenance edges (inputs/outputs) resolved to summaries. |
| `activity_history` | One recurring workload over time, with min/avg/p95/max duration. |
| `logs_search` | Cross-activity log search; each row carries `activity_id` to pivot. |
| `run_readonly_query` | **Optional, off by default.** Guarded ad-hoc `SELECT`. |

All timestamps are ISO-8601 UTC (`…Z`); statuses and levels are names; durations are precomputed.
Free-text fields (`error`/`message`/`exception`) are truncated to a configurable length with a
`*_truncated` marker and a `full=true` escape.

---

## Prerequisites

- **.NET 10 SDK** (the project targets `net10.0`).
- A reachable **MariaDB 10.5+ / MySQL 8.0+** server hosting the Roadbed.Logging `logging` schema
  (tables `activity`, `activity_input`, `log_entries`). This server only **reads** that schema; it
  never creates, migrates, or writes it.

---

## Step 1 — Create a read-only database account

The server should connect as a dedicated account that can only `SELECT` from the logging schema.
On MariaDB/MySQL:

```sql
CREATE USER 'loganalyst_ro'@'%' IDENTIFIED BY 'choose-a-strong-password';
GRANT SELECT ON logging.* TO 'loganalyst_ro'@'%';
FLUSH PRIVILEGES;
```

Granting only `SELECT` (and withholding `FILE`) is the authoritative backstop: even the optional
ad-hoc tool cannot write or read files through this account.

## Step 2 — Publish the server to a folder

The project is configured to **always publish as a single file** (the `win-x64` runtime identifier
and single-file settings are baked into the `.csproj`), so *any* publish produces one
`Roadbed.Logging.Mcp.exe`. In Visual Studio, right-click the project → **Publish** with any Folder
profile and click **Publish**.

Equivalent from the CLI:

```powershell
dotnet publish src\Roadbed.Logging.Mcp\Roadbed.Logging.Mcp.csproj -c Release -o C:\Tools\Roadbed.Logging.Mcp
```

This is **framework-dependent**, so the host needs the **.NET 10 runtime** installed (the published
exe is small). For a fully standalone build that needs no installed runtime, set
`<SelfContained>true</SelfContained>` in the `.csproj` (larger exe). To target a different
architecture, change `<RuntimeIdentifier>` (e.g. `win-arm64`).

The entry point is `C:\Tools\Roadbed.Logging.Mcp\Roadbed.Logging.Mcp.exe` — run it directly (no
`dotnet` launcher needed).

## Step 3 — Create the external configuration file

Credentials live **outside** the agent's workspace. On startup the server reads a file named
**`.Roadbed.Logging.Mcp`** in the **current user's home directory** — it grabs the current user from
the environment and looks under `C:\Users\{user}\.Roadbed.Logging.Mcp`.

Create `C:\Users\<you>\.Roadbed.Logging.Mcp` (a JSON file, no extension) with your read-only
connection string:

```json
{
  "sources": [
    {
      "name": "primary",
      "connectionString": "Server=your-db-host;Database=logging;User ID=loganalyst_ro;Password=your-password;",
      "default": true
    }
  ],
  "limits": { "maxActivities": 200, "maxLogRows": 500, "textTruncateChars": 500 },
  "features": { "adHocQuery": false },
  "defaultWindowDays": { "lists": 7, "logs": 1, "history": 90, "overview": 7 }
}
```

Notes:
- `sources` is a **list** — add more `{ name, connectionString }` entries to query additional logging
  databases without code changes. Exactly one source must be `"default": true` (or configure a single
  source). Every tool takes an optional `source` argument; omitting it uses the default.
- The server hardens each connection string at startup: `AllowLoadLocalInfile` is forced off, a
  default command timeout is applied, and the connect timeout is capped (~10s) so an unreachable
  source fails fast.
- Set `features.adHocQuery` to `true` only if you want the `run_readonly_query` tool registered.
- Do **not** commit this file. No credentials live in this repository or in `.mcp.json`.

## Step 4 — Register the server with the agent

Add the server to the consuming agent's `.mcp.json` (no secrets, and no config path needed — the
server finds `.Roadbed.Logging.Mcp` in the current user's home directory on its own):

```json
{
  "mcpServers": {
    "roadbed-logging": {
      "command": "C:\\Tools\\Roadbed.Logging.Mcp\\Roadbed.Logging.Mcp.exe"
    }
  }
}
```

## Step 5 — Verify

Start the agent (or any MCP client) pointed at the server and list tools. You should see the 10
always-on tools above (11 when `adHocQuery` is enabled). The server communicates over **stdio**:
stdout is the protocol channel and **all diagnostics go to stderr**, so nothing pollutes the stream.

A quick manual check without an agent (reads `~\.Roadbed.Logging.Mcp` automatically) — pipe a
JSON-RPC handshake to the exe:

```powershell
@'
{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2024-11-05","capabilities":{},"clientInfo":{"name":"c","version":"1"}}}
{"jsonrpc":"2.0","method":"notifications/initialized"}
{"jsonrpc":"2.0","id":2,"method":"tools/list"}
'@ | & C:\Tools\Roadbed.Logging.Mcp\Roadbed.Logging.Mcp.exe
```

The `tools/list` response (on stdout) enumerates the registered tools.

---

## A suggested triage flow for the analyst

1. `fleet_overview` (and `activities_recent_failures`) for a one-call fleet picture.
2. `activities_list` to find runs of interest (each row carries `created_on`).
3. `activity_get` (pass `created_on` to prune to one partition) for the full record.
4. `activity_log_summary` → `activity_logs` to read the narrative.
5. `activity_lineage` to walk upstream/downstream, `activity_history` to spot regressions,
   `logs_search` to find cross-run errors and pivot back via `activity_id`.

---

## Behavior & design notes

- **Schema assumption.** Built for the partitioning/retention release where `activity` and
  `activity_input` are **monthly-partitioned on `created_on`** with `created_on` indexes. Activity
  tools filter/sort on `created_on`; log tools on `event_time_utc`. (If your deployment indexes
  activity on `started_on` instead, the activity time filters would need to switch columns.)
- **Retention awareness.** Activity/activity_input ~12 months; `log_entries` ~90 days. `activity_history`
  caps its window to ~12 months; log tools add a `note` when the requested window predates the
  ~90-day horizon.
- **Percentiles.** `fleet_overview` leaves `p95_duration_ms` null (cheap rollup); true min/avg/p95/max
  come from `activity_history` for a single workload.
- **By-id pruning.** Activity IDs are ULIDs; `activity_get`/`activity_lineage` derive a `created_on`
  prune window from the ULID timestamp even when no hint is passed.
- **Reads run without retries** and with a bounded connect timeout, so a down/misconfigured source
  returns a structured error quickly rather than hanging.
- **No secrets in output.** Errors are structured (`{ "error": ..., "argument": ... }`) and never
  include connection strings or credentials.

## Development

```powershell
dotnet build src\Roadbed.Logging.Mcp\Roadbed.Logging.Mcp.csproj -c Release
```

The project builds clean (0 warnings) under the strict analyzer stack
(StyleCop + SonarAnalyzer + NetAnalyzers, `TreatWarningsAsErrors`). Analyzer tuning lives in
`src/.editorconfig` and `src/Roadbed.Logging.Mcp/stylecop.json`.

Tools are registered from an explicit type list in `Program.cs` (rather than assembly scanning) so the
optional `run_readonly_query` tool can be gated on `features.adHocQuery`.
