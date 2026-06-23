using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;

namespace EF_Config.IntegrationTests;

// Unlike PersonRepositoryTests (EF Config.Tests, InMemory provider), this
// runs against a real, disposable Postgres container via Testcontainers -
// it validates actual Npgsql query translation and type mapping, not just
// EF Core's provider-agnostic change tracking. Requires Docker to be
// running; Testcontainers assigns a random free host port per container,
// so it won't collide with a locally-installed Postgres on 5432.
[Trait("Category", "Integration")]
public class PersonRepositoryIntegrationTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:16")
        .WithDatabase("efconfig_test")
        .WithUsername("postgres")
        .WithPassword("postgres")
        .Build();

    public Task InitializeAsync() => _postgres.StartAsync();

    public Task DisposeAsync() => _postgres.DisposeAsync().AsTask();

    private DataContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<DataContext>()
            .UseNpgsql(_postgres.GetConnectionString())
            .Options;

        var context = new DataContext(options);
        // EnsureCreated, not migrations: this demo has no migration history,
        // so each fresh container gets schema-from-model the same way
        // Program.cs does against the real dev database.
        context.Database.EnsureCreated();
        return context;
    }

    [Fact]
    public void GetAll_AgainstRealPostgres_ReturnsSeededRows()
    {
        using var context = CreateContext();
        var repository = new PersonRepository(context);
        repository.AddRange(new[]
        {
            new Person { Name = "Alan Turing", Address = "1 Bletchley Park" },
            new Person { Name = "Margaret Hamilton", Address = "1 Apollo Way" },
        });

        var result = repository.GetAll();

        result.Should().HaveCount(2);
        result.Select(p => p.Name).Should().Contain("Alan Turing", "Margaret Hamilton");
    }

    [Fact]
    public void Any_EmptyRealDatabase_ReturnsFalse()
    {
        using var context = CreateContext();
        var repository = new PersonRepository(context);

        repository.Any().Should().BeFalse();
    }
}
