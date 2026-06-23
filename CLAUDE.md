# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Commands

- Build: `dotnet build "EF Config.slnx"`
- Run: `dotnet run --project "EF Config/EF Config.csproj"`

There are no automated tests in this repository.

## Architecture

This is a minimal .NET 10 console app (`EF Config/Program.cs`) demonstrating legacy EF6 (`EntityFramework` NuGet package, not EF Core) configured via `App.config` connection strings rather than code-first connection setup.

- `DataContext` (in `Program.cs`) is a `DbContext` subclass with a single `DbSet<Person> Users`. Its constructor takes a connection string *name* (e.g. `"name = AppConnectionString"`), which EF6 resolves against `<connectionStrings>` entries in `App.config`.
- `App.config` defines two connection strings — `DataContext` and `AppConnectionString` — both pointing at different databases on `(localdb)\MSSQLLocalDB`. Switching which one `Program.cs` uses changes which LocalDB database is queried.
- `Person` is a plain POCO mapped implicitly by EF6 convention (no Fluent API or migrations are set up).

Because this uses EF6 (not EF Core), `dotnet ef` migration tooling does not apply; schema must exist already in the target LocalDB database for `context.Users.ToList()` to succeed.
