// ============================================================================
// AppDbContext.cs
// ----------------------------------------------------------------------------
// The DbContext is the heart of Entity Framework Core. It is the "bridge"
// between our C# classes and the SQL Server database:
//
//   * Each DbSet<T> property represents one table (Assets, Offices, ...).
//   * OnModelCreating describes the details EF Core can't guess by itself:
//     max text lengths, unique values, relationships and what happens on delete.
//
// From this description, "dotnet ef migrations add" generates a migration
// (see Data/Migrations). A migration is C# code that creates or changes the
// tables in the database.
// ============================================================================

// "using" lines import namespaces, so we can use their classes without writing the full name.
using Microsoft.EntityFrameworkCore;
using SmartAssetTracking.Models;

namespace SmartAssetTracking.Data;

// "AppDbContext : DbContext" means our class inherits all of EF Core's database features.
// The "(DbContextOptions<AppDbContext> options)" after the class name is a "primary constructor":
// a short way to declare a constructor parameter. "DbContext(options)" passes it on to the
// parent class. The options contain things like the connection string.
public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    // Each DbSet is a table we can query with LINQ, e.g. db.Assets.Where(a => a.Brand == "HP").
    // "=> Set<Asset>()" asks EF Core for the table object every time the property is read.
    public DbSet<Asset> Assets => Set<Asset>();
    public DbSet<Office> Offices => Set<Office>();
    public DbSet<Employee> Employees => Set<Employee>();
    public DbSet<User> Users => Set<User>();

    // EF Core calls this method once to learn how the tables should look.
    // "protected override" means we are replacing a method from the DbContext parent class.
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Splitting the setup into small methods keeps each one short and readable.
        ConfigureAssets(modelBuilder);
        ConfigureOffices(modelBuilder);
        ConfigureEmployees(modelBuilder);
        ConfigureUsers(modelBuilder);
    }

    private static void ConfigureAssets(ModelBuilder modelBuilder)
    {
        // "entity => { ... }" is a "lambda": a small unnamed function we pass to another method.
        // Here EF Core gives us "entity" and we describe the Asset table on it.
        modelBuilder.Entity<Asset>(entity =>
        {
            entity.ToTable("Assets");

            // Table-per-hierarchy: all asset types live in one table, separated by the "AssetType" column.
            // There are 5 asset classes but only ONE table. EF Core adds an extra column,
            // "AssetType" (the "discriminator"), that stores "Laptop", "Tablet" and so on.
            // When EF Core reads a row it looks at that column and creates the right
            // class: a row with "Laptop" becomes a Laptop object.
            // nameof(AssetKind.Laptop) gives the text "Laptop". If we ever rename the
            // enum value, the compiler catches the mistake.
            entity.HasDiscriminator<string>("AssetType")
                .HasValue<Laptop>(nameof(AssetKind.Laptop))
                .HasValue<Desktop>(nameof(AssetKind.Desktop))
                .HasValue<MobilePhone>(nameof(AssetKind.MobilePhone))
                .HasValue<Tablet>(nameof(AssetKind.Tablet))
                .HasValue<OfficeEquipment>(nameof(AssetKind.OfficeEquipment));
            entity.Property("AssetType").HasMaxLength(30);

            // These properties are calculated in C# and must NOT become database columns.
            entity.Ignore(a => a.Kind);
            entity.Ignore(a => a.Category);
            entity.Ignore(a => a.TypeName);
            entity.Ignore(a => a.DisplayName);

            // Column rules. HasMaxLength(50) becomes nvarchar(50) in SQL Server.
            // IsRequired() means the column can't be NULL (the value must exist).
            entity.Property(a => a.Brand).HasMaxLength(50).IsRequired();
            entity.Property(a => a.ModelName).HasMaxLength(100).IsRequired();
            entity.Property(a => a.SerialNumber).HasMaxLength(50).IsRequired();

            // Precision 18, scale 2 means up to 18 digits, of which 2 are decimals: decimal(18,2).
            entity.Property(a => a.PurchasePriceUsd).HasPrecision(18, 2);

            // "date" stores only the day, with no time of day.
            entity.Property(a => a.PurchaseDate).HasColumnType("date");
            entity.Property(a => a.WarrantyExpirationDate).HasColumnType("date");
            entity.Property(a => a.LastMaintenanceDate).HasColumnType("date");
            entity.Property(a => a.NextMaintenanceDate).HasColumnType("date");
            entity.Property(a => a.MaintenanceNotes).HasMaxLength(500);

            // A unique index means two assets can never have the same serial number.
            // The database refuses duplicates.
            entity.HasIndex(a => a.SerialNumber).IsUnique();

            // Relationship: each asset has ONE office, and an office has MANY assets.
            entity.HasOne(a => a.Office)
                .WithMany(o => o.Assets)
                .HasForeignKey(a => a.OfficeId)
                .OnDelete(DeleteBehavior.Restrict); // an office with assets cannot be deleted

            // Relationship: each asset MAY have one employee, and an employee has many assets.
            entity.HasOne(a => a.Employee)
                .WithMany(e => e.AssignedAssets)
                .HasForeignKey(a => a.EmployeeId)
                .OnDelete(DeleteBehavior.SetNull); // deleting an employee un-assigns their assets
        });
    }

    private static void ConfigureOffices(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Office>(entity =>
        {
            entity.Property(o => o.Name).HasMaxLength(100).IsRequired();
            entity.Property(o => o.Country).HasMaxLength(60).IsRequired();
            entity.Property(o => o.CurrencyCode).HasMaxLength(3).IsRequired();
            entity.HasIndex(o => o.Name).IsUnique();

            // Offices are static reference data, so they are seeded through the migration.
            // "HasData" puts these rows into the migration. When the database is
            // created, the four offices from the assignment (Level 3) are already there.
            entity.HasData(
                new Office { Id = 1, Name = "Sweden Office", Country = "Sweden", CurrencyCode = "SEK" },
                new Office { Id = 2, Name = "USA Office", Country = "USA", CurrencyCode = "USD" },
                new Office { Id = 3, Name = "Germany Office", Country = "Germany", CurrencyCode = "EUR" },
                new Office { Id = 4, Name = "Turkey Office", Country = "Turkey", CurrencyCode = "TRY" });
            // The "new Office { Id = 1, ... }" syntax is an "object initializer": it
            // creates the object and sets its properties in one step.
        });
    }

    private static void ConfigureEmployees(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Employee>(entity =>
        {
            entity.Property(e => e.FullName).HasMaxLength(100).IsRequired();
            entity.Property(e => e.Department).HasMaxLength(60).IsRequired();
            entity.Property(e => e.Email).HasMaxLength(150).IsRequired();
            entity.HasIndex(e => e.Email).IsUnique(); // two employees can't share an email
        });
    }

    private static void ConfigureUsers(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<User>(entity =>
        {
            entity.Property(u => u.Username).HasMaxLength(50).IsRequired();
            entity.Property(u => u.PasswordHash).HasMaxLength(200).IsRequired();

            // Store the role as readable text ("Admin") instead of a number (3).
            // This makes the database easier to understand when you look at it directly.
            entity.Property(u => u.Role).HasConversion<string>().HasMaxLength(20);
            entity.HasIndex(u => u.Username).IsUnique();

            // A user can be linked to one employee. ".WithMany()" with no argument
            // means Employee has no list of users. We don't need one.
            entity.HasOne(u => u.Employee)
                .WithMany()
                .HasForeignKey(u => u.EmployeeId)
                .OnDelete(DeleteBehavior.SetNull);
        });
    }
}
