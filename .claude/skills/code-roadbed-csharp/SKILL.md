---
name: code-roadbed-csharp
description: Use this skill when writing or modifying C# code that uses the Roadbed.* libraries (Common, Crud, Data, Data.Dapper, Data.Sqlite, Data.Postgresql, Data.MySql, Net, IO, IO.Csv, Logging, Messaging, Scheduling, Secrets.KeePass). TRIGGER when the project references any Roadbed.*.dll, when the user asks to scaffold a CRUDL repository / service / SDK class library / scheduled job / CSV file handler / message envelope / HTTP client wrapper / KeePass-backed secret reader / structured-log+activity tracking using Roadbed, when the user asks about persistent / MySQL-backed / clustered Quartz scheduling, when the user asks about OpenTelemetry-backed MEL-to-database logging or activity / run-instance / lineage tracking, or when the user mentions any Roadbed.* type by name. SKIP for unrelated C# projects, ASP.NET Core code that doesn't reference Roadbed, or code using EF Core / non-Roadbed data access patterns.
---

# code-roadbed-csharp

This skill teaches you how to write C# code against the Roadbed framework — a set of opinionated .NET libraries for repositories, services, data access, scheduling, messaging, file I/O, HTTP, and module auto-discovery.

The reference files in `references/` are the authoritative per-library guides. Read the relevant reference before generating code that uses that library.

## When to load a reference file

| If the user asks about / wants to use…                                          | Load                                          |
| ------------------------------------------------------------------------------- | --------------------------------------------- |
| `BaseClassWithLogging`, `IServiceCollectionInstaller`, `ServiceLocator`, `CommonBusinessKey`, logging conventions, module auto-discovery | `references/reference-roadbed-common.md`      |
| Repositories or services (CRUD, CRUDL, CRUDA, CRUDAL, ListOnly), `IEntity<T>`, `BaseEntityRecord`, `BaseEntityClass` | `references/reference-roadbed-crud.md`        |
| `IDataConnectionFactory`, `DataConnecionString`, `DataExecutorRequest`, marker-factory pattern | `references/reference-roadbed-data.md`        |
| Dapper type handlers for SQLite (`DateTime`, `DateTimeOffset`), `[Column]` mapping via `DapperMapping.Configure()` | `references/reference-roadbed-data-dapper.md` |
| SQLite-specific factory and executor (`SqliteConnectionFactory`, `SqliteExecutor`, `KeepAlive` for in-memory tests) | `references/reference-roadbed-data-sqlite.md` |
| PostgreSQL-specific factory and executor (`PostgresqlConnectionFactory`, `PostgresqlExecutor`, transient SQLSTATE handling) | `references/reference-roadbed-data-postgresql.md` |
| MySQL-specific factory and executor (`MySqlConnectionFactory`, `MySqlExecutor`, `AutoEnlist` for `TransactionScope`) | `references/reference-roadbed-data-mysql.md`  |
| Resilient HTTP calls with retry/backoff, JSON deserialization (`INetHttpClient`, `NetHttpRequest`, `NetHttpResponse<T>`) | `references/reference-roadbed-net.md`         |
| `IoFile`, `IoFileInfo` — typed file abstractions or building a new typed-file library | `references/reference-roadbed-io.md`          |
| CSV reading/writing with `IoCsvFile<T>` and `ICsvEntityMapper<T>`               | `references/reference-roadbed-io-csv.md`      |
| OpenTelemetry-backed MEL-to-database logging, activity / run-instance / lineage tracking (`LoggingActivityService`, `LoggingActivityBeginRequest`, `LoggingActivityUpdateRequest`, `LoggingActivityScope`, `LoggingOptions`, `ILoggingDatabaseFactory`, the three persisted entities, `RoadbedDbLogRecordExporter`, `LogWriterHostedService`, `InstallLogging`, `AddRoadbedDbLogging`) | `references/reference-roadbed-logging.md` |
| Pub/sub message envelopes (`MessagingMessageRequest<T>`, `MessagingMessageResponse<T>`, `MessagingPublisher`) | `references/reference-roadbed-messaging.md`   |
| Scheduled / recurring jobs (`BaseSchedulingJob<T>`, `SchedulingSchedule`, `SchedulingJobOptions`, metrics, persistent / AdoJobStore / clustered Quartz storage via `SchedulingPersistenceOptions` + `ISchedulingDatabaseFactory`, `SchedulerName` isolation for sharing one schema across services) | `references/reference-roadbed-scheduling.md`  |
| KeePass2 (`.kdbx`) secret loading at startup (`KeePassReader`, `IKeePassOptions`, `KeePassSecret`, marker-interface multi-database pattern) | `references/reference-roadbed-secrets-keepass.md` |

If the user's request spans multiple libraries (e.g., "an SDK that calls a REST API and stores results in SQLite"), load every relevant reference.

## Project-wide conventions

These rules apply to **every** Roadbed library. Always follow them — the references will repeat them in their own context, but they are not optional.

### MUST

- **MUST** prefix every instance member access with `this.` (e.g., `this._repository`, `this.LogDebug(...)`, `this.Context`).
- **MUST** validate reference parameters with `ArgumentNullException.ThrowIfNull(param)`.
- **MUST** validate string parameters with `ArgumentException.ThrowIfNullOrWhiteSpace(param)`.
- **MUST** put `CancellationToken cancellationToken = default` as the **last** parameter on any async method.
- **MUST** use `Newtonsoft.Json` (`[JsonProperty(...)]`) for serialization, not `System.Text.Json` (`[JsonPropertyName]`). Roadbed's serialization stack assumes Newtonsoft.
- **MUST** use the level-checked logging methods inherited from `BaseClassWithLogging` (`this.LogDebug`, `this.LogInformation`, `this.LogWarning`, `this.LogError`, `this.LogCritical`) — never call `this.Logger.LogDebug` or `this._logger.LogDebug` directly.
- **MUST** structure files with `#region` blocks: `Private Fields`, `Public Constructors` (or `Protected Constructors`), `Public Properties`, `Public Methods`, `Private Methods`, etc. Match the style of files already in the project.
- **MUST** write XML doc comments (`/// <summary>`, `/// <param>`, `/// <returns>`, `/// <exception>`) on every public and protected member. The Roadbed projects build with `<GenerateDocumentationFile>true</GenerateDocumentationFile>` and `<TreatWarningsAsErrors>True</TreatWarningsAsErrors>` — missing XML docs fail the build.
- **MUST** register module services through `IServiceCollectionInstaller` implementations, not via `services.Add*` calls in `Program.cs`. The host calls `services.InstallModulesInAppDomain(configuration)` once and every module's installer runs automatically.
- **MUST** call `ServiceLocator.SetLocatorProvider(services.BuildServiceProvider())` at the end of every `IServiceCollectionInstaller.ConfigureServices` that registers services other modules might need to resolve.

### MUST NOT

- **MUST NOT** use `_field = value` without `this.` — even when the C# compiler accepts it.
- **MUST NOT** use the old `?? throw new ArgumentNullException(nameof(param))` pattern. Use the throw-helpers above.
- **MUST NOT** use `System.Text.Json` for any DTO that crosses a Roadbed boundary (HTTP response, message envelope, persisted JSON).
- **MUST NOT** use `$"..."` interpolation inside a log message — it allocates the string even when the level is disabled. Use structured templates: `this.LogDebug("Processing {Id}", id)`.
- **MUST NOT** register services manually in `Program.cs` for code that has its own installer.
- **MUST NOT** implement marker-style framework interfaces (`ISchedulingJob`, `IDataConnectionFactory`, etc.) directly — always inherit the matching `Base*` class so the framework's lifecycle wiring works.
- **MUST NOT** invent your own retry loops around `MakeHttpRequestAsync`, `*Executor.*Async`, or other Roadbed methods that already retry internally. Configure their retry parameters instead.
- **MUST NOT** wrap Roadbed methods that already catch and translate exceptions (e.g., `NetHttpClient` translates `JsonException` to `Failure()`) in additional `try/catch` blocks. Check the documented success flag instead.

### Naming conventions for the file you generate

- Marker interfaces: `IFooDatabaseFactory : IDataConnectionFactory`, `IFooRepository : IAsyncCrudlRepository<...>`. The marker exists for DI distinction; it is empty.
- Concrete implementations: `FooDatabaseFactory`, `FooRepository`, `FooService`, `FooJob`, `FooMapper`. No `Impl` suffix.
- Installers: `InstallFoo` (e.g., `InstallFooDatabase`, `InstallFooSdk`). One installer per assembly.
- Internal interfaces are `internal interface IFooRepository`. Concrete services are `public sealed class FooService`.
- Private fields: `_underscoreCamelCase`. Always accessed as `this._field`.

## When you do not yet have the relevant `references/*.md` loaded

If you are about to generate code that uses a Roadbed library and you have not read the matching reference file in this turn, **stop and read it first**. The references contain MUST/MUST NOT rules and patterns that you cannot safely guess — the framework relies on specific class hierarchies, dual-constructor patterns, and DI registration shapes that vary per library.

## Cross-cutting patterns to remember

- **Marker-interface DI pattern.** When a framework interface exists at a package level (e.g., `IDataConnectionFactory`, `IAsyncCrudlRepository<T, TId>`), the application creates a per-domain marker (`IFooDatabaseFactory`, `IFooRepository`) so DI can distinguish multiple instances. Inject the marker, never the framework interface directly. Roadbed.* libraries occasionally ship their own framework-side markers when they need a dedicated registration (e.g., `ISchedulingDatabaseFactory` so the Quartz schema can be a distinct connection from the application's other databases, or `ILoggingDatabaseFactory` so the activity/log_entries schema is separately addressable) — the host implements them the same way.
- **POCO-options pattern (host owns configuration sourcing).** Roadbed libraries never read `IConfiguration` directly to drive behavior. Instead, each library declares a POCO (`SchedulingJobOptions`, `SchedulingPersistenceOptions`, `LoggingOptions`, `IKeePassOptions`, …) which the host populates from whatever source it wants — appsettings.json, environment variables, secret store, a baked constant. The host registers the populated POCO as a singleton; the library's installer resolves it from DI. This keeps Roadbed.* unaware of the host's configuration shape and reload semantics.
- **Dual-constructor pattern on services.** Concrete `public sealed` service classes have two constructors: a `public` one that takes only `ILogger<T>` and resolves the repository via `ServiceLocator.GetService<T>()`, and an `internal` one that takes the repository directly for unit tests reaching across `InternalsVisibleTo`. The application layer only ever sees the `public` constructor.
- **Auto-discovery installers.** `InstallModulesInAppDomain(configuration)` walks all loaded assemblies (skipping `System.*` and `Microsoft.*`), finds every `IServiceCollectionInstaller`, instantiates it, and calls `ConfigureServices`. Adding a new library to the host requires no change to `Program.cs`.
- **Async retry is built in.** Roadbed.Net (`NetHttpClient`), Roadbed.Data.Sqlite/Postgresql/MySql executors, and Roadbed.Scheduling all retry transient failures internally. Do not layer your own retry logic on top — configure their retry knobs.
- **Logging is level-checked.** Every base class derived from `BaseClassWithLogging` checks `IsEnabled(level)` before formatting the message. This is why you must use `this.LogDebug(...)` instead of `this.Logger.LogDebug(...)`.
