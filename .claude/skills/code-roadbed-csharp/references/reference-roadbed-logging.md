# Roadbed.Logging Reference

A self-contained library that persists structured `Microsoft.Extensions.Logging`
output to a relational database and tracks the **activities** (run instances of
jobs, pipelines, ad-hoc work) that those log rows tie back to. OpenTelemetry-
first: MEL flows through the OTel logging pipeline, and database persistence is
one exporter — adding OTLP export (Grafana / Tempo / Jaeger / etc.) later is a
configuration add, not a rewrite.

Activity rows are mutable; log entries are append-only and flushed off the
hot path by a bounded channel + background writer. The library has **no
dependency on `Roadbed.Crud`** — its bulk-insert path is internal, custom, and
stamps each row with its own originating `activity_id` rather than a uniform
activity id like the CRUDALBT "B" tier.

## Type catalog (17 types)

| Group                       | Types                                                                                                  |
| --------------------------- | ------------------------------------------------------------------------------------------------------ |
| Entities                    | `LoggingActivity`, `LoggingActivityInput`, `LoggingLogEntry`                                            |
| Enums                       | `LoggingActivityStatus`, `LoggingActivityType`, `LoggingChannelFullPolicy`                              |
| Public DTOs                 | `LoggingOptions`, `LoggingActivityBeginRequest`, `LoggingActivityUpdateRequest`                         |
| Marker interface            | `ILoggingDatabaseFactory` (extends `IDataConnectionFactory`)                                            |
| Service                     | `LoggingActivityService` (public sealed; dual ctor), `LoggingActivityScope` (IDisposable)               |
| OTel pipeline               | `RoadbedDbLogRecordExporter`, `LogWriterHostedService`, `LoggingChannel`                                |
| Wiring                      | `InstallLogging` (`IServiceCollectionInstaller`), `LoggingBuilderExtensions.AddRoadbedDbLogging`        |

## MUST

- **MUST** register a singleton `LoggingOptions` and a singleton `ILoggingDatabaseFactory` in DI **before** calling `services.InstallModulesInAppDomain(configuration)`. The installer resolves both up-front and throws if either is missing.
- **MUST** call `builder.Logging.AddRoadbedDbLogging()` on the host's `ILoggingBuilder` to wire the OpenTelemetry MEL provider, the batch processor, and the database exporter. The `IServiceCollectionInstaller` does **not** see `ILoggingBuilder`, so this step lives outside `InstallLogging`.
- **MUST** generate the activity ULID in the consuming application — Roadbed.Logging does **not** generate identifiers. Pass the same ULID you used for `IAsyncBulkInsertOperation.BulkInsertAsync` calls during the run.
- **MUST** set `LoggingOptions.Application` (and ideally `Environment`) so every row carries identifying provenance. The exporter stamps these onto every `LoggingLogEntry`.
- **MUST** set `LoggingOptions.Schema` to the MySQL database name (e.g. `"ops"`, `"platform"`) in production. The default is the empty string for SQLite-dev friendliness.
- **MUST** install the three table DDL scripts (`activity`, `activity_input`, `log_entries`) against the target schema **before** the host starts. Roadbed.Logging does not run schema migrations. The shipped MySQL scripts pre-create 10 years of monthly partitions (2026-01 .. 2035-12) on all three tables — no forward-rollover routine is needed until late 2035.
- **MUST** schedule a partition-drop routine on MySQL: drop partitions older than **12 months** for `activity` and `activity_input`, and older than **90 days** for `log_entries`. **Roadbed.Logging does not ship this routine** — the consuming app builds it (a stored proc invoked by a Roadbed.Scheduling job, or a MySQL EVENT). On SQLite, run the equivalent `DELETE FROM …` statements on the same cadence.
- **MUST** pass the `LoggingActivityScope` to `HeartbeatAsync` / `CompleteAsync` / `FailAsync` instead of the bare activity id whenever the scope is in scope. The scope carries the row's `created_on`, which the framework includes in the UPDATE's WHERE clause so MySQL prunes to the single monthly partition that owns the row. The id-only overloads are kept for legacy / external callers but probe every monthly partition (~120).
- **MUST** treat all stored timestamps as UTC. The DDL defaults `created_on` and `recorded_on` to `UTC_TIMESTAMP(6)` (MySQL) / `strftime('%Y-%m-%dT%H:%M:%fZ','now')` (SQLite); the framework's UPDATE path stamps `last_modified_on` with `DateTime.UtcNow` on every call. `last_modified_on`'s `ON UPDATE CURRENT_TIMESTAMP(6)` clause is a safety net only — the explicit set wins.
- **MUST** call `service.CompleteAsync(...)` or `service.FailAsync(...)` explicitly when a run ends. Disposing the `LoggingActivityScope` pops the ambient MEL scope and stops the diagnostic Activity, but it does **not** record a terminal status.
- **MUST** use structured log templates (`logger.LogInformation("Loaded {RowCount}", count)`) — the exporter splits the template (stored as `message_template`) from the named args (stored as `properties` JSON) for downstream aggregation. Interpolated `$"..."` strings produce a single rendered message with no template.

## MUST NOT

- **MUST NOT** reference `Roadbed.Crud` from a project that already takes `Roadbed.Logging`. The library is deliberately a peer, not a consumer, of the CRUD pattern.
- **MUST NOT** point `ILoggingDatabaseFactory` at any `DataConnectionStringType` other than `MySQL`, `SQLite`, or `SQLiteInMemory`. The installer throws on every other value.
- **MUST NOT** rely on `LoggingActivityScope` to auto-finalize the activity row. Skipped, Canceled, and Succeeded outcomes are all distinct terminal states; the dispose path has no way to choose between them.
- **MUST NOT** invent your own batching or retry layer around `LoggingActivityService` or the log-entry path — the background writer already batches, falls back to `Console.Error` on database error, and flushes on `StopAsync`.
- **MUST NOT** log from a category that overlaps `LoggingOptions.RecursionGuardCategories` and expect the entry to be persisted. Categories under `Roadbed.Logging`, `Roadbed.Data`, `Roadbed.Data.MySql`, `Roadbed.Data.Sqlite`, and `MySqlConnector` are dropped to prevent the database write path from logging through itself.
- **MUST NOT** pass `LoggingActivityStatus.Failed` to `CompleteAsync`. Use `FailAsync(activityId, exception)` instead — it records the exception message and type as well as the terminal status.
- **MUST NOT** read `IConfiguration` from inside Roadbed.Logging-aware code expecting the library to honor it. The library only sees `LoggingOptions` and `ILoggingDatabaseFactory` from DI.
- **MUST NOT** add a standalone `UNIQUE` on `activity.id` or any other partitioned-table single column. MySQL requires every unique key on a partitioned InnoDB table to contain the partition column, so the only PK is the composite one. Uniqueness of `activity.id` is guaranteed by the caller's ULID, not by a DB constraint.
- **MUST NOT** add foreign keys between the three tables (or from them to anything else). Partitioned InnoDB tables cannot have FKs. The lineage edges in `activity_input` are soft references on purpose.
- **MUST NOT** rely on the `last_modified_on` server-side `ON UPDATE` trigger to be UTC. The framework's UPDATE statements pass an explicit `@LastModifiedOn = DateTime.UtcNow` parameter that overrides the trigger. Custom queries that update the row outside the framework should do the same — or set the connection's session `time_zone` to `+00:00`.
- **MUST NOT** re-register `LoggingChannel` in DI. `InstallLogging` registers it as a process-wide shared instance built from `LoggingOptions`; the host writer (in the host container) and every producer-side OTel exporter (in any container — host, `ServiceLocator` snapshot, or test fixture) all need to resolve the **same** object. Overwriting that registration in `Program.cs` (e.g. via `services.AddSingleton<LoggingChannel>(new LoggingChannel(...))`) is what creates the "`activity` rows write but `log_entries` stays empty" symptom.

## Consuming-application host wiring

The canonical startup recipe — register `LoggingOptions` and `ILoggingDatabaseFactory` **before** anything else logging-related, call `AddRoadbedDbLogging()` on the logging builder, then run `InstallModulesInAppDomain`. The order shown below is the one the framework is tested against:

```csharp
// Program.cs (host)
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Roadbed;
using Roadbed.Logging;

var builder = Host.CreateApplicationBuilder(args);

// 1. The two POCO singletons the framework reads at install time.
builder.Services.AddSingleton(new LoggingOptions
{
    Schema      = "logging",                                     // MySQL DB name; empty for SQLite-dev
    Application = "Foo",
    Environment = builder.Environment.EnvironmentName,
    BatchSize   = 1000,
    FlushInterval = TimeSpan.FromSeconds(5),
});

builder.Services.AddSingleton<ILoggingDatabaseFactory, FooLoggingDatabaseFactory>();

// 2. Wire the OpenTelemetry MEL provider, batch processor, and DB exporter
//    onto the host's logging builder. Safe to call before InstallModules*
//    — the exporter resolves LoggingChannel lazily on first export, not at
//    OTel-provider realization, so installer-discovery order is not load-
//    bearing.
builder.Logging.AddRoadbedDbLogging();

// 3. Discover and run every IServiceCollectionInstaller, including
//    InstallExtensionsLogging (Roadbed.Common) and InstallLogging
//    (Roadbed.Logging). InstallLogging eagerly constructs the shared
//    LoggingChannel instance and registers it as a singleton.
builder.Services.InstallModulesInAppDomain(builder.Configuration);

using var host = builder.Build();
await host.RunAsync();
```

### Supported providers

Set `ILoggingDatabaseFactory.Connecion.ConnectionStringType` to one of:

| Type                                                        | Use                                                                                              |
| ----------------------------------------------------------- | ------------------------------------------------------------------------------------------------ |
| `DataConnectionStringType.MySQL`                            | Production. `log_entries` ships with monthly RANGE partitioning for fast retention drops.        |
| `DataConnectionStringType.SQLite`                           | Local/dev only. No partitioning; retention is a scheduled `DELETE` job.                          |
| `DataConnectionStringType.SQLiteInMemory`                   | Test harness only. Same as SQLite but the database vanishes when the connection closes.          |

`Postgres`, `Unknown`, and any other value cause `InstallLogging` to throw `InvalidOperationException` at install time.

### Database setup (MySQL example)

Apply the install script under `src/Roadbed.Logging/Assets/Tables/<table>/install_mysql.txt` (or paste the consolidated copies further down this reference) against your target database. The DDL creates tables **unqualified** — `CREATE TABLE activity (...)`, not `CREATE TABLE logging.activity (...)`. Select the target database before executing:

```sql
CREATE DATABASE IF NOT EXISTS logging
    DEFAULT CHARACTER SET utf8mb4
    DEFAULT COLLATE utf8mb4_unicode_ci;
USE logging;
-- then run install_mysql.txt for each of the three tables
```

In the host, set `LoggingOptions.Schema = "logging"` so every C# repository statement qualifies the tables as `logging.activity` etc.

The DB user the app runs as needs `INSERT`, `UPDATE`, and `SELECT` privileges on all three tables — `activity` rows writing successfully only proves the activity path's grants exist; `log_entries` can fail silently if the same user lacks INSERT there.

### Why the shared channel matters

Roadbed framework services use the dual-constructor pattern: their public constructor resolves dependencies via `ServiceLocator.GetService<T>()`, and `ServiceLocator` holds a point-in-time **snapshot** of the host's `IServiceCollection` — a separate `IServiceProvider` from the host's own container. When a `ServiceLocator`-resolved component logs, the log record flows through *that snapshot's* OTel logger provider, which builds *its own* `RoadbedDbLogRecordExporter`. The exporter resolves `LoggingChannel` from the snapshot's provider, not the host's.

`InstallLogging` registers `LoggingChannel` as `AddSingleton<LoggingChannel>(eagerInstance)` — a **concrete-instance descriptor** rather than a typed factory — so every `IServiceProvider` built from the underlying collection returns the same object. Producers in any container converge on one channel; the `LogWriterHostedService` running in the host container drains that one channel.

## Troubleshooting

**`activity` rows write but `log_entries` stays empty (no `Console.Error` fallback firing either).** The exporter is enqueueing into a `LoggingChannel` that the host writer does not drain. After the framework fix that ships `LoggingChannel` as a shared singleton, the usual causes are:

1. Something in `Program.cs` (or another installer) re-registered `LoggingChannel` after `InstallLogging` ran, replacing the shared instance.
2. The host code is using an older vendored copy of `Roadbed.Common.dll` or `Roadbed.Logging.dll` that still freezes a throwaway `ILoggerFactory` (the pre-fix behavior). Re-vendor both DLLs from the framework solution's `bin/Release/net10.0/` directory.
3. The DB user has `INSERT` on `logging.activity` but not on `logging.log_entries`. The `activity` write proves only that grant; check `SHOW GRANTS FOR <user>` for the full set.

**Startup crash: `No service for type 'Roadbed.Logging.LoggingChannel' has been registered.`** This was the symptom when `AddRoadbedDbLogging()` was called before `InstallModulesInAppDomain` AND `InstallExtensionsLogging` eagerly realized the OTel provider via a throwaway service provider. After the framework fix, the exporter resolves `LoggingChannel` lazily on first export — never at OTel-provider realization — so this crash should not occur even with the documented startup order. If you do see it, you are on a pre-fix vendored DLL.

**Log lines from `ServiceLocator`-resolved components are missing while host-resolved ones land fine.** This is the cause-2 symptom from the pre-fix design. Confirm both `Roadbed.Common.dll` and `Roadbed.Logging.dll` are at or after the fix version; the channel-sharing test in `Roadbed.Test.Unit.Logging.InstallLoggingTests` covers exactly this scenario.

## Code patterns

### Host wire-up (abbreviated)

```csharp
// See the Consuming-application host wiring section above for the full
// recipe with comments. The compact form:
var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddSingleton(new LoggingOptions
{
    Schema = "logging",
    Application = "Foo",
    Environment = builder.Environment.EnvironmentName,
});

builder.Services.AddSingleton<ILoggingDatabaseFactory, FooLoggingDatabaseFactory>();

builder.Logging.AddRoadbedDbLogging();   // OTel + batch processor + exporter

builder.Services.InstallModulesInAppDomain(builder.Configuration);  // InstallLogging runs here

using var host = builder.Build();
await host.RunAsync();
```

### Marker-interface factory implementation

```csharp
namespace Foo.App;

using System;
using System.Data;
using Microsoft.Data.Sqlite;
using Roadbed.Data;
using Roadbed.Logging;

internal sealed class FooLoggingDatabaseFactory : ILoggingDatabaseFactory
{
    public FooLoggingDatabaseFactory(IConnectionStringProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);

        this.Connecion = new DataConnecionString(
            DataConnectionStringType.MySQL,
            provider.Resolve("FooLogging"));
    }

    public DataConnecionString Connecion { get; }

    public IDbConnection CreateOpenConnection() { /* open + return */ throw new NotImplementedException(); }

    public Task<IDbConnection> CreateOpenConnectionAsync(CancellationToken cancellationToken)
    {
        /* open + return */ throw new NotImplementedException();
    }
}
```

### A Quartz job that opens an activity

```csharp
namespace Foo.App.Jobs;

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Quartz;
using Roadbed.Logging;
using Roadbed.Scheduling;

public sealed class FooIngestionJob : BaseSchedulingJob<FooIngestionJob>
{
    private readonly LoggingActivityService _activities;
    private readonly IFooLoader _loader;

    public FooIngestionJob(
        ILogger<FooIngestionJob> logger,
        LoggingActivityService activities,
        IFooLoader loader)
        : base(
            name: "FooIngestion",
            description: "Loads the Foo dataset every 15 minutes.",
            schedule: new SchedulingSchedule(TimeSpan.FromMinutes(15)),
            logger: logger)
    {
        ArgumentNullException.ThrowIfNull(activities);
        ArgumentNullException.ThrowIfNull(loader);

        this._activities = activities;
        this._loader = loader;
    }

    public override async Task ExecuteAsync(CancellationToken cancellationToken)
    {
        string activityId = Ulid.NewUlid().ToString();        // app owns the ULID dep

        using LoggingActivityScope scope = await this._activities.BeginAsync(
            new LoggingActivityBeginRequest
            {
                Id = activityId,
                ActivityType = LoggingActivityType.Ingestion.ToString().ToLowerInvariant(),
                Target = "ops.foo",
                ActivityKey = "Foo.Ingestion.FullRefresh",
                FireInstanceId = this.Context.FireInstanceId,
                QuartzJobName = this.Context.JobDetail.Key.Name,
                QuartzJobGroup = this.Context.JobDetail.Key.Group,
                QuartzTriggerName = this.Context.Trigger.Key.Name,
                QuartzTriggerGroup = this.Context.Trigger.Key.Group,
                SchedulerInstanceId = this.Context.Scheduler.SchedulerInstanceId,
            },
            cancellationToken);

        try
        {
            int rows = await this._loader.LoadAsync(activityId, cancellationToken);

            // Prefer the scope-aware overload — it includes scope.CreatedOn
            // in the UPDATE WHERE clause so MySQL prunes to one monthly
            // partition instead of probing all 120.
            await this._activities.CompleteAsync(
                scope,
                LoggingActivityStatus.Succeeded,
                recordsImpacted: rows,
                cancellationToken: cancellationToken);

            this.Context.Result = $"Loaded {rows:N0} foo rows";
        }
        catch (Exception ex)
        {
            await this._activities.FailAsync(scope, ex, CancellationToken.None);
            throw;
        }
    }
}
```

### How `log_entries.activity_id` gets populated

Any `ILogger` call emitted **while a `LoggingActivityScope` is alive on the
current async flow** is automatically stamped with that scope's
`activity_id` — you do **not** pass the id to the logger. `BeginAsync`
establishes the correlation in two ambient channels, and
`RoadbedDbLogRecordExporter` reads them per log record:

- the **MEL logging scope** state key `activity_id` (primary), and
- `Activity.Current`'s `roadbed.activity_id` tag (fallback).

The ambient state is pushed in the **caller's** execution context, so it
flows to the `await BeginAsync(...)` caller and to any code it awaits while
the `using` scope is open. Putting `{ActivityId}` in a message template is
optional and independent — that only lands the value in the row's
`properties` JSON, never in the dedicated `activity_id` column. Once the
scope is disposed, `Activity.Current` reverts and later log lines are no
longer stamped.

> Requires the Roadbed.Logging build where `BeginAsync` pushes the ambient
> in the caller's frame. In earlier builds `BeginAsync` was `async` and the
> scope/`Activity` were established inside its state machine, so .NET's
> `ExecutionContext` restore discarded them before control returned to the
> caller — leaving `activity_id` NULL even though `properties.ActivityId`
> was set. If you see that symptom, re-vendor `Roadbed.Logging.dll`.

### Heartbeating from a long-running step

```csharp
while (await reader.ReadBatchAsync(cancellationToken) is { } batch)
{
    await this._sink.WriteAsync(batch, cancellationToken);

    // Pass the scope — it carries created_on so the UPDATE prunes to
    // one MySQL partition. The string-id overload is kept for callers
    // that only have the activity id (e.g. a watchdog process); on
    // MySQL it probes every monthly partition.
    await this._activities.HeartbeatAsync(scope, cancellationToken);
}
```

### Patching current state mid-run

```csharp
await this._activities.UpdateAsync(
    new LoggingActivityUpdateRequest
    {
        ActivityId = scope.ActivityId,
        CreatedOn  = scope.CreatedOn,           // enables MySQL partition pruning
        Target = $"ops.{table}",                // discovered after Begin
        ParametersJson = JsonConvert.SerializeObject(currentParameters),
        RecordsImpacted = runningTotal,
    },
    cancellationToken);
```

### Recording lineage edges

```csharp
// "this silver-run consumed those two bronze loads"
await this._activities.AddInputAsync(silverActivityId, bronzePlacesActivityId, inputRole: "places", cancellationToken);
await this._activities.AddInputAsync(silverActivityId, bronzeCousubsActivityId, inputRole: "cousubs", cancellationToken);
```

## Common pitfalls

❌ Disposing the scope without calling `CompleteAsync`:
```csharp
using var scope = await activities.BeginAsync(request, ct);
await DoWorkAsync(ct);
// scope disposes → row stays in 'running' forever
```
✅ Always finalize explicitly:
```csharp
using var scope = await activities.BeginAsync(request, ct);
try { await DoWorkAsync(ct); await activities.CompleteAsync(scope.ActivityId, LoggingActivityStatus.Succeeded, cancellationToken: ct); }
catch (Exception ex) { await activities.FailAsync(scope.ActivityId, ex, CancellationToken.None); throw; }
```

❌ Logging from inside the database write path:
```csharp
// Repository inside Roadbed.Logging itself
this._logger.LogInformation("Inserted {N} entries", count);
// → category is "Roadbed.Logging.Repositories.LoggingLogEntryRepository"
// → recursion-guarded → silently dropped
```
✅ Either accept the drop (the guard exists for a reason) or pick a category outside the guard list for genuine operator-visible diagnostics.

❌ Treating logs as a substitute for the activity row:
```csharp
this._logger.LogInformation("Starting Foo run with batch={BatchId}", batchId);
// no activity row → no run record, no heartbeat, no terminal status
```
✅ Open an activity at run start; logs become the per-event narrative attached to it.

❌ Setting `Schema = "ops"` against SQLite without ATTACH:
```csharp
new LoggingOptions { Schema = "ops" }
// SQL becomes "INSERT INTO ops.activity ..." which SQLite rejects.
```
✅ Either leave `Schema` empty for SQLite or ATTACH the file under that alias.

❌ Passing `LoggingActivityStatus.Failed` to `CompleteAsync`:
```csharp
await activities.CompleteAsync(id, LoggingActivityStatus.Failed);  // throws ArgumentException
```
✅ Use `FailAsync(id, exception)` — it records the message and type as well.

## Quick reference

| Need                                                       | Use                                                                                                |
| ---------------------------------------------------------- | -------------------------------------------------------------------------------------------------- |
| Insert a run record                                        | `await service.BeginAsync(request, ct)`                                                            |
| Stamp last_heartbeat_on                                    | `await service.HeartbeatAsync(activityId, ct)`                                                     |
| Patch current-state columns (target, metrics, Quartz, ...) | `await service.UpdateAsync(updateRequest, ct)`                                                     |
| Finish successfully                                        | `await service.CompleteAsync(activityId, LoggingActivityStatus.Succeeded, recordsImpacted: n, ct)` |
| Finish on exception                                        | `await service.FailAsync(activityId, exception, ct)`                                               |
| Mark canceled / skipped                                    | `await service.CompleteAsync(activityId, LoggingActivityStatus.Canceled, ct)`                      |
| Record a lineage edge                                      | `await service.AddInputAsync(consumerId, inputId, inputRole, ct)`                                  |
| Wire MEL → OTel → DB                                       | `builder.Logging.AddRoadbedDbLogging()`                                                            |
| Override drop policy                                       | `new LoggingOptions { ChannelFullPolicy = LoggingChannelFullPolicy.BlockBriefly }`                  |

## DDL install scripts

The source-of-truth files live under
`src/Roadbed.Logging/Assets/Tables/<table>/install_<provider>.txt` — six
files, three tables × two providers. The scripts create tables
**unqualified**; run them after selecting the target database (`USE logging;`
on MySQL, or against the right `.db` file on SQLite). Set
`LoggingOptions.Schema` in the host to match the database name so C#
statements end up qualified consistently (`logging.activity`, etc.).

> **The MySQL scripts are long.** Each of the three tables ships with 120
> pre-created monthly partitions covering 2026-01 .. 2035-12, plus `p_min`
> and `pmax`. An AI assistant generating an install script for a consumer
> should use the **Read** tool against the asset files rather than rely on
> what is reproduced inline below.

### Schema summary (MySQL)

| Table            | PK                                         | Partition key                       | Retention | Pre-created |
| ---------------- | ------------------------------------------ | ----------------------------------- | --------- | ----------- |
| `activity`       | `(id, created_on)`                         | `RANGE (TO_DAYS(created_on))`       | 12 months | 120 months  |
| `activity_input` | `(activity_id, input_activity_id, created_on)` | `RANGE (TO_DAYS(created_on))`   | 12 months | 120 months  |
| `log_entries`    | `(id, event_time_utc)`                     | `RANGE (TO_DAYS(event_time_utc))`   | 90 days   | 120 months  |

Composite-PK rules (load-bearing):

- Every UNIQUE/PRIMARY key on a partitioned InnoDB table must contain the partition column.
- No standalone `UNIQUE (id)` on any of the three tables — the composite PK is the only uniqueness constraint. Uniqueness of `activity.id` is the caller's responsibility (ULIDs are globally unique by construction).
- Partitioned InnoDB tables cannot have foreign keys. The lineage references in `activity_input` are soft on purpose.
- Partition pruning only happens when the query filters on the partition column. Every composite index leads with the fleet filter (`application`, `activity_id`, etc.) and ends with the partition column (`created_on` / `event_time_utc`).

### UTC contract

- `created_on`, `recorded_on` — server-side `DEFAULT (UTC_TIMESTAMP(6))` (MySQL) / `strftime('%Y-%m-%dT%H:%M:%fZ','now')` (SQLite). Connection-time-zone-independent — important for the partition key.
- `last_modified_on` — declares `ON UPDATE CURRENT_TIMESTAMP(6)` as a safety net, but the framework's UPDATE path passes an explicit `@LastModifiedOn = DateTime.UtcNow` parameter that overrides it. The column stays UTC even when the session `time_zone` is not.

### SQLite (no partitioning)

SQLite has no partitioning support. Retention is a scheduled `DELETE` (`activity` / `activity_input` after 12 months; `log_entries` after 90 days). The DDL is short:

#### activity (SQLite)

```sql
CREATE TABLE activity (
     id                    TEXT     NOT NULL COLLATE BINARY
    ,parent_activity_id    TEXT     NULL     COLLATE BINARY
    ,root_activity_id      TEXT     NULL     COLLATE BINARY
    ,trace_id              TEXT     NULL     COLLATE BINARY
    ,span_id               TEXT     NULL     COLLATE BINARY
    ,activity_key          TEXT     NULL
    ,application           TEXT     NOT NULL
    ,environment           TEXT     NULL
    ,activity_type         TEXT     NOT NULL
    ,target                TEXT     NULL
    ,status                TEXT     NOT NULL DEFAULT 'pending'
                           CHECK (status IN ('pending','running','succeeded','failed','canceled','skipped'))
    ,started_on            DATETIME NULL
    ,completed_on          DATETIME NULL
    ,last_heartbeat_on     DATETIME NULL
    ,records_impacted      INTEGER  NULL
    ,parameters            TEXT     NULL
    ,metrics               TEXT     NULL
    ,error                 TEXT     NULL
    ,error_type            TEXT     NULL
    ,scheduler_instance_id TEXT     NULL
    ,fire_instance_id      TEXT     NULL
    ,quartz_job_name       TEXT     NULL
    ,quartz_job_group      TEXT     NULL
    ,quartz_trigger_name   TEXT     NULL
    ,quartz_trigger_group  TEXT     NULL
    ,host                  TEXT     NULL
    ,process_id            INTEGER  NULL
    ,created_by            INTEGER  NULL
    ,created_on            DATETIME NOT NULL DEFAULT (strftime('%Y-%m-%dT%H:%M:%fZ','now'))
    ,last_modified_on      DATETIME NOT NULL DEFAULT (strftime('%Y-%m-%dT%H:%M:%fZ','now'))
    ,PRIMARY KEY (id)
);

CREATE INDEX idx_activity_app_created        ON activity (application, created_on);
CREATE INDEX idx_activity_app_status_created ON activity (application, status, created_on);
CREATE INDEX idx_activity_key_created        ON activity (activity_key, created_on);
CREATE INDEX idx_activity_status_created     ON activity (status, created_on);
CREATE INDEX idx_activity_type_created       ON activity (activity_type, created_on);
CREATE INDEX idx_activity_parent             ON activity (parent_activity_id);
CREATE INDEX idx_activity_root               ON activity (root_activity_id);
CREATE INDEX idx_activity_trace              ON activity (trace_id);
CREATE INDEX idx_activity_fire               ON activity (fire_instance_id);
```

#### activity_input (SQLite)

```sql
CREATE TABLE activity_input (
     activity_id        TEXT     NOT NULL COLLATE BINARY
    ,input_activity_id  TEXT     NOT NULL COLLATE BINARY
    ,input_role         TEXT     NULL
    ,created_on         DATETIME NOT NULL DEFAULT (strftime('%Y-%m-%dT%H:%M:%fZ','now'))
    ,PRIMARY KEY (activity_id, input_activity_id)
);

CREATE INDEX idx_activity_input_reverse ON activity_input (input_activity_id);
```

#### log_entries (SQLite)

```sql
CREATE TABLE log_entries (
     id               INTEGER  NOT NULL PRIMARY KEY AUTOINCREMENT
    ,event_time_utc   DATETIME NOT NULL
    ,recorded_on      DATETIME NOT NULL DEFAULT (strftime('%Y-%m-%dT%H:%M:%fZ','now'))
    ,log_level        INTEGER  NOT NULL
    ,category         TEXT     NOT NULL
    ,event_id         INTEGER  NULL
    ,event_name       TEXT     NULL
    ,message          TEXT     NOT NULL
    ,message_template TEXT     NULL
    ,properties       TEXT     NULL
    ,exception        TEXT     NULL
    ,exception_type   TEXT     NULL
    ,activity_id      TEXT     NULL COLLATE BINARY
    ,trace_id         TEXT     NULL COLLATE BINARY
    ,span_id          TEXT     NULL COLLATE BINARY
    ,application      TEXT     NOT NULL
    ,environment      TEXT     NULL
    ,host             TEXT     NULL
    ,process_id       INTEGER  NULL
);

CREATE INDEX idx_log_activity       ON log_entries (activity_id, event_time_utc);
CREATE INDEX idx_log_trace          ON log_entries (trace_id);
CREATE INDEX idx_log_app_time       ON log_entries (application, event_time_utc);
CREATE INDEX idx_log_app_level_time ON log_entries (application, log_level, event_time_utc);
CREATE INDEX idx_log_level_time     ON log_entries (log_level, event_time_utc);
CREATE INDEX idx_log_time           ON log_entries (event_time_utc);
```

### Retention

- **MySQL**, all three tables — schedule a partition-drop routine to `ALTER TABLE … DROP PARTITION p_YYYYMM` for every partition whose entire date range falls outside the retention window:
  - `activity`, `activity_input` → 12 months.
  - `log_entries` → 90 days.
  No forward-rollover routine is needed until 2035 because the install scripts pre-create the full 10-year run. Roadbed.Logging does **not** ship the drop routine — build it as a stored proc invoked by a Roadbed.Scheduling job, or as a MySQL EVENT, in the consuming application.
- **SQLite**, all three tables — schedule equivalent DELETE statements:
  ```sql
  DELETE FROM log_entries     WHERE event_time_utc < datetime('now', '-90 days');
  DELETE FROM activity_input  WHERE created_on     < datetime('now', '-12 months');
  DELETE FROM activity        WHERE created_on     < datetime('now', '-12 months');
  ```
  Follow with `VACUUM` (or set `PRAGMA auto_vacuum = INCREMENTAL` plus periodic `incremental_vacuum`) to reclaim disk.

## MCP / analytical query advice

The activity tables are partitioned on `created_on`; `log_entries` on `event_time_utc`. Tools that query these tables — including the consuming-app "logging analyst" MCP — should:

- **Filter every activity-table query on `created_on`** to give MySQL a chance to prune. The composite indexes are ordered `(fleet_filter…, created_on)` so range filters compose naturally.
- **Filter every log-table query on `event_time_utc`** for the same reason. For "fetch all logs for activity X" pulls, pass the activity's own `started_on..completed_on` window so the query prunes by month.
- **Display `started_on` / `completed_on`** for activity timing — those are the app-supplied wall-clock timestamps. Compute durations from them. `created_on` is the row-insert timestamp and is used for partitioning / retention only.
