// ============================================================================
// DbSeeder.cs
// ----------------------------------------------------------------------------
// "Seeding" means filling an empty database with starting data. This lets
// you try the program right away with users, employees and assets.
//
// The seeder only adds data if the table is EMPTY. Running the program again
// never creates duplicates, and your own changes are kept.
// ============================================================================

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SmartAssetTracking.Helpers;
using SmartAssetTracking.Models;

namespace SmartAssetTracking.Data;

/// <summary>
/// Seeds demo users, employees and assets the first time the application runs.
/// Purchase dates are relative to today so the demo always shows every lifecycle status.
/// </summary>
// The primary constructor receives three "dependencies" (things this class needs):
//   * contextFactory - creates database connections (AppDbContext objects)
//   * timeProvider   - tells us today's date (tests can replace it with a fixed date)
//   * logger         - writes messages to the log file
// We never create these with "new" ourselves. The dependency injection container
// (see Helpers/ServiceRegistration.cs) creates them and passes them in.
public class DbSeeder(IDbContextFactory<AppDbContext> contextFactory, TimeProvider timeProvider, ILogger<DbSeeder> logger)
{
    // "async Task" means this method is asynchronous. It can wait for slow work,
    // like talking to the database, without freezing the program. Inside an async
    // method we use "await" to wait for each slow operation to finish.
    // The CancellationToken lets someone ask the method to stop early. Here it defaults to "never".
    public async Task SeedAsync(CancellationToken ct = default)
    {
        // "await using" creates a database context and automatically closes it
        // ("disposes" it) when the method ends, even if an error happens.
        await using var db = await contextFactory.CreateDbContextAsync(ct);

        // AnyAsync() asks the database: "is there at least one row?" The "!" means "not".
        if (!await db.Employees.AnyAsync(ct))
        {
            // AddRange adds several objects at once. Nothing is written to the database yet...
            db.Employees.AddRange(
                new Employee { FullName = "Sarah Johnson", Department = "IT Support", Email = "sarah.johnson@smartasset.example" },
                new Employee { FullName = "Anna Svensson", Department = "Finance", Email = "anna.svensson@smartasset.example" },
                new Employee { FullName = "Michael Brown", Department = "Sales", Email = "michael.brown@smartasset.example" },
                new Employee { FullName = "Lukas Becker", Department = "Engineering", Email = "lukas.becker@smartasset.example" },
                new Employee { FullName = "Elif Yilmaz", Department = "Marketing", Email = "elif.yilmaz@smartasset.example" });

            // ...until SaveChangesAsync, which sends the INSERT statements to SQL Server.
            await db.SaveChangesAsync(ct);
            logger.LogInformation("Seeded employees");
        }

        if (!await db.Users.AnyAsync(ct))
        {
            // FirstAsync returns the first employee matching the condition.
            // "e => e.FullName == ..." is a lambda that EF Core translates into a SQL WHERE clause.
            var sarah = await db.Employees.FirstAsync(e => e.FullName == "Sarah Johnson", ct);
            var michael = await db.Employees.FirstAsync(e => e.FullName == "Michael Brown", ct);

            // Passwords are hashed before they are saved (see PasswordHasher).
            // The login "admin / 1234" comes straight from the assignment (Level 5).
            db.Users.AddRange(
                new User { Username = "admin", PasswordHash = PasswordHasher.Hash("1234"), Role = UserRole.Admin },
                new User { Username = "manager", PasswordHash = PasswordHasher.Hash("1234"), Role = UserRole.Manager, EmployeeId = michael.Id },
                new User { Username = "employee", PasswordHash = PasswordHasher.Hash("1234"), Role = UserRole.Employee, EmployeeId = sarah.Id });
            await db.SaveChangesAsync(ct);
            logger.LogInformation("Seeded users");
        }

        if (!await db.Assets.AnyAsync(ct))
        {
            // ToDictionaryAsync builds a lookup table: name -> id, e.g. "Sarah Johnson" -> 1.
            var employees = await db.Employees.ToDictionaryAsync(e => e.FullName, e => e.Id, ct);
            db.Assets.AddRange(CreateDemoAssets(timeProvider.GetLocalNow().Date, employees));
            await db.SaveChangesAsync(ct);
            logger.LogInformation("Seeded demo assets");
        }
    }

    // IEnumerable<Asset> means "something you can loop over that gives Asset objects".
    // IReadOnlyDictionary is a dictionary (key -> value lookup) that can't be changed.
    private static IEnumerable<Asset> CreateDemoAssets(DateTime today, IReadOnlyDictionary<string, int> employees)
    {
        // "Age" helper: purchased this many months ago. Lifetime is 3 years (36 months).
        // This is a "local function": a small helper method that only exists inside this method.
        // Using dates relative to TODAY means the demo always shows a mix of
        // NORMAL, YELLOW, RED and EXPIRED assets, no matter when you run it.
        DateTime Ago(int months, int days = 0) => today.AddMonths(-months).AddDays(-days);

        // These numbers match the office Ids created by HasData in AppDbContext.
        const int sweden = 1, usa = 2, germany = 3, turkey = 4;

        // Look up each employee's database Id by their name.
        int? sarah = employees["Sarah Johnson"], anna = employees["Anna Svensson"], michael = employees["Michael Brown"],
             lukas = employees["Lukas Becker"], elif = employees["Elif Yilmaz"];

        // "employeeId: sarah" is a "named argument": we name the parameter we are
        // setting, so we can skip the optional warranty parameter that comes before it.
        var thinkPad = new Laptop("Lenovo", "ThinkPad X1", "LEN-X1-0001", Ago(34, 10), 1650m, sweden, employeeId: sarah);
        thinkPad.RecordMaintenance(Ago(3), today.AddDays(10), "Battery replaced, OS reinstalled.");

        // The "m" after a number (420m) makes it a decimal instead of a double.
        var printer = new OfficeEquipment("HP", "LaserJet Pro M404", "HP-LJ-4040", Ago(20), 420m, sweden);
        printer.RecordMaintenance(Ago(7), today.AddDays(-5), "Toner and drum replaced.");

        // Return a list (collection expression [ ... ]) with all demo assets.
        // Because every class inherits from Asset, a Laptop, a Tablet and a Desktop
        // can all live in the same list of Assets.
        return
        [
            // Sweden
            thinkPad,                                                                                       // YELLOW (< 3 months left)
            new MobilePhone("Apple", "iPhone 15", "APL-IP15-0001", Ago(10), 980m, sweden, employeeId: sarah), // NORMAL
            new Tablet("Samsung", "Galaxy Tab S9", "SAM-TS9-0001", Ago(14), 850m, sweden, employeeId: sarah),
            new Desktop("Dell", "OptiPlex 7010", "DEL-OP-7010", Ago(26), 1100m, sweden, employeeId: anna),
            new MobilePhone("Motorola", "Razr 40", "MOT-RZR-0001", Ago(31, 5), 970m, sweden, employeeId: anna), // RED (3-6 months left)
            printer,

            // USA
            new Laptop("Asus", "ZenBook 14", "ASU-ZB14-0001", Ago(32), 1200m, usa, employeeId: michael),       // RED
            new Laptop("Lenovo", "Yoga 730", "LEN-Y730-0001", Ago(40), 835m, usa),                             // EXPIRED
            new MobilePhone("Apple", "iPhone 14 Pro", "APL-IP14P-0001", Ago(22), 1099m, usa, employeeId: michael),
            new Desktop("Asus", "ROG Station", "ASU-ROG-0001", Ago(8), 2400m, usa),
            new MobilePhone("Samsung", "Galaxy S24", "SAM-S24-0001", Ago(12), 899m, usa),

            // Germany
            new Laptop("Lenovo", "Legion 5", "LEN-LG5-0001", Ago(18), 1450m, germany, employeeId: lukas),
            new MobilePhone("Nokia", "XR20", "NOK-XR20-0001", Ago(34, 20), 499m, germany, employeeId: lukas), // YELLOW
            new Laptop("HP", "EliteBook 840", "HP-EB840-0001", Ago(5), 1423m, germany),
            new OfficeEquipment("Epson", "EB-L520U Projector", "EPS-PRJ-0001", Ago(30, 15), 1900m, germany), // RED

            // Turkey
            new Laptop("Apple", "MacBook Pro 14", "APL-MBP14-0001", Ago(6), 1999m, turkey, employeeId: elif),
            new Tablet("Apple", "iPad Air", "APL-IPA-0001", Ago(38), 599m, turkey),                           // EXPIRED
            new MobilePhone("Samsung", "Galaxy A55", "SAM-A55-0001", Ago(2), 449m, turkey, employeeId: elif),
            new Desktop("HP", "Z2 Tower", "HP-Z2-0001", Ago(16), 1750m, turkey),
            new MobilePhone("Nokia", "G42", "NOK-G42-0001", Ago(33, 25), 249m, turkey),                       // YELLOW
        ];
    }
}
