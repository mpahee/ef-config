
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

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
