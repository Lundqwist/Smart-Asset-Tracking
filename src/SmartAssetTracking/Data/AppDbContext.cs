using Microsoft.EntityFrameworkCore;
using SmartAssetTracking.Models;

namespace SmartAssetTracking.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<Asset> Assets => Set<Asset>();
    public DbSet<Office> Offices => Set<Office>();
    public DbSet<Employee> Employees => Set<Employee>();
    public DbSet<User> Users => Set<User>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ConfigureAssets(modelBuilder);
        ConfigureOffices(modelBuilder);
        ConfigureEmployees(modelBuilder);
        ConfigureUsers(modelBuilder);
    }

    private static void ConfigureAssets(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Asset>(entity =>
        {
            entity.ToTable("Assets");

            // Table-per-hierarchy: all asset types live in one table, separated by the "AssetType" column.
            entity.HasDiscriminator<string>("AssetType")
                .HasValue<Laptop>(nameof(AssetKind.Laptop))
                .HasValue<Desktop>(nameof(AssetKind.Desktop))
                .HasValue<MobilePhone>(nameof(AssetKind.MobilePhone))
                .HasValue<Tablet>(nameof(AssetKind.Tablet))
                .HasValue<OfficeEquipment>(nameof(AssetKind.OfficeEquipment));
            entity.Property("AssetType").HasMaxLength(30);

            entity.Ignore(a => a.Kind);
            entity.Ignore(a => a.Category);
            entity.Ignore(a => a.TypeName);
            entity.Ignore(a => a.DisplayName);

            entity.Property(a => a.Brand).HasMaxLength(50).IsRequired();
            entity.Property(a => a.ModelName).HasMaxLength(100).IsRequired();
            entity.Property(a => a.SerialNumber).HasMaxLength(50).IsRequired();
            entity.Property(a => a.PurchasePriceUsd).HasPrecision(18, 2);
            entity.Property(a => a.PurchaseDate).HasColumnType("date");
            entity.Property(a => a.WarrantyExpirationDate).HasColumnType("date");
            entity.Property(a => a.LastMaintenanceDate).HasColumnType("date");
            entity.Property(a => a.NextMaintenanceDate).HasColumnType("date");
            entity.Property(a => a.MaintenanceNotes).HasMaxLength(500);

            entity.HasIndex(a => a.SerialNumber).IsUnique();

            entity.HasOne(a => a.Office)
                .WithMany(o => o.Assets)
                .HasForeignKey(a => a.OfficeId)
                .OnDelete(DeleteBehavior.Restrict); // an office with assets cannot be deleted

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
            entity.HasData(
                new Office { Id = 1, Name = "Sweden Office", Country = "Sweden", CurrencyCode = "SEK" },
                new Office { Id = 2, Name = "USA Office", Country = "USA", CurrencyCode = "USD" },
                new Office { Id = 3, Name = "Germany Office", Country = "Germany", CurrencyCode = "EUR" },
                new Office { Id = 4, Name = "Turkey Office", Country = "Turkey", CurrencyCode = "TRY" });
        });
    }

    private static void ConfigureEmployees(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Employee>(entity =>
        {
            entity.Property(e => e.FullName).HasMaxLength(100).IsRequired();
            entity.Property(e => e.Department).HasMaxLength(60).IsRequired();
            entity.Property(e => e.Email).HasMaxLength(150).IsRequired();
            entity.HasIndex(e => e.Email).IsUnique();
        });
    }

    private static void ConfigureUsers(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<User>(entity =>
        {
            entity.Property(u => u.Username).HasMaxLength(50).IsRequired();
            entity.Property(u => u.PasswordHash).HasMaxLength(200).IsRequired();
            entity.Property(u => u.Role).HasConversion<string>().HasMaxLength(20);
            entity.HasIndex(u => u.Username).IsUnique();

            entity.HasOne(u => u.Employee)
                .WithMany()
                .HasForeignKey(u => u.EmployeeId)
                .OnDelete(DeleteBehavior.SetNull);
        });
    }
}
