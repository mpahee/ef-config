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

### Step 7 follow-up — Updated for Step 9's WPF project (two jobs now) — DONE
Once `EF Config.Wpf`/`EF Config.Wpf.Tests` (`net10.0-windows`, `UseWPF=true`) joined `EF Config.slnx`, the original single job's `dotnet build "EF Config.slnx"` would break — WPF projects can't build on a Linux runner. Restructured `ci.yml` into two independent jobs:
- `build-and-test` (`ubuntu-latest`, unchanged in spirit): now restores/builds/tests **explicit project paths** (`EF Config/EF Config.csproj`, `EF Config.Tests/EF Config.Tests.csproj`, `EF Config.IntegrationTests/EF Config.IntegrationTests.csproj`) instead of the whole `.slnx`, so it never touches the WPF projects.
- `build-and-test-wpf` (new, `windows-latest`): restores/builds `EF Config.Wpf` + `EF Config.Wpf.Tests`, then runs the 9 ViewModel unit tests.
- Both jobs run independently and must both pass; verified locally first by running each job's exact `restore`/`build`/`test` commands by hand in `-c Release` (matching CI's config) before pushing — all green, including the full `dotnet test "EF Config.slnx"` run together (16/16: 5 + 9 + 2).

## Step 8 — CD (optional sketch, superseded by Step 10)
- Gate on Step 7's CI job passing.
- `dotnet publish` the project; if presenting a containerized story, build and push a Docker image to GHCR on tag push.
- Frame this as "the same pattern as a real service, scaled down" — not a hard requirement for the interview unless asked.
- **Superseded**: this was a one-paragraph sketch for the *console* app, written before the WPF app existed. The actual CD implementation (Step 10) targets the WPF app instead — a Docker/GHCR story never made sense for a WPF GUI app, and once the WPF app existed it became the more interesting CD target for an interview. This section is kept as historical context only.

## Step 9 — WPF MVVM CRUD UI (interview requirement: MVVM) — DONE (in progress, unit tests pending)
- New project `EF Config.Wpf` (`OutputType=WinExe`, `TargetFramework=net10.0-windows`, `UseWPF=true`), added to `EF Config.slnx`. References `EF Config/EF Config.csproj` directly — referencing an `OutputType=Exe` project as a library reference is fine (already proven by `EF Config.Tests` doing the same); a `net10.0-windows` project can reference a plain `net10.0` project (compatible TFM superset), not the reverse.
- Extended `IPersonRepository`/`PersonRepository` (`EF Config/PersonRepository.cs`) with `Update(Person)` and `Delete(Person)`, matching the existing per-operation `SaveChanges()` style, to support full CRUD instead of just read/insert.
- Hand-rolled MVVM plumbing (no toolkit) in `EF Config.Wpf/Mvvm/`: `RelayCommand` (`ICommand`, wired to `CommandManager.RequerySuggested` for auto re-query, plus a manual `RaiseCanExecuteChanged()`), `ViewModelBase` (`INotifyPropertyChanged` + `SetField<T>` helper, pure BCL — Avalonia-portable).
- `MainViewModel` (`EF Config.Wpf/ViewModels/MainViewModel.cs`): `ObservableCollection<Person> People`, `SelectedPerson`, separate `EditingName`/`EditingAddress` form fields, `AddCommand`/`UpdateCommand`/`DeleteCommand`/`RefreshCommand`. Depends only on `IPersonRepository` — no EF Core/`DataContext` knowledge, trivially unit-testable.
- `MainWindow.xaml`/`.xaml.cs`: `DataGrid` bound to `People` + `SelectedItem`, form `TextBox`es bound to `EditingName`/`EditingAddress`, buttons bound to the four commands. `MainWindow(MainViewModel viewModel)` constructor — no parameterless constructor, forces DI to supply the ViewModel.
- `App.xaml`/`App.xaml.cs` composition root: removed `StartupUri="MainWindow.xaml"` (it requires a parameterless constructor, which `MainWindow` no longer has); `OnStartup` now builds `IConfiguration` (same `appsettings.json`/`appsettings.Local.json` pattern as `Program.cs`), registers `DataContext`/`IPersonRepository`/`MainViewModel`/`MainWindow` via DI, holds **one scope for the app's session** (vs. `Program.cs`'s one scope per console run), calls `EnsureCreated()`, resolves and shows `MainWindow`; `OnExit` disposes the scope/provider.
- New `EF Config.Wpf/appsettings.json` (placeholder) + local-only `appsettings.Local.json` (gitignored, same bare-filename `.gitignore` pattern already covers it) — config is per-project, not shared with the console app's output.
- **Bug found and fixed during manual verification:** editing a row and clicking Update changed the database but didn't refresh the DataGrid cell until a manual Refresh. Root cause: `Person` didn't implement `INotifyPropertyChanged`, so mutating `SelectedPerson.Name`/`Address` in place raised no notification the DataGrid could react to. First attempted fix (replacing the item at its index in `People` to force a collection-level `Replace` notification) was insufficient. Real fix: made `Person` (`EF Config/Person.cs`) implement `INotifyPropertyChanged` directly, raising `PropertyChanged` from the `Name`/`Address` setters — EF Core fully supports "notification entities" implementing `INotifyPropertyChanged`, so this is safe for the console app and existing tests too (verified: all 5 `EF Config.Tests` unit tests still pass, full solution still builds with 0 warnings — the change also incidentally resolved the long-standing `CS8618` nullable warnings on `Person.Name`/`Address`, since the backing fields now default to `string.Empty` instead of being implicitly null).
- Remaining: unit tests for `MainViewModel` (new `EF Config.Wpf.Tests` project, hand-rolled `FakePersonRepository` test double, no Moq), CI job for the WPF project on `windows-latest`, and a final `InterviewQA.md`/`PLAN.md` pass once tests are in.
- **Update**: all of the above completed — `EF Config.Wpf.Tests` added (9 ViewModel tests, see Step 9 follow-up), `ci.yml` got its `build-and-test-wpf` job (Step 7 follow-up above), docs current.

## Step 10 — CD for the WPF app via tagged GitHub Releases — DONE
- New workflow `.github/workflows/cd-wpf.yml`, triggered only on `push: tags: ['v*']` — a separate file from `ci.yml` rather than a third job in it, since `ci.yml`'s trigger (`push`/`pull_request`, no tag filter) would otherwise redundantly re-run the existing two jobs on every tag push too.
- Two jobs: `test-wpf` (rebuilds + reruns the 9 ViewModel unit tests independently — `needs:` only works between jobs in the *same* workflow file, so this can't depend on `ci.yml`'s already-passed job; re-verifying here guarantees the exact tagged commit is checked, at the cost of one redundant build), then `publish-and-release` (`needs: test-wpf`, so a failing build/test can never produce a release).
- `publish-and-release` strips the leading `v` from the pushed tag (`github.ref_name`, e.g. `v1.2.3` → `1.2.3`) for `-p:Version=`, runs `dotnet publish "EF Config.Wpf/EF Config.Wpf.csproj" -c Release -r win-x64 --self-contained true -p:Version=... -o publish/EF-Config-Wpf`, zips the output with PowerShell's `Compress-Archive` (`windows-latest` runners default to `pwsh`), then `gh release create` (preinstalled on GitHub-hosted runners, no extra marketplace action) to publish the GitHub Release with the zip attached. Needs `permissions: contents: write` for `gh release create` to use the auto-provided `GITHUB_TOKEN`.
- **Two real bugs found and fixed only by actually running the publish locally before pushing** (the original design sketch assumed both would just work):
  1. Setting `<RuntimeIdentifier>`/`<SelfContained>true</SelfContained>` directly in `EF Config.Wpf.csproj`'s main `PropertyGroup` broke plain `dotnet build` with `NETSDK1150` — a self-contained executable project cannot reference a non-self-contained executable project, and `EF Config.csproj` (the console app) is exactly that. Fix: don't set these in the csproj at all; pass `-r win-x64 --self-contained true` purely as CLI flags on the `dotnet publish` command in the workflow, leaving normal build/test runs untouched.
  2. `dotnet publish` on `EF Config.Wpf` failed with `NETSDK1152` ("multiple publish output files with the same relative path") — `EF Config.csproj`'s own `appsettings.json`/`appsettings.Local.json` (`CopyToOutputDirectory="PreserveNewest"`) flow transitively into a *referencing* project's publish output via the `ProjectReference`, colliding with `EF Config.Wpf`'s own same-named files. Fix: added `CopyToPublishDirectory="Never"` to those items in `EF Config/EF Config.csproj` — the console app is never itself published by this CD pipeline, so this only stops its files from leaking into `EF Config.Wpf`'s publish output, without touching the console app's own `CopyToOutputDirectory`-driven `dotnet build`/`dotnet run` behavior at all.
- **Correction to an earlier assumption**: it was assumed `EF Config.Wpf`'s publish output would include the console project's compiled code only as a DLL, not a second standalone executable. Verified locally that this is wrong — `EF Config.exe` (the console app's own executable, since `EF Config.csproj` is `OutputType=Exe`) does appear alongside `EF Config.Wpf.exe` in the publish output. Harmless (the WPF app's shortcut/launch still correctly points at `EF Config.Wpf.exe`), but worth knowing precisely rather than assuming.
- Verified locally end-to-end before pushing: `dotnet publish ... -r win-x64 --self-contained true -o publish/EF-Config-Wpf` (succeeded, ~148MB output, confirmed `EF Config.Wpf.exe` + `appsettings.json` present) → `Compress-Archive` (succeeded, ~65MB zip) → full solution build (`dotnet build "EF Config.slnx"`, 0 warnings/errors) and full test run (`dotnet test "EF Config.slnx"`, 16/16: 5 + 9 + 2) both still pass after the csproj changes.

## Interview talking points (Steps 4-8)
- InMemory provider for fast unit tests vs Testcontainers for integration tests: speed vs fidelity — InMemory doesn't validate real SQL translation, Testcontainers does.
- Why Postgres runs as a CI service container rather than being mocked: same fidelity argument, catches real provider-specific behavior (e.g. Npgsql's strict UTC handling for `timestamp with time zone`).
- Secrets/config: CI's env var override and local `appsettings.Local.json` are the same `IConfiguration` system, different sources — nothing Postgres- or EF-specific about it.
