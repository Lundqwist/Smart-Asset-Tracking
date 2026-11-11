// ============================================================================
// Asset.cs
// ----------------------------------------------------------------------------
// The base class for everything the company owns. A "class" is a blueprint
// that describes what data an object holds (properties) and what it can do
// (methods).
//
// This class is "abstract", which means you can never create a plain Asset
// directly. You always create a specific kind, such as a Laptop or a Tablet.
// Those classes "inherit" from Asset (see AssetTypes.cs). Inheritance means
// they automatically get every property and method written here, so we don't
// have to repeat Brand, ModelName, PurchaseDate and so on in each class.
//
// Entity Framework Core (EF Core) reads this class and creates a database
// table from it. Every public property with a getter and setter becomes a
// column in the "Assets" table.
// ============================================================================

namespace SmartAssetTracking.Models;

/// <summary>
/// Base class for every physical asset the company owns.
/// Stored in a single table (TPH) with a discriminator column identifying the concrete type.
/// </summary>
public abstract class Asset
{
    /// <summary>Warranty length used when no explicit warranty date is supplied.</summary>
    // "const" means the value is fixed when the program is compiled and can never change.
    public const int DefaultWarrantyYears = 2;

    /// <summary>Parameterless constructor required by Entity Framework Core.</summary>
    // A "constructor" is a special method that runs when a new object is created.
    // EF Core needs an empty constructor so it can create objects when it reads
    // rows from the database, and then fill in the properties itself.
    // "protected" means only this class and classes that inherit from it can use it.
    protected Asset()
    {
    }

    // This constructor is used by our own code when we register a new asset.
    // Parameters with "= null" are optional: the caller may leave them out.
    // The "?" after a type (DateTime?, int?) means the value may be "null", i.e. missing.
    protected Asset(
        string brand,
        string modelName,
        string serialNumber,
        DateTime purchaseDate,
        decimal purchasePriceUsd,
        int officeId,
        DateTime? warrantyExpirationDate = null,
        int? employeeId = null)
    {
        // Trim() removes spaces at the start and end, so " Lenovo " becomes "Lenovo".
        Brand = brand.Trim();
        ModelName = modelName.Trim();
        SerialNumber = serialNumber.Trim();

        // .Date removes the time part, so we only store the day (e.g. 2024-02-11 00:00).
        PurchaseDate = purchaseDate.Date;
        PurchasePriceUsd = purchasePriceUsd;
        OfficeId = officeId;

        // "??" is the null-coalescing operator: "use the left value, or if it is
        // null, use the right value instead". If no warranty date was given, we
        // assume the default of 2 years after the purchase.
        WarrantyExpirationDate = (warrantyExpirationDate ?? purchaseDate.AddYears(DefaultWarrantyYears)).Date;
        EmployeeId = employeeId;
    }

    // ---- Properties ----
    // "{ get; set; }" creates an "auto-property": a value you can read (get) and change (set).
    // EF Core treats a property called "Id" as the primary key: a unique number
    // for each row. The database generates it automatically (1, 2, 3, ...).
    public int Id { get; set; }

    // "= string.Empty" gives the property a starting value so it is never null.
    public string Brand { get; set; } = string.Empty;
    public string ModelName { get; set; } = string.Empty;
    public string SerialNumber { get; set; } = string.Empty;
    public DateTime PurchaseDate { get; set; }

    /// <summary>Purchase price in USD. Local prices are always calculated from this value.</summary>
    // "decimal" is the right type for money. It stores exact decimal numbers,
    // while "double" can give small rounding errors (0.1 + 0.2 = 0.30000000000000004).
    public decimal PurchasePriceUsd { get; set; }

    public DateTime WarrantyExpirationDate { get; set; }

    // ---- Relationship to Office ----
    // OfficeId is the "foreign key": the number of the office this asset belongs to.
    // Office is a "navigation property": EF Core can fill it with the whole Office
    // object so we can write asset.Office.Name. "null!" tells the compiler
    // "trust me, EF Core will fill this in", so it does not warn us about null.
    public int OfficeId { get; set; }
    public Office Office { get; set; } = null!;

    /// <summary>Optional employee the asset is assigned to.</summary>
    // "int?" means the asset may not be assigned to anyone (the value is null).
    public int? EmployeeId { get; set; }
    public Employee? Employee { get; set; }

    // ---- Maintenance tracking (Level 5) ----
    public DateTime? LastMaintenanceDate { get; set; }
    public DateTime? NextMaintenanceDate { get; set; }
    public string? MaintenanceNotes { get; set; }

    // ---- Polymorphic, non-persisted members ----
    // "abstract" members have no code here. Every class that inherits from Asset
    // MUST provide its own version. For example, Laptop says Kind => AssetKind.Laptop.
    // This is "polymorphism": the same property gives different answers
    // depending on which kind of object it is.
    // These members have no setter, so they are not stored in the database.
    public abstract AssetKind Kind { get; }
    public abstract AssetCategory Category { get; }

    // "virtual" means a child class MAY replace (override) this, but doesn't have to.
    public virtual string TypeName => Kind.ToDisplayName();

    // "$" before a string allows {placeholders} that are replaced with values.
    // Example: Brand "Lenovo" and ModelName "ThinkPad X1" give "Lenovo ThinkPad X1".
    public string DisplayName => $"{Brand} {ModelName}";

    /// <summary>Stores the details of a maintenance visit on this asset.</summary>
    // A "method" is a block of code that does something. "void" means it returns nothing.
    public void RecordMaintenance(DateTime performedOn, DateTime? nextMaintenance, string? notes)
    {
        LastMaintenanceDate = performedOn.Date;

        // "?." is the null-conditional operator: if nextMaintenance is null the
        // result is null, otherwise it takes .Date of the value.
        NextMaintenanceDate = nextMaintenance?.Date;

        // "condition ? a : b" is the ternary operator: if the condition is true use a, otherwise b.
        // Empty notes are stored as null (no notes) instead of an empty string.
        MaintenanceNotes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim();
    }

    // "override" replaces the built-in ToString() that every C# object has.
    // It is used for logging, e.g. "#1 Laptop Lenovo ThinkPad X1 (LEN-X1-0001)".
    public override string ToString() => $"#{Id} {TypeName} {DisplayName} ({SerialNumber})";
}
