// Abstraction in front of data access. Program.cs depends on this interface,
// not on EF Core or DataContext directly - in a test you could register a
// fake IPersonRepository instead of standing up a real database.
public interface IPersonRepository
{
    List<Person> GetAll();
    void Add(Person person);
    void AddRange(IEnumerable<Person> people);
    void Update(Person person);
    void Delete(Person person);
    bool Any();
}

// Concrete implementation. DataContext arrives via constructor injection -
// PersonRepository never constructs it itself, so it doesn't know or care
// whether the container builds it from SQL Server, Postgres, or a test
// double options instance (e.g. an in-memory provider).
public class PersonRepository : IPersonRepository
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

    public void Update(Person person)
    {
        // Update() marks every property as Modified, not just the ones that
        // actually changed - fine for a small POCO like Person, but worth
        // knowing it's a coarser-grained alternative to mutating a tracked
        // entity's properties directly and letting EF Core detect changes.
        _context.Users.Update(person);
        _context.SaveChanges();
    }

    public void Delete(Person person)
    {
        _context.Users.Remove(person);
        _context.SaveChanges();
    }
}
