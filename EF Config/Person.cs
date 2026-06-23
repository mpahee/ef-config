using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

// Data Annotations make the mapping explicit instead of relying on naming
// conventions (handy when a property/class name doesn't match what EF Core
// would infer, e.g. table name differs from the DbSet name, or the PK isn't
// named "<ClassName>Id"/"Id"). Same attributes existed in EF6 - this is one
// of the few mapping mechanisms that carried over unchanged.
// Alternative: Fluent API in OnModelCreating, used when mapping logic is
// more complex than attributes can express (composite keys, value
// conversions, relationships across many entities).
[Table("Users")]
public class Person
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
