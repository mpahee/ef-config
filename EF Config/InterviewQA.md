# Interview Q&A — EF Core, Dependency Injection, Postgres

Living document, updated as each step of `PLAN.md` is completed.

## Step 1 — Entity Framework Core

**Q: What's the difference between EF6 and EF Core?**
A: EF6 is the legacy .NET Framework ORM; EF Core is the rewritten, cross-platform version used on .NET Core/.NET 5+. EF Core has no `App.config`/`web.config` connection-string resolution by name — configuration is done in code (`DbContextOptionsBuilder`) or via `Microsoft.Extensions.Configuration`. EF Core also has a pluggable provider model (SQL Server, Postgres, SQLite, etc. are separate NuGet packages implementing the same abstractions).

**Q: How does a `DbContext` get its connection string/options without `App.config`?**
A: Either override `OnConfiguring(DbContextOptionsBuilder)` inside the context (used when the context is constructed directly, no DI), or pass `DbContextOptions<TContext>` into the constructor (used when registering with `AddDbContext` in a DI container — see Step 2).

**Q: Why read the connection string from `appsettings.json` instead of hardcoding it?**
A: Keeps environment-specific values (dev/staging/prod) out of source code, and is the standard .NET configuration mechanism (`Microsoft.Extensions.Configuration`), replacing EF6's `ConfigurationManager.ConnectionStrings` pattern.

**Q: What does `Database.EnsureCreated()` do, and how is it different from migrations?**
A: It creates the database and schema from the current model if they don't already exist — useful for quick prototypes/demos. It does **not** track schema history or generate incremental SQL scripts, so it cannot be combined with EF Core migrations (`dotnet ef migrations add` / `database update`) on the same database. Migrations are the production-appropriate approach because they're versioned and reversible.

**Q: When does a LINQ query against a `DbSet<T>` actually hit the database?**
A: EF Core builds an `IQueryable` expression tree lazily; nothing executes until the query is enumerated — e.g. by calling `.ToList()`, `.ToListAsync()`, iterating in a `foreach`, or similar. Until then you can keep composing `.Where()`, `.OrderBy()`, etc., and EF Core translates the final expression tree to one SQL query.

**Q: How does EF Core decide table names, primary keys, and column types without being told?**
A: By convention: the `DbSet<Person> Users` property name becomes the table name (`Users`); a property named `Id` or `<ClassName>Id` (here, `PersonId`) becomes the primary key; `string` properties become nullable `nvarchar(max)` columns unless told otherwise.

**Q: What are Data Annotations and when would you use them over relying on convention or Fluent API?**
A: Data Annotations (`[Table]`, `[Key]`, `[Required]`, `[MaxLength]`, etc., from `System.ComponentModel.DataAnnotations`) let you state mapping explicitly on the entity class itself — useful for simple, per-property mapping that doesn't fit the conventions (e.g. table name differs from the DbSet name, or you want a constrained column length/NOT NULL). Fluent API (overriding `OnModelCreating`) is used instead when mapping logic is too complex for attributes — composite keys, relationships spanning multiple entities, value conversions — or when you don't want mapping concerns inside the entity class at all.

**Q: Same attributes existed in EF6 — what carried over?**
A: Data Annotations mapping attributes are essentially unchanged between EF6 and EF Core; it's one of the few mapping mechanisms that didn't change shape across the rewrite.

---

## Step 2 — Dependency Injection

**Q: What is `IServiceCollection` and what does `BuildServiceProvider()` do?**
A: `IServiceCollection` is the DI container's registry — you call methods like `AddScoped`/`AddSingleton`/`AddTransient`/`AddDbContext` to describe what types exist and how to construct them. `BuildServiceProvider()` compiles that registry into a `ServiceProvider`, the actual container you can ask to resolve instances from.

**Q: What does `AddDbContext<DataContext>(options => options.UseSqlServer(...))` do, and what lifetime does it register with?**
A: It registers `DataContext` with the container as a **Scoped** service by default, and configures how `DbContextOptions<DataContext>` gets built (here, pointing at SQL Server with a connection string). This replaces the `OnConfiguring` override approach from Step 1 — instead of the context configuring itself, the container builds and hands it pre-configured options.

**Q: Why Scoped and not Singleton or Transient for a `DbContext`?**
A: A `DbContext` is meant to represent one unit of work (it tracks entity state, has one underlying DB connection). Singleton would share one context — and one DB connection/change tracker — across the whole app, which isn't thread-safe and leaks stale tracked entities. Transient would create a new context per resolution, which is wasteful and breaks the "one unit of work per scope" idea. Scoped means one instance per logical operation (per HTTP request in a web app; here, per manually-created `CreateScope()`).

**Q: What is `CreateScope()` doing in a console app, where there's no HTTP request?**
A: It manually creates the boundary that a Scoped lifetime needs. In ASP.NET Core, the framework creates one scope per incoming request automatically. A console app has no such built-in boundary, so `using var scope = serviceProvider.CreateScope();` creates one explicitly — everything resolved from `scope.ServiceProvider` within that `using` block shares the same Scoped instances (e.g. the same `DataContext`).

**Q: Walk through what happens when `GetRequiredService<IPersonRepository>()` is called.**
A: The container looks up the registration (`AddScoped<IPersonRepository, PersonRepository>()`), sees it needs to build a `PersonRepository`, and reflects over its constructor: `public PersonRepository(DataContext context)`. It doesn't have a `DataContext` yet, so it recursively resolves that first — via the `AddDbContext` registration, reusing the same Scoped instance if one was already created in this scope. Once it has a `DataContext`, it calls `new PersonRepository(context)` itself. The caller never writes `new PersonRepository(...)` anywhere — the container wires up the whole dependency graph.

**Q: What does constructor injection actually buy you here?**
A: `PersonRepository` declares what it needs (`DataContext`) without knowing or caring where that instance comes from — a real SQL Server-backed context, a Postgres-backed context (Step 3), or a fake/in-memory one in a unit test. `Program.cs` similarly depends on `IPersonRepository`, the abstraction, not `PersonRepository`, the concrete class — so it could be swapped for a test double without changing any calling code.

**Q: Why register `PersonRepository` as Scoped too, instead of Transient?**
A: It should live and die with the `DataContext` it wraps — both represent the same unit of work. If `PersonRepository` were registered Transient while `DataContext` is Scoped, you could get away with it here (no observable difference for one repository), but it's a "captive dependency" risk pattern to be aware of in general: a longer-lived service holding a reference to a shorter-lived one can cause a service to be used after its underlying scope is gone, or to silently capture an instance that should have been replaced.

## Step 3 — Postgres

**Q: What changed to move from SQL Server to Postgres?**
A: One NuGet package (`Microsoft.EntityFrameworkCore.SqlServer` → `Npgsql.EntityFrameworkCore.PostgreSQL`), one method call in the `AddDbContext` registration (`UseSqlServer(...)` → `UseNpgsql(...)`), and the connection string format. `DataContext`, `Person`, `IPersonRepository`/`PersonRepository`, and every DI registration stayed exactly the same.

**Q: Why does that minimal a change work?**
A: EF Core's provider model puts all database-specific behaviour (SQL dialect, type mapping, connection handling) behind the provider package. The rest of EF Core — `DbContext`, `DbSet<T>`, LINQ-to-SQL translation, change tracking — is provider-agnostic. `UseNpgsql`/`UseSqlServer` are just the entry point that plugs a different provider implementation into the same `DbContextOptionsBuilder`.

**Q: What's `Npgsql` itself, as opposed to the EF Core provider package?**
A: Npgsql is the low-level ADO.NET driver for Postgres — it owns the actual TCP connection and Postgres wire protocol, similar to how `Microsoft.Data.SqlClient` does for SQL Server. `Npgsql.EntityFrameworkCore.PostgreSQL` builds on top of it to add the EF Core-specific pieces (LINQ translation, migrations support, type mapping).

**Q: What's a connection-string format difference between SQL Server and Postgres worth knowing?**
A: SQL Server commonly uses `Data Source=...;Initial Catalog=...`; Postgres/Npgsql uses `Host=...;Port=...;Database=...;Username=...;Password=...`. The shape is provider-specific — there's no universal connection string format, which is part of why the provider package is what knows how to parse it.

**Q: Describe a real debugging scenario from building this — connecting to "the same" Postgres on two different setups.**
A: A Docker container running Postgres and a natively-installed Postgres service can both be configured to listen on port 5432, and both appear reachable at `localhost:5432` — but they're different servers in different network namespaces. A Windows process (like `dotnet run`) connecting to `localhost:5432` reaches whichever server is actually bound to that port in the *Windows* network stack; `docker exec` commands run *inside* the container, so they only ever see that container's own Postgres. If both happen to be running, you can get `CanConnect() == true` and no exceptions, while inspecting "the database" via `docker exec` shows it's empty — because you were never looking at the same server. Lesson: confirm which process owns a port (e.g. `Get-NetTCPConnection`/`Get-CimInstance Win32_Process`) before assuming a Docker container is what an app is actually talking to.

**Q: `EnsureCreated()` returned `false` and didn't create the table even though the database had no tables. Why?**
A: `EnsureCreated()`'s check is "does the database itself exist?" — not "does it have my schema?". If the database already exists (even completely empty, e.g. auto-created by Docker's `POSTGRES_DB` environment variable), EF Core assumes the schema is already in place and does nothing. It only creates the database *and* schema together, as one step, when the database doesn't exist yet. Dropping the empty database and re-running let `EnsureCreated()` create both correctly.

## Writing data — Add, AddRange, SaveChanges

**Q: What does `_context.Users.Add(person)` actually do, and when does the INSERT happen?**
A: `Add()` only changes the entity's state in EF Core's change tracker to `Added` — no SQL runs yet. `SaveChanges()` is what inspects the change tracker, generates the SQL (an `INSERT` for an `Added` entity), and executes it inside an implicit transaction. This is the same "build it up, then execute" pattern as deferred LINQ execution: tracking/composing is separate from the moment something actually talks to the database.

**Q: Why is looping `Add()` + `SaveChanges()` 100 times worse than one `AddRange()` + one `SaveChanges()`?**
A: Each `SaveChanges()` call is a round trip to the database (and its own transaction by default). Looping it 100 times means 100 round trips and 100 transactions for what's logically one operation — slow, and not atomic (a failure partway through leaves some rows inserted and others not). `AddRange()` marks every entity as `Added` in one call; a single subsequent `SaveChanges()` batches all 100 inserts into one round trip and one transaction — succeed or fail together.

**Q: How did you make the 100-row seed idempotent, so re-running the app doesn't keep adding more rows?**
A: Added `Any()` to the repository (`_context.Users.Any()`), and guarded the seed call with `if (!personRepository.Any())`. `Any()` translates to a SQL `EXISTS` query — it stops as soon as it finds one row, rather than pulling back every row just to check `Count > 0`. This is a cheap way to make a "seed data" step safe to run repeatedly, which matters since this demo uses `EnsureCreated()` instead of migrations (no built-in seed-data tracking like `HasData` + migrations would give you).

## Step 4 — Splitting classes out of `Program.cs` for testability

**Q: Why split `Person`, `DataContext`, and `PersonRepository` into their own files if the app behaves identically either way?**
A: Top-level-statement files (`Program.cs` using the implicit `Main` style) implicitly make any type declared in them `internal`/file-local in practice for cross-project visibility purposes — a separate test project referencing the main project can't easily see classes nested inside another project's `Program.cs`. Splitting them into their own files and marking them `public` is what makes `PersonRepository`, `IPersonRepository`, `DataContext`, and `Person` referenceable from `EF Config.Tests` later. No behavior changes — purely about making the next step (a test project) possible.

**Q: What's left in `Program.cs` after the split, and why keep anything there at all?**
A: Only composition/wiring: building `IConfiguration` from `appsettings.json`, registering services (`AddDbContext`, `AddScoped<IPersonRepository>`), creating a `ServiceProvider`/scope, seeding, and the final read-and-print. This is the "composition root" pattern — one place in the app responsible for wiring concrete implementations to abstractions; everything else in the app should only ever depend on the abstractions (`IPersonRepository`), never construct concrete types itself.

**Q: What broke when the split happened, and why?**
A: `Program.cs` still calls `options.UseNpgsql(connectionString)` inside the `AddDbContext` registration — `UseNpgsql` is an extension method from `Microsoft.EntityFrameworkCore` (well, from the Npgsql provider package, extending a type in `Microsoft.EntityFrameworkCore`) on `DbContextOptionsBuilder`. Before the split, `Program.cs` got that `using Microsoft.EntityFrameworkCore;` "for free" because `DataContext`'s own definition (which needs `DbContext`/`DbContextOptions`) lived in the same file and had the import. After moving `DataContext` to its own file, `Program.cs` needed its own explicit `using Microsoft.EntityFrameworkCore;` — a good example of why splitting files can surface implicit dependencies that were previously hidden by file-level proximity.

## Step 5 — Unit tests with the EF Core InMemory provider

**Q: Why InMemory provider instead of mocking `DataContext` itself?**
A: `DbContext`/`DbSet<T>` are notoriously hard to mock well (lots of surface area: change tracking, LINQ provider, `SaveChanges` behavior) — most attempts end up re-implementing half of EF Core badly. The InMemory provider sidesteps that: it's a real (if non-relational) EF Core provider, so `DbSet<Person>`, `Add`, `AddRange`, `SaveChanges`, and LINQ all behave through the same EF Core machinery as Postgres would, just without real SQL. You get a real `DataContext` instance, not a hand-built fake.

**Q: Why does each test call `UseInMemoryDatabase(Guid.NewGuid().ToString())` instead of reusing one database name?**
A: The InMemory provider keys a "database" by the name string passed in — reusing the same name across tests means they'd all share state (one test's seeded rows would leak into another's `Any()`/`GetAll()` assertions), and test outcomes would depend on run order. A fresh GUID per test gives full isolation, the same property a real integration test would get from a fresh container/schema per run.

**Q: Didn't you say `IPersonRepository` exists so you can swap in a fake during tests — why does `PersonRepositoryTests` use the real `PersonRepository`, not a fake?**
A: Those are two different layers being tested. `IPersonRepository` is the seam *above* the repository — useful when something that *depends on* `IPersonRepository` (e.g. a future business-logic/service layer) needs a fake collaborator so its own logic can be tested in isolation, without a database at all. `PersonRepositoryTests` is testing the repository itself — there's nothing to fake here, the InMemory provider stands in for the database layer underneath it instead.

**Q: Why pin `FluentAssertions` to `7.0.0` instead of using the latest version?**
A: FluentAssertions changed its license starting with v8 — v8+ requires a paid commercial license for for-profit use, while the v7.x line (and everything before it) remains free under the Apache 2.0 license. For a demo/interview project there's no commercial entanglement either way, but pinning to 7.x avoids introducing a licensing question at all, and is worth knowing as a "watch your dependency licenses" talking point — `dotnet add package` defaults to latest, which silently would have pulled in v8.

**Q: What would change to add an integration-level test against real Postgres later (Step 6)?**
A: Swap `UseInMemoryDatabase(...)` for `UseNpgsql(...)` pointed at a real connection string (ideally a Testcontainers-managed disposable Postgres container, not a shared dev database), and call `context.Database.EnsureCreated()` once per test run to materialize the schema. The test bodies calling into `PersonRepository` wouldn't need to change at all — that's the same provider-abstraction benefit from Step 3 showing up again, just now in tests instead of `Program.cs`.

## Step 6 — Integration tests against real Postgres via Testcontainers

**Q: What is Testcontainers, and what problem does it solve here?**
A: A library that programmatically starts and stops real Docker containers from inside test code, scoped to a test run's lifetime. Instead of requiring a shared, manually-maintained "test database" that tests have to coordinate around (and that can drift or get polluted), each test class gets its own disposable, throwaway Postgres instance, started fresh and destroyed afterward. It turns "you need a real database for this test" from an infrastructure/coordination problem into a few lines of test setup code.

**Q: Why implement `IAsyncLifetime` instead of starting the container in a constructor?**
A: Starting a container is an async, potentially slow operation (pulling the image, waiting for Postgres to accept connections) — xUnit constructors can't be `async`. `IAsyncLifetime.InitializeAsync()` is xUnit's hook for async setup before each test class's tests run, and `DisposeAsync()` is the matching async teardown. Doing this in a constructor would force blocking on async code (`.Result`/`.Wait()`), which risks deadlocks and is exactly the anti-pattern async/await exists to avoid.

**Q: Did you need to worry about port conflicts with the Postgres instance already installed locally?**
A: No — Testcontainers binds each container to a random free host port rather than the image's default port (5432), then exposes the actual resolved port through `GetConnectionString()`. Confirmed in practice: a local Postgres install can be sitting on 5432 the whole time, and the Testcontainers-managed container runs alongside it without collision, because the test never hardcodes 5432 — it always asks Testcontainers for the live connection string.

**Q: Why does this project use `EnsureCreated()` here too, instead of real migrations, given migrations are "the production-appropriate approach" per Step 1?**
A: Consistency with how `Program.cs` itself initializes the schema (also `EnsureCreated()`, no migrations exist in this demo) — the integration test is meant to validate the same code path the app actually uses, not introduce a different schema-creation mechanism that the app doesn't have. If migrations were added to the main project, the integration test would switch to `context.Database.Migrate()` to stay representative of production behavior.

**Q: Why a separate `[Trait("Category", "Integration")]` and a separate `EF Config.IntegrationTests` project, instead of just adding these tests to `EF Config.Tests`?**
A: Two different concerns: speed and environment dependency. Unit tests (InMemory provider) run in milliseconds with zero external dependencies — they're meant to run on every save/build. Integration tests need Docker, pull an image on first run, and take seconds per test (container startup dominates). Separating projects/traits lets CI (or a developer locally) choose to run only the fast tests during normal iteration, and gate the slower, Docker-dependent ones to PR/merge checks — `dotnet test --filter Category!=Integration` for the fast loop, full `dotnet test "EF Config.slnx"` for the complete signal.

**Q: What does this integration test actually catch that the InMemory-provider unit tests in Step 5 couldn't?**
A: Real Npgsql SQL generation and type mapping — e.g. it proves `UseNpgsql` + the `Person`/`DataContext` mapping genuinely produces valid Postgres DDL/DML, not just "valid EF Core LINQ that some provider can satisfy." The InMemory provider doesn't validate SQL translation at all; it's a different (non-relational) provider implementing the same abstractions, so it can hide bugs that only manifest against a real relational backend (e.g. the `DateTime`/`timestamp with time zone` UTC-handling gotcha mentioned under Step 3 — InMemory wouldn't flag that, real Postgres would).

## Step 7 — CI pipeline (GitHub Actions)

**Q: Why doesn't `ci.yml` define a `postgres:16` service container, if integration tests need real Postgres?**
A: Because `EF Config.IntegrationTests` already manages its own Postgres via Testcontainers (Step 6) — it talks to the Docker daemon directly to start a disposable container per test class and reads the connection string back from Testcontainers itself, never from `IConfiguration`/an env var. A GitHub Actions service container is a different mechanism (the runner pre-starts a container alongside the job and exposes it on a fixed port) that nothing in this codebase is wired to consume. Adding one would just be an extra, unused Postgres instance running in CI — `ubuntu-latest` runners already ship Docker, which is the only prerequisite Testcontainers actually needs.

**Q: When *would* you reach for a GitHub Actions service container instead of (or alongside) Testcontainers?**
A: When the thing under test is the application itself reading a real `ConnectionStrings__AppConnectionString` from configuration — e.g. a smoke test that runs `Program.cs`'s actual startup path end-to-end in CI. Testcontainers is ideal *inside* test code that wants full lifecycle control (start fresh, assert, tear down, possibly multiple containers per run). A service container is simpler when you just need "a database listening on a known port for the whole job" and don't need test code to manage its lifecycle.

**Q: Why split the unit and integration test runs into two separate `dotnet test` steps instead of one `dotnet test "EF Config.slnx"` call?**
A: Two practical reasons. First, a failed step is individually visible in the GitHub Actions log/UI — "Unit tests" vs "Integration tests" failing tells you immediately which category broke, rather than parsing one combined test-runner summary. Second, it leaves room to later run them on different triggers cheaply (e.g. unit tests on every push, integration tests only on `pull_request`, to save CI minutes) by editing just one step instead of restructuring a single combined command.

**Q: This demo has no secrets in CI for the database — why mention secrets handling at all?**
A: Because that won't stay true once anything beyond Testcontainers-managed throwaway databases is involved — e.g. a CD job that needs to apply migrations against a real staging database, or an integration test against a long-lived shared instance instead of a disposable container. The pattern to reach for then is the same one already used locally: `IConfiguration`'s env-var override (`ConnectionStrings__AppConnectionString`), sourced from GitHub Actions Secrets in CI instead of `appsettings.Local.json` locally — same configuration system, different source, never committed to the repo either way.
