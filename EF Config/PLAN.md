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
