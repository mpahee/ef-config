
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

Console.WriteLine("Hello, World!");

// Builds an IConfiguration from appsettings.json - the modern replacement
// for EF6's App.config/ConfigurationManager.ConnectionStrings lookup.
// SetBasePath ensures it finds the file next to the built exe (CopyToOutputDirectory).
// appsettings.Local.json (gitignored, optional) layers real local
// credentials on top of the committed placeholder - it's loaded last so its
// values win. In CI/production, environment variables or GitHub Secrets
// would override the same way instead of a local file.
IConfiguration configuration = new ConfigurationBuilder()
    .SetBasePath(AppContext.BaseDirectory)
    .AddJsonFile("appsettings.json")
    .AddJsonFile("appsettings.Local.json", optional: true)
    .Build();

string connectionString = configuration.GetConnectionString("AppConnectionString")
    ?? throw new InvalidOperationException("Connection string 'AppConnectionString' not found in appsettings.json");

// IServiceCollection is the DI container's registry: you describe what types
// are available and how to build them, then BuildServiceProvider() turns
// that into a resolvable container (the ServiceProvider).
var services = new ServiceCollection();

// AddDbContext registers DataContext with a Scoped lifetime by default and
// wires up DbContextOptions<DataContext> - the DI-friendly way to configure
// EF Core, replacing the OnConfiguring override used in Step 1.
// Step 2 (SQL Server): services.AddDbContext<DataContext>(options => options.UseSqlServer(connectionString));
// Step 3 (Postgres) - only this one method call changed to swap providers:
services.AddDbContext<DataContext>(options => options.UseNpgsql(connectionString));

// Registering the repository as Scoped matches DataContext's lifetime - a
// PersonRepository and the DataContext it wraps should live and die
// together within the same unit of work/scope.
services.AddScoped<IPersonRepository, PersonRepository>();

using ServiceProvider serviceProvider = services.BuildServiceProvider();

// CreateScope mirrors what ASP.NET Core does per HTTP request: a Scoped
// service (like DataContext) lives for the lifetime of the scope, not the
// whole app. Outside a web app there's no natural "request", so we create
// one scope manually for this run.
using var scope = serviceProvider.CreateScope();

// We no longer resolve DataContext directly - the container builds
// PersonRepository, sees its constructor needs a DataContext, and resolves
// that first (constructor injection happens automatically, recursively).
IPersonRepository personRepository = scope.ServiceProvider.GetRequiredService<IPersonRepository>();
DataContext context = scope.ServiceProvider.GetRequiredService<DataContext>();

// Creates the database/tables from the model if they don't exist yet, but
// does NOT use migrations - it's a quick dev-time substitute for `dotnet ef
// database update`. EnsureCreated() and migrations cannot be mixed on the
// same DB once migrations are introduced.
context.Database.EnsureCreated();

// Only seed once - otherwise every run of this console app would insert
// another 100 rows, since there's no migrations/idempotency check here.
if (!personRepository.Any())
{
    // Generates 100 Person records in memory first, then inserts them with
    // one AddRange()+SaveChanges() pair - a single round trip, instead of
    // looping Add()+SaveChanges() 100 times.
    var newPeople = Enumerable.Range(1, 100)
        .Select(i => new Person { Name = $"Person {i}", Address = $"{i} Sample Street" });
    personRepository.AddRange(newPeople);
}

var myUsers = personRepository.GetAll();
Console.WriteLine("DoneReading");
Console.Read();

// EF Core DbContext: represents a session with the database (unit of work +
// identity map).
class DataContext : DbContext
{
    // Each DbSet<T> property is a queryable, in-memory-tracked collection
    // mapped to a table. EF Core infers the table name ("Users") and column
    // mappings from this property and the Person class by convention.
    public DbSet<Person> Users { get; set; }

    // DbContextOptions<DataContext> is supplied by the DI container (built
    // from the AddDbContext registration above), not constructed by hand.
    // This constructor signature is exactly what AddDbContext expects to
    // find via reflection when it builds a DataContext instance.
    public DataContext(DbContextOptions<DataContext> options) : base(options)
    {
    }
}

// Abstraction in front of data access. Program.cs depends on this interface,
// not on EF Core or DataContext directly - in a test you could register a
// fake IPersonRepository instead of standing up a real database.
interface IPersonRepository
{
    List<Person> GetAll();
    void Add(Person person);
    void AddRange(IEnumerable<Person> people);
    bool Any();
}

// Concrete implementation. DataContext arrives via constructor injection -
// PersonRepository never constructs it itself, so it doesn't know or care
// whether the container builds it from SQL Server, Postgres, or a test
// double options instance (e.g. an in-memory provider).
class PersonRepository : IPersonRepository
{
    private readonly DataContext _context;

    public PersonRepository(DataContext context)
    {
        _context = context;
    }

    public List<Person> GetAll()
    {
        // LINQ against DbSet<Person> is translated to SQL by EF Core's
        // query provider only when enumerated (ToList() triggers execution).
        return _context.Users.ToList();
    }

    public bool Any()
    {
        // Any() translates to a SQL "EXISTS" query - far cheaper than
        // pulling every row back just to check Count > 0.
        return _context.Users.Any();
    }

    public void Add(Person person)
    {
        // Add() only marks the entity as "Added" in the change tracker - no
        // SQL runs yet. SaveChanges() is what generates and executes the
        // INSERT statement(s), inside an implicit transaction.
        _context.Users.Add(person);
        _context.SaveChanges();
    }

    public void AddRange(IEnumerable<Person> people)
    {
        // AddRange tracks every entity in one call instead of one Add() call
        // per entity. The real win is calling SaveChanges() only once
        // afterwards: EF Core batches all the INSERTs into a single round
        // trip/transaction instead of one round trip per row (avoids the
        // classic "N+1 SaveChanges" mistake of looping Add()+SaveChanges()).
        _context.Users.AddRange(people);
        _context.SaveChanges();
    }
}

// Data Annotations make the mapping explicit instead of relying on naming
// conventions (handy when a property/class name doesn't match what EF Core
// would infer, e.g. table name differs from the DbSet name, or the PK isn't
// named "<ClassName>Id"/"Id"). Same attributes existed in EF6 - this is one
// of the few mapping mechanisms that carried over unchanged.
// Alternative: Fluent API in OnModelCreating, used when mapping logic is
// more complex than attributes can express (composite keys, value
// conversions, relationships across many entities).
[Table("Users")]
class Person
{
    [Key] // marks the primary key explicitly; without it EF Core would
          // still infer PersonId as the PK by convention, but here it's stated.
    public int PersonId { get; set; }

    [Required] // generates a NOT NULL column instead of relying on the
               // non-nullable reference type alone.
    [MaxLength(100)] // generates nvarchar(100) instead of nvarchar(max).
    public string Name { get; set; }

    [MaxLength(200)]
    public string Address { get; set; }
}
