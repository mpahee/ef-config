namespace EF_Config.Wpf.Tests.Fakes;

// Hand-rolled test double, not a mocking library - consistent with the
// "hand-rolled, no toolkit" approach taken for the MVVM plumbing itself.
// Backed by a plain List<Person>, no EF Core/database involved at all.
public class FakePersonRepository : IPersonRepository
{
    private readonly List<Person> _people = new();
    private int _nextId = 1;

    public int AddCallCount { get; private set; }
    public int UpdateCallCount { get; private set; }
    public int DeleteCallCount { get; private set; }

    public List<Person> GetAll() => _people.ToList();

    public bool Any() => _people.Count > 0;

    public void Add(Person person)
    {
        person.PersonId = _nextId++;
        _people.Add(person);
        AddCallCount++;
    }

    public void AddRange(IEnumerable<Person> people)
    {
        foreach (var person in people)
        {
            Add(person);
        }
    }

    public void Update(Person person)
    {
        UpdateCallCount++;
    }

    public void Delete(Person person)
    {
        _people.Remove(person);
        DeleteCallCount++;
    }

    public void Seed(params Person[] people) => AddRange(people);
}
