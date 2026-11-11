using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SmartAssetTracking.Helpers;
using SmartAssetTracking.Models;

namespace SmartAssetTracking.Data;

/// <summary>
/// Seeds demo users, employees and assets the first time the application runs.
/// Purchase dates are relative to today so the demo always shows every lifecycle status.
/// </summary>
public class DbSeeder(IDbContextFactory<AppDbContext> contextFactory, TimeProvider timeProvider, ILogger<DbSeeder> logger)
{
    public async Task SeedAsync(CancellationToken ct = default)
    {
        await using var db = await contextFactory.CreateDbContextAsync(ct);

        if (!await db.Employees.AnyAsync(ct))
        {
            db.Employees.AddRange(
                new Employee { FullName = "Sarah Johnson", Department = "IT Support", Email = "sarah.johnson@smartasset.example" },
                new Employee { FullName = "Anna Svensson", Department = "Finance", Email = "anna.svensson@smartasset.example" },
                new Employee { FullName = "Michael Brown", Department = "Sales", Email = "michael.brown@smartasset.example" },
                new Employee { FullName = "Lukas Becker", Department = "Engineering", Email = "lukas.becker@smartasset.example" },
                new Employee { FullName = "Elif Yilmaz", Department = "Marketing", Email = "elif.yilmaz@smartasset.example" });
            await db.SaveChangesAsync(ct);
            logger.LogInformation("Seeded employees");
        }

        if (!await db.Users.AnyAsync(ct))
        {
            var sarah = await db.Employees.FirstAsync(e => e.FullName == "Sarah Johnson", ct);
            var michael = await db.Employees.FirstAsync(e => e.FullName == "Michael Brown", ct);
            db.Users.AddRange(
                new User { Username = "admin", PasswordHash = PasswordHasher.Hash("1234"), Role = UserRole.Admin },
                new User { Username = "manager", PasswordHash = PasswordHasher.Hash("1234"), Role = UserRole.Manager, EmployeeId = michael.Id },
                new User { Username = "employee", PasswordHash = PasswordHasher.Hash("1234"), Role = UserRole.Employee, EmployeeId = sarah.Id });
            await db.SaveChangesAsync(ct);
            logger.LogInformation("Seeded users");
        }

        if (!await db.Assets.AnyAsync(ct))
        {
            var employees = await db.Employees.ToDictionaryAsync(e => e.FullName, e => e.Id, ct);
            db.Assets.AddRange(CreateDemoAssets(timeProvider.GetLocalNow().Date, employees));
            await db.SaveChangesAsync(ct);
            logger.LogInformation("Seeded demo assets");
        }
    }

    private static IEnumerable<Asset> CreateDemoAssets(DateTime today, IReadOnlyDictionary<string, int> employees)
    {
        // "Age" helper: purchased this many months ago. Lifetime is 3 years (36 months).
        DateTime Ago(int months, int days = 0) => today.AddMonths(-months).AddDays(-days);

        const int sweden = 1, usa = 2, germany = 3, turkey = 4;
        int? sarah = employees["Sarah Johnson"], anna = employees["Anna Svensson"], michael = employees["Michael Brown"],
             lukas = employees["Lukas Becker"], elif = employees["Elif Yilmaz"];

        var thinkPad = new Laptop("Lenovo", "ThinkPad X1", "LEN-X1-0001", Ago(34, 10), 1650m, sweden, employeeId: sarah);
        thinkPad.RecordMaintenance(Ago(3), today.AddDays(10), "Battery replaced, OS reinstalled.");

        var printer = new OfficeEquipment("HP", "LaserJet Pro M404", "HP-LJ-4040", Ago(20), 420m, sweden);
        printer.RecordMaintenance(Ago(7), today.AddDays(-5), "Toner and drum replaced.");

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
