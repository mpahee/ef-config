# Plan: EF Core + DI + Postgres, step by step (interview prep)

## Context
Repo is currently a single `Program.cs` using legacy EF6 with SqlServer/LocalDB, configured via `App.config` connection strings. Goal: rebuild this incrementally into a small, interview-explainable example covering three concepts in order: (1) Entity Framework Core, (2) Dependency Injection, (3) Postgres as the actual database. Domain model stays as `Person`/`Users` (PersonId, Name, Address) since it's already familiar. We go step by step — each step should be small enough to explain clearly in an interview.

## Step 1 — Entity Framework Core, no DI yet, still SQL Server (establish EF Core basics)
- Remove the `EntityFramework` (EF6) package reference from `EF Config.csproj`.
- Add `Microsoft.EntityFrameworkCore.SqlServer`.
- Rewrite `DataContext` to inherit `Microsoft.EntityFrameworkCore.DbContext`, override `OnConfiguring(DbContextOptionsBuilder)` to call `UseSqlServer(connectionString)` directly (no `App.config` resolution — EF Core doesn't use that mechanism).
- Keep `Person` POCO and `DbSet<Person> Users` as-is.
- Talking point for interview: EF Core vs EF6 differences — `DbContextOptions`, no `App.config` magic-string resolution, code-first config.
- Verify: `dotnet build`, run app against existing LocalDB instance, confirm it reads/creates `Users` table (may need `Database.EnsureCreated()`).

## Step 2 — Dependency Injection (still SQL Server)
- Add `Microsoft.Extensions.DependencyInjection`.
- Build a small `IServiceCollection` in `Program.cs`: register `DataContext` via `AddDbContext<DataContext>(options => options.UseSqlServer(...))`.
- Introduce a thin repository/service abstraction, e.g. `IPersonRepository` with `GetAllAsync()`, backed by `PersonRepository` that takes `DataContext` via constructor injection — this is the concrete DI talking point (constructor injection, service lifetimes: scoped DbContext).
- `Program.cs` resolves `IPersonRepository` from the built `ServiceProvider` and calls it, instead of instantiating `DataContext` directly.
- Talking point: why DbContext is registered as Scoped, what constructor injection buys you (testability — could swap `IPersonRepository` for a fake).
- Verify: `dotnet build`, run app, same data printed as Step 1 but now resolved through DI container.

## Step 3 — Postgres (swap provider, keep DI + EF Core shape)
- Replace `Microsoft.EntityFrameworkCore.SqlServer` with `Npgsql.EntityFrameworkCore.PostgreSQL`.
- Change `UseSqlServer(...)` to `UseNpgsql(...)` in the `AddDbContext` registration.
- Update connection string to Postgres format (host/port/database/username/password), pointing at a local Postgres instance (Docker or local install) — confirm with user where Postgres will run before finalizing the literal string.
- Talking point: provider swap is mostly confined to the connection string + `UseNpgsql` — that's the abstraction EF Core's provider model buys you.
- Verify: requires a real Postgres instance reachable; run `dotnet build`, run app, confirm it connects and reads/writes `Person` rows in Postgres.

## Verification (end-to-end, after all 3 steps)
- `dotnet build "EF Config.slnx"` succeeds at each step.
- `dotnet run --project "EF Config/EF Config.csproj"` connects to the live database for that step and prints `Person` rows.
- Should be able to narrate, for each step, what changed and why (EF Core config model → DI/constructor injection/lifetimes → provider swap to Postgres).

**Status: Steps 1-3 complete.** `Program.cs` has EF Core 9 (`DataContext : DbContext`), DI (`IServiceCollection`/`AddDbContext`/`AddScoped<IPersonRepository>`), and Npgsql (`UseNpgsql`, `Npgsql.EntityFrameworkCore.PostgreSQL` 9.0.0 in `EF Config.csproj`) all in place. `dotnet build "EF Config.slnx"` succeeds (2 pre-existing nullable-reference warnings on `Person.Name`/`Address`, unrelated to EF/DI/Postgres work). No test project or CI workflow exists yet — that's Steps 4-7 below.

## Step 4 — Make code testable (split out of `Program.cs`) — DONE
- Moved `Person`, `DataContext`, `IPersonRepository`+`PersonRepository` out of `Program.cs` into their own files (`Person.cs`, `DataContext.cs`, `PersonRepository.cs`), all made `public` (were implicitly file-private top-level-statement classes before) so a future test project can reference them.
- `Program.cs` now only does composition/wiring: building `IConfiguration`, registering services, creating a scope, seeding, and the read-and-print at the end.
- Fixed one easy-to-miss issue from the split: `Program.cs` still calls `options.UseNpgsql(...)` (an `Microsoft.EntityFrameworkCore` extension method on `DbContextOptionsBuilder`), so it needs its own `using Microsoft.EntityFrameworkCore;` — that using was previously only there because `DataContext`'s definition lived in the same file.
- No new project needed for this step — it just makes the classes referenceable from a test project added in Step 5.
- Verified: `dotnet build "EF Config.slnx"` succeeds (same 2 pre-existing nullable-reference warnings on `Person.Name`/`Address`, 0 errors).

## Step 5 — Add a unit test project (EF Core InMemory provider) — DONE
- `dotnet new xunit -o "EF Config.Tests"`, added to `EF Config.slnx`.
- Packages: `xunit` 2.9.3 + `xunit.runner.visualstudio` + `coverlet.collector` (template defaults), `Microsoft.EntityFrameworkCore.InMemory` 9.0.0, `FluentAssertions` **7.0.0** (pinned — v8+ requires a commercial license, v7.x is the last Apache-licensed free release), plus a project reference to `EF Config/EF Config.csproj`.
- No `Moq` needed yet — `PersonRepositoryTests` exercises the real `PersonRepository` against a real (InMemory-provider) `DataContext` rather than mocking the context; mocking would only make sense if testing something that depends on `IPersonRepository` as a collaborator, which doesn't exist yet (Program.cs has no business-logic layer above the repository).
- Each test builds its own `DataContext` via `UseInMemoryDatabase(Guid.NewGuid().ToString())` — a uniquely-named in-memory DB per test, so tests don't share state or depend on run order.
- Implemented test cases in `PersonRepositoryTests.cs`: `Any_EmptyDatabase_ReturnsFalse`, `Any_WithSeededRow_ReturnsTrue`, `GetAll_ReturnsAllSeededPeople`, `AddRange_InsertsAllRecordsInOneSaveChanges`, `Add_SingleRecord_PersistsImmediately`.
- Naming convention used: `MethodName_Scenario_ExpectedResult`.
- Verified: `dotnet test "EF Config.slnx"` — 5/5 passed, no real database running.

## Step 6 — Integration tests against real Postgres — DONE
- Created a separate `EF Config.IntegrationTests` project (xUnit), added to `EF Config.slnx`, project reference to `EF Config.csproj`.
- Packages: `Testcontainers.PostgreSql` 4.12.0, `FluentAssertions` 7.0.0 (same licensing pin as Step 5).
- `PersonRepositoryIntegrationTests` implements `IAsyncLifetime`: `InitializeAsync` starts a `postgres:16` container per test class via `PostgreSqlBuilder("postgres:16")` (the non-obsolete constructor form — the parameterless `PostgreSqlBuilder()` + `.WithImage(...)` pattern is deprecated), `DisposeAsync` tears it down. Each test builds a `DataContext` with `UseNpgsql(_postgres.GetConnectionString())` and calls `context.Database.EnsureCreated()` to materialize schema (no migrations in this demo).
- Tagged the whole class `[Trait("Category", "Integration")]` so CI/local runs can separate fast unit tests from slower, Docker-dependent integration tests via `dotnet test --filter Category=Integration` (or `--filter Category!=Integration` to exclude them).
- Confirmed: Testcontainers assigns a random free host port per container — no conflict with a locally-installed Postgres bound to 5432, since they ran side by side without any manual port configuration.
- Verified: `dotnet test "EF Config.slnx"` → 5 unit tests (EF Config.Tests, InMemory) + 2 integration tests (EF Config.IntegrationTests, real Postgres via Docker) — 7/7 passed, Docker confirmed available beforehand via `docker ps`.

## Step 7 — CI pipeline (GitHub Actions, `.github/workflows/ci.yml`) — DONE
- Trigger on `push` and `pull_request`.
- **No Postgres service container** — that approach was superseded once Step 6 introduced Testcontainers: `EF Config.IntegrationTests` already starts its own disposable `postgres:16` container directly via the Docker daemon, and GitHub Actions `ubuntu-latest` runners have Docker preinstalled. Adding a service container too would be a redundant, unused second Postgres instance.
- Steps: `actions/checkout@v4` → `actions/setup-dotnet@v4` (net10.0) → `dotnet restore "EF Config.slnx"` → `dotnet build --no-restore -c Release` → unit tests (`EF Config.Tests`, fast, no external deps) → integration tests (`EF Config.IntegrationTests`, Testcontainers-managed Postgres), both with `--collect:"XPlat Code Coverage"`.
- Split the unit and integration `dotnet test` calls into separate steps (rather than one `dotnet test "EF Config.slnx"`) so a CI log clearly shows which category failed, and so a future "skip slow tests on every push" tweak (e.g. only run integration tests on `pull_request`) is a one-line change to `on:` or to that one step.
- No connection-string secret needed in CI at all — Testcontainers generates its own per-run connection string, so there's nothing to inject via `ConnectionStrings__AppConnectionString` for the integration job specifically. That env-var-override pattern still applies if/when `Program.cs` itself needs to run against a real fixed instance in CI/CD (e.g. Step 8 deployment smoke test).
- Verify next: push this branch and confirm the workflow goes green on GitHub; intentionally break a test locally first to confirm a red run is possible.

## Step 8 — CD (optional, since this is a console demo, not a deployed service)
- Gate on Step 7's CI job passing.
- `dotnet publish` the project; if presenting a containerized story, build and push a Docker image to GHCR on tag push.
- Frame this as "the same pattern as a real service, scaled down" — not a hard requirement for the interview unless asked.

## Interview talking points (Steps 4-8)
- InMemory provider for fast unit tests vs Testcontainers for integration tests: speed vs fidelity — InMemory doesn't validate real SQL translation, Testcontainers does.
- Why Postgres runs as a CI service container rather than being mocked: same fidelity argument, catches real provider-specific behavior (e.g. Npgsql's strict UTC handling for `timestamp with time zone`).
- Secrets/config: CI's env var override and local `appsettings.Local.json` are the same `IConfiguration` system, different sources — nothing Postgres- or EF-specific about it.
