using Microsoft.EntityFrameworkCore;

// EF Core DbContext: represents a session with the database (unit of work +
// identity map).
public class DataContext : DbContext
{
    // Each DbSet<T> property is a queryable, in-memory-tracked collection
    // mapped to a table. EF Core infers the table name ("Users") and column
    // mappings from this property and the Person class by convention.
    public DbSet<Person> Users { get; set; }

    // DbContextOptions<DataContext> is supplied by the DI container (built
    // from the AddDbContext registration in Program.cs), not constructed by
    // hand. This constructor signature is exactly what AddDbContext expects
    // to find via reflection when it builds a DataContext instance.
    public DataContext(DbContextOptions<DataContext> options) : base(options)
    {
    }
}
