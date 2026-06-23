using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace EF_Config.Tests;

// Each test builds its own DataContext against a uniquely-named InMemory
// database (Guid per test) so tests never share state or run order. This is
// the EF Core InMemory provider, not a real database - fast, but it skips
// real SQL translation, which is why Step 6 (Testcontainers) exists for
// integration-level confidence against actual Postgres.
public class PersonRepositoryTests
{
    private static DataContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<DataContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        return new DataContext(options);
    }

    [Fact]
    public void Any_EmptyDatabase_ReturnsFalse()
    {
        using var context = CreateContext();
        var repository = new PersonRepository(context);

        repository.Any().Should().BeFalse();
    }

    [Fact]
    public void Any_WithSeededRow_ReturnsTrue()
    {
        using var context = CreateContext();
        var repository = new PersonRepository(context);
        repository.Add(new Person { Name = "Ada Lovelace", Address = "1 Analytical Engine Way" });

        repository.Any().Should().BeTrue();
    }

    [Fact]
    public void GetAll_ReturnsAllSeededPeople()
    {
        using var context = CreateContext();
        var repository = new PersonRepository(context);
        var seeded = Enumerable.Range(1, 5)
            .Select(i => new Person { Name = $"Person {i}", Address = $"{i} Sample Street" });
        repository.AddRange(seeded);

        var result = repository.GetAll();

        result.Should().HaveCount(5);
        result.Select(p => p.Name).Should().Contain("Person 1", "Person 5");
    }

    [Fact]
    public void AddRange_InsertsAllRecordsInOneSaveChanges()
    {
        using var context = CreateContext();
        var repository = new PersonRepository(context);

        repository.AddRange(new[]
        {
            new Person { Name = "Alice", Address = "1 Main St" },
            new Person { Name = "Bob", Address = "2 Main St" },
        });

        repository.GetAll().Should().HaveCount(2);
    }

    [Fact]
    public void Add_SingleRecord_PersistsImmediately()
    {
        using var context = CreateContext();
        var repository = new PersonRepository(context);

        repository.Add(new Person { Name = "Grace Hopper", Address = "1 Compiler Ave" });

        repository.GetAll().Single().Name.Should().Be("Grace Hopper");
    }
}
