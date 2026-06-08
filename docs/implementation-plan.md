# Roadbed.Logging.Mcp — Implementation Plan

A read-only **Model Context Protocol (MCP) server** that lets an AI "Log Analyst"
agent triage and analyze the **Roadbed.Logging** activity + log data across a
fleet of applications (10+ apps log into the shared `logging` schema). It is the
analyst's first line of defense: find where things aren't running well, explain
why, and produce prompts the human hands to other coding agents.

The server is **strictly read-only**, **token-efficient** (aggregation-first),
and **safe** (the agent gets curated query *capabilities*, never raw DB access or
credentials).

---

## 1. Scope & principles

- **Read-only.** Only `SELECT`. No DDL/DML, ever. Enforced by both the DB account
  and the server.
- **Aggregation-first.** The agent should be able to triage a whole fleet in a few
  hundred tokens (summaries/rollups) before pulling any raw rows. Every "list"
  returns lean rows; every "detail" is one record; dedicated summary tools exist.
- **Partition-pruning aware.** The underlying tables are monthly-partitioned;
  every tool filters on the partition key so queries stay fast (see §4).
- **UTC everywhere.** Every stored timestamp is UTC; the server emits ISO-8601
  `…Z`. No timezone conversion needed.
- **Multi-source.** Designed for one shared `logging` database today, but the
  config is a *list* of sources so additional logging DBs/servers can be added
  without code changes.

---

## 2. What it queries (Roadbed.Logging schema — do NOT create or migrate it)

The schema is owned by **Roadbed.Logging** and installed by the operator. This
server only reads three tables in a `logging` database (MariaDB/MySQL). The
relevant shape (final, as of the partitioning/retention release):

### `activity` — one row per run instance (job / pipeline / ad-hoc work)
Key columns: `id CHAR(26)` (ULID, PK prefix), `parent_activity_id`,
`root_activity_id`, `trace_id CHAR(32)`, `span_id CHAR(16)`, `activity_key`,
`application`, `environment`, `activity_type`, `target`,
`status ENUM('pending','running','succeeded','failed','canceled','skipped')`,
`started_on`, `completed_on`, `last_heartbeat_on`, `records_impacted`,
`parameters JSON`, `metrics JSON`, `error TEXT`, `error_type`, the Quartz/host
fields (`scheduler_instance_id`, `fire_instance_id`, `quartz_*`, `host`,
`process_id`), `created_by`, `created_on`, `last_modified_on`.
- **PRIMARY KEY `(id, created_on)`**; **partition key `created_on`**.
- Retention: **12 months** (operator drops old partitions).

### `activity_input` — lineage DAG ("this activity consumed those upstream activities")
`activity_id CHAR(26)`, `input_activity_id CHAR(26)`, `input_role VARCHAR(50)`,
`created_on`.
- **PRIMARY KEY `(activity_id, input_activity_id, created_on)`**; partition key
  `created_on`. Soft references to `activity.id` (no FKs). Retention 12 months.

### `log_entries` — high-volume append-only MEL log narrative
`id BIGINT`, `event_time_utc`, `recorded_on`, `log_level TINYINT`, `category`,
`event_id`, `event_name`, `message TEXT`, `message_template`, `properties JSON`,
`exception TEXT`, `exception_type`, `activity_id`, `trace_id`, `span_id`,
`application`, `environment`, `host`, `process_id`.
- **PRIMARY KEY `(id, event_time_utc)`**; **partition key `event_time_utc`**.
- Retention: **~90 days** (operator drops old partitions).

### `log_level` integer → name (Microsoft.Extensions.Logging)
`0 Trace · 1 Debug · 2 Information · 3 Warning · 4 Error · 5 Critical · 6 None`.
Tools accept/return **names**; a `min_level` filter translates to `log_level >= n`.

### Indexes the tools rely on (Roadbed-owned — the server must NOT create indexes)
- activity: `idx_activity_app_created (application, created_on)`,
  `idx_activity_app_status_created (application, status, created_on)`,
  `idx_activity_key_created (activity_key, created_on)`,
  `idx_activity_status_created (status, created_on)`,
  `idx_activity_type_created (activity_type, created_on)`,
  plus `idx_activity_parent/root/trace/fire`.
- activity_input: PK `(activity_id, input_activity_id, created_on)` (forward),
  `idx_activity_input_reverse (input_activity_id)` (reverse).
- log_entries: `idx_log_activity (activity_id, event_time_utc)`,
  `idx_log_app_time (application, event_time_utc)`,
  `idx_log_app_level_time (application, log_level, event_time_utc)`,
  `idx_log_level_time (log_level, event_time_utc)`, `idx_log_time`,
  `idx_log_trace`.

---

## 3. Architecture & tech

- **Runtime:** `net10.0` console **Exe** (already scaffolded). Strict analyzer
  stack is on (StyleCop/Sonar/NetAnalyzers, `TreatWarningsAsErrors`) — write
  analyzer-clean code: full XML-doc comments on public members, no
  `TODO`/`FIXME`/`HACK` tokens, ordered modifiers, static-before-instance, etc.
- **MCP:** the official C# SDK, NuGet **`ModelContextProtocol`**, hosted via
  `Microsoft.Extensions.Hosting`. Use **stdio** server transport. Tools are
  methods annotated with `[McpServerTool]` + `[Description]`, discovered with
  `.WithToolsFromAssembly()` (verify exact API against the installed SDK version;
  it is evolving). Register tool classes + the data layer in DI.
- **Data access:** reuse **Roadbed.Data** + **Roadbed.Data.Dapper** +
  **Roadbed.Data.MySql** (vendored DLLs copied into this repo, mirroring how
  `Pebble.*` consumes them) plus their runtime deps **Dapper** + **MySqlConnector**.
  Implement a small per-source connection factory (one `DataConnecionString`
  of type `MySQL` per configured source) and run reads through
  `MySqlExecutor.QueryAsync<T>` / `QuerySingleOrDefaultAsync<T>`. See the
  `code-roadbed-csharp` skill in the Roadbed repo for the exact usage pattern.
  *(Plain Dapper + MySqlConnector is an acceptable lighter alternative if you
  prefer fewer deps, but Roadbed.Data keeps it consistent with the stack.)*
- **Own read DTOs.** Define lean read models in this project (do **not** depend on
  `Roadbed.Logging`'s write entities) so the server controls shape, truncation,
  and UTC stamping and isn't coupled to internal entity changes.
- **stdio gotcha:** in a stdio MCP server, **stdout is the protocol channel** —
  the server's own diagnostics must go to **stderr** (or a file), never stdout.
  Do not route the server's logs into the `logging` DB (read-only + avoid noise).

---

## 4. Cross-cutting query conventions (apply to every tool)

- **Time filters map to the partition key.** Activity-oriented tools filter/sort
  on **`created_on`**; log-oriented tools on **`event_time_utc`**. This is what
  makes partition pruning work — never filter activity time on `started_on`
  (return `started_on`/`completed_on`/`duration_ms` for display only).
- **`created_on` drill hints.** A bare lookup by `id` can't prune (partition key
  absent) and probes all ~120 partitions. So: every list/overview/catalog tool
  **returns `created_on`**, and by-id tools (`activity_get`, `activity_lineage`,
  `activity_logs`) accept an **optional `created_on` hint** (or a date) to prune
  to one partition. The agent gets this for free from the preceding list call.
- **Defaults & caps** (so the agent can't accidentally scan everything):
  - When `since`/`until` are omitted, apply a sensible default window per tool
    (stated below).
  - `limit` defaults are modest with hard ceilings; **keyset cursor** pagination
    (`cursor` opaque token encoding the last `(partition_key, id)`).
  - Truncate `error` / `message` / `exception` to ~500 chars with an explicit
    `"<field>_truncated": true` marker and a `full=true` escape to fetch untruncated.
- **Output:** each tool returns **compact JSON** (an object or array of the DTOs
  in §7). Enums (`status`, `log_level`) are **names**. Durations are precomputed
  `duration_ms` (and a human `duration` string). All timestamps ISO-8601 `…Z`.
- **Retention awareness:**
  - `activity_history` and any long-range activity query is bounded to ~12 months
    — say so in the tool description; don't promise multi-year trends.
  - Log tools see only ~90 days. If a requested log window predates the
    ~90-day horizon, return an explicit `note` ("window predates log retention")
    rather than an empty result the agent could misread as "clean run." The
    activity itself may still resolve (12-month retention) even when its logs are gone.
- **Errors:** return structured tool errors (message + which arg was invalid).
  **Never** include connection strings or credentials in any output.

---

## 5. Security & configuration

- **DB account:** a dedicated **read-only** service account with privileges on the
  `logging` schema(s) only (`SELECT`). The server additionally rejects any
  non-`SELECT` text in the optional ad-hoc tool (§6).
- **Config location — outside the agent's reach.** The connection config lives in
  a file the C# server reads but the agent's workspace cannot. The server grabs the
  current user from the environment and reads `.Roadbed.Logging.Mcp` in that user's
  home directory (`C:\Users\{user}\.Roadbed.Logging.Mcp`). **No credentials live in
  this repo or in `.mcp.json`.**
- **Config schema (example):**
  ```json
  {
    "sources": [
      {
        "name": "primary",
        "connectionString": "Server=...;Database=logging;User Id=loganalyst_ro;Password=...;",
        "default": true
      }
    ],
    "limits": { "maxActivities": 200, "maxLogRows": 500, "textTruncateChars": 500 },
    "features": { "adHocQuery": false },
    "defaultWindowDays": { "lists": 7, "logs": 1, "history": 90, "overview": 7 }
  }
  ```
- **MCP registration (`.mcp.json` in the agent's repo — no secrets, no config path):**
  ```json
  {
    "mcpServers": {
      "roadbed-logging": {
        "command": "C:\\Tools\\Roadbed.Logging.Mcp\\Roadbed.Logging.Mcp.exe"
      }
    }
  }
  ```
- Every tool takes an optional `source` (defaults to the `default: true` source).
  `application` is a separate filter dimension *within* a source.

---

## 6. Tool catalog

Each tool lists its purpose, parameters (with the column each filters), and the
returned DTO. All `since`/`until` are ISO-8601 UTC; all tools accept optional
`source`. "→" shows the returned shape (DTOs defined in §7).

### Orientation
- **`catalog`** — what exists, to orient the agent + seed its knowledge base.
  Params: `source?`, `since?`/`until?` (on `created_on`, default last 30d).
  → per `application`: list of `{ application, activity_types[], activity_keys[] (top N by count), run_count, first_seen_utc, last_seen_utc }`.

### Fleet triage (daily driver)
- **`fleet_overview`** — per `(application[, activity_type])` health rollup.
  Params: `source?`, `since?`/`until?` (`created_on`, default last 7d),
  `application?`, `group_by` = `application` | `application_type` (default `application`).
  → rows: `{ application, activity_type?, runs, succeeded, failed, canceled, running, success_rate, avg_duration_ms, p95_duration_ms, total_records_impacted, last_run_utc, last_failure_utc }`.
- **`activities_recent_failures`** — newest failed/canceled runs across the fleet.
  Params: `source?`, `since?`/`until?` (`created_on`, default last 7d),
  `application?`, `activity_key?`, `limit` (default 25, max from config).
  → `ActivitySummary[]` + truncated `error`/`error_type`.

### List / drill
- **`activities_list`** — filtered run list. Params: `source?`, `since?`/`until?`
  (`created_on`, default last 7d), `application?`, `environment?`,
  `activity_type?`, `activity_key?`, `status?`, `target_contains?`,
  `limit` (default 50), `cursor?`. → `ActivitySummary[]` + `next_cursor`.
- **`activity_get`** — one full run. Params: `id` (required), `created_on?`
  (prune hint), `source?`. → `ActivityDetail` (full record, parsed
  `parameters`/`metrics`, full `error`, `log_level_counts`, `input_count`,
  `output_count`).
- **`activity_log_summary`** — cheap triage of a run's logs *before* pulling lines.
  Params: `activity_id` (required), `created_on?`/window hint, `source?`.
  → `{ total, counts_by_level{}, first_event_utc, last_event_utc, top_categories[{category,count}], exception_types[{type,count,sample_message}] }`.
- **`activity_logs`** — a run's raw log lines. Params: `activity_id` (required),
  `min_level` (default `Information`), `category_contains?`, `message_contains?`,
  `exceptions_only` (default false), `created_on?`/`started_on?`/`completed_on?`
  (window hints used to bound `event_time_utc` for pruning), `limit` (default 200),
  `cursor?`, `order` = `asc`|`desc` (default `asc`). → `LogEntry[]` + `next_cursor`
  (+ a `note` if the window predates the ~90-day horizon).

### Lineage
- **`activity_lineage`** — provenance edges resolved to summaries. Params: `id`
  (required), `direction` = `inputs`|`outputs`|`both` (default `both`),
  `depth` (default 1, max 3), `created_on?` (prune hint), `source?`.
  → `{ inputs[], outputs[] }` of `{ role, activity_id, activity_type, target, status, started_on_utc, records_impacted }`.

### Cross-run analysis
- **`activity_history`** — one recurring workload over time (regression hunting).
  Params: `activity_key?` **or** `activity_type?` (one required), `application?`,
  `source?`, `since?`/`until?` (`created_on`, default last 90d, capped to ~12mo
  retention), `limit` (default 30). → `{ runs: ActivitySummary[], stats: { count, success_rate, min_duration_ms, avg_duration_ms, p95_duration_ms, max_duration_ms, avg_records_impacted } }`.
- **`logs_search`** — cross-activity log search. Params: `source?`, `since?`/`until?`
  (`event_time_utc`, default last 24h), `min_level` (default `Warning`),
  `application?`, `category_contains?`, `message_contains?`, `exception_type?`,
  `limit` (default 100), `cursor?`. → `LogEntry[]` (each carrying `activity_id`
  so the agent can pivot to `activity_get`) + `next_cursor`.

### Optional escape hatch (config-gated, default OFF)
- **`run_readonly_query`** — a guarded ad-hoc `SELECT`. Only registered when
  `features.adHocQuery = true`. Params: `sql` (required), `source?`,
  `max_rows` (default 200, hard cap). Server validation: single statement, must
  start with `SELECT`/`WITH`, reject `;`-chained statements and any
  DDL/DML/`INTO`/comment-smuggling; wrap in a `LIMIT`; enforce a statement
  timeout. → `{ columns[], rows[][], row_count, truncated }`. *(Recommended:
  ship it but default off; the read-only account is the backstop.)*

---

## 7. DTO reference (lean read models)

```text
ActivitySummary {
  id, created_on_utc, application, environment, activity_type, activity_key,
  target, status, started_on_utc, completed_on_utc, duration_ms, duration,
  records_impacted, error_type, host, source
}

ActivityDetail : ActivitySummary +
  parent_activity_id, root_activity_id, trace_id, span_id,
  last_heartbeat_on_utc, parameters (object), metrics (object),
  error (full|truncated + error_truncated), created_by,
  scheduler_instance_id, fire_instance_id, quartz_job_name, quartz_job_group,
  quartz_trigger_name, quartz_trigger_group, process_id,
  log_level_counts {trace,debug,information,warning,error,critical},
  input_count, output_count

LogEntry {
  id, event_time_utc, level (name), category, event_id, event_name,
  message (+ message_truncated), message_template, properties (object|null),
  exception_type, exception (+ exception_truncated when full=false),
  activity_id, trace_id, span_id, application, host, source
}
```
Notes: `duration_ms` = `completed_on − started_on` (null if not completed);
`level`/`status` always names; timestamps `…Z`; `properties`/`parameters`/
`metrics` parsed from JSON columns into objects.

---

## 8. Suggested project structure

```
src/Roadbed.Logging.Mcp/
  Program.cs                      // Host builder, DI, AddMcpServer().WithStdioServerTransport().WithToolsFromAssembly()
  Configuration/                  // McpConfig, SourceConfig, LimitsConfig + loader (reads the external config file)
  Data/                           // ISourceConnectionFactory + MySQL factory per source; SQL builders (prune-aware)
  Models/                         // ActivitySummary, ActivityDetail, LogEntry, rollup DTOs, cursor codec
  Tools/                          // one class per tool group; [McpServerTool] methods returning JSON
  Lib/                            // log-level mapping, truncation, ISO-8601 helpers, keyset cursor encode/decode
docs/implementation-plan.md       // this file
```
Vendor `Roadbed.Common.dll`, `Roadbed.Data.dll`, `Roadbed.Data.Dapper.dll`,
`Roadbed.Data.MySql.dll` (from the Roadbed build) into the repo and reference via
`<Reference HintPath="…">`; add NuGet `ModelContextProtocol`,
`Microsoft.Extensions.Hosting`, `Dapper`, `MySqlConnector`.

---

## 9. Testing

- **Unit:** SQL builders produce prune-aware, parameterized SQL (time filter on
  the correct partition key; cursor keyset correct); `min_level` → threshold;
  truncation + `full` flag; cursor encode/decode round-trips; the
  `run_readonly_query` validator accepts representative `SELECT`/`WITH` and
  rejects DDL/DML/multi-statement/comment-smuggling. Use the same MSTest+Moq
  conventions as the rest of the stack.
- **Integration (`[Ignore]` by default):** against a real `logging` DB — assert
  each tool returns expected shapes and that queries prune (e.g.
  `EXPLAIN PARTITIONS`).

---

## 10. Done criteria

- Builds clean under the strict analyzer stack (0 warnings).
- stdio server starts, lists all tools, reads the external config, connects to the
  default source read-only, and **writes nothing** to the DB.
- `fleet_overview` + `activities_recent_failures` give a fleet triage in one call
  each; `activity_get` → `activity_log_summary` → `activity_logs` drill works with
  `created_on` prune hints; `activity_lineage` resolves Silver→Bronze-style edges;
  `activity_history` shows a per-`activity_key` trend; `logs_search` finds
  cross-run errors and returns `activity_id` to pivot.
- Server diagnostics go to stderr/file (never stdout); no secrets in any output.

---

## 11. Assumptions / open decisions (override as needed)

1. **One shared `logging` database** assumed as the default source; the config is
   a list, so additional sources can be added without code changes.
2. **`run_readonly_query` is shipped but default-OFF** (`features.adHocQuery:false`).
   Flip on per environment if you want the analyst to run bespoke `SELECT`s.
3. **`fleet_overview` + `catalog` are included** (the fleet-triage entry points).
4. **`net10.0`, stdio transport, project/solution names as scaffolded.**
5. Retention windows reflected in tool behavior: **activity/activity_input 12
   months, log_entries ~90 days** (operator-enforced via partition drops).
