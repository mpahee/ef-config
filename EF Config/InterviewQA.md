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
