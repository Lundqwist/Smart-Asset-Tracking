namespace SmartAssetTracking.Models;

/// <summary>
/// Base class for every physical asset the company owns.
/// Stored in a single table (TPH) with a discriminator column identifying the concrete type.
/// </summary>
public abstract class Asset
{
    /// <summary>Warranty length used when no explicit warranty date is supplied.</summary>
    public const int DefaultWarrantyYears = 2;

    /// <summary>Parameterless constructor required by Entity Framework Core.</summary>
    protected Asset()
    {
    }

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
        Brand = brand.Trim();
        ModelName = modelName.Trim();
        SerialNumber = serialNumber.Trim();
        PurchaseDate = purchaseDate.Date;
        PurchasePriceUsd = purchasePriceUsd;
        OfficeId = officeId;
        WarrantyExpirationDate = (warrantyExpirationDate ?? purchaseDate.AddYears(DefaultWarrantyYears)).Date;
        EmployeeId = employeeId;
    }

    public int Id { get; set; }
    public string Brand { get; set; } = string.Empty;
    public string ModelName { get; set; } = string.Empty;
    public string SerialNumber { get; set; } = string.Empty;
    public DateTime PurchaseDate { get; set; }

    /// <summary>Purchase price in USD. Local prices are always calculated from this value.</summary>
    public decimal PurchasePriceUsd { get; set; }

    public DateTime WarrantyExpirationDate { get; set; }

    public int OfficeId { get; set; }
    public Office Office { get; set; } = null!;

    /// <summary>Optional employee the asset is assigned to.</summary>
    public int? EmployeeId { get; set; }
    public Employee? Employee { get; set; }

    // ---- Maintenance tracking ----
    public DateTime? LastMaintenanceDate { get; set; }
    public DateTime? NextMaintenanceDate { get; set; }
    public string? MaintenanceNotes { get; set; }

    // ---- Polymorphic, non-persisted members ----
    public abstract AssetKind Kind { get; }
    public abstract AssetCategory Category { get; }
    public virtual string TypeName => Kind.ToDisplayName();
    public string DisplayName => $"{Brand} {ModelName}";

    public void RecordMaintenance(DateTime performedOn, DateTime? nextMaintenance, string? notes)
    {
        LastMaintenanceDate = performedOn.Date;
        NextMaintenanceDate = nextMaintenance?.Date;
        MaintenanceNotes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim();
    }

    public override string ToString() => $"#{Id} {TypeName} {DisplayName} ({SerialNumber})";
}
