namespace SmartAssetTracking.Models;

// ------------------------------------------------------------------
// Computer assets
// ------------------------------------------------------------------

/// <summary>Base class for all computers (laptops and desktops).</summary>
public abstract class ComputerAsset : Asset
{
    protected ComputerAsset() { }

    protected ComputerAsset(string brand, string modelName, string serialNumber, DateTime purchaseDate,
        decimal purchasePriceUsd, int officeId, DateTime? warrantyExpirationDate = null, int? employeeId = null)
        : base(brand, modelName, serialNumber, purchaseDate, purchasePriceUsd, officeId, warrantyExpirationDate, employeeId) { }

    public override AssetCategory Category => AssetCategory.Computer;
}

public class Laptop : ComputerAsset
{
    protected Laptop() { }

    public Laptop(string brand, string modelName, string serialNumber, DateTime purchaseDate,
        decimal purchasePriceUsd, int officeId, DateTime? warrantyExpirationDate = null, int? employeeId = null)
        : base(brand, modelName, serialNumber, purchaseDate, purchasePriceUsd, officeId, warrantyExpirationDate, employeeId) { }

    public override AssetKind Kind => AssetKind.Laptop;
}

public class Desktop : ComputerAsset
{
    protected Desktop() { }

    public Desktop(string brand, string modelName, string serialNumber, DateTime purchaseDate,
        decimal purchasePriceUsd, int officeId, DateTime? warrantyExpirationDate = null, int? employeeId = null)
        : base(brand, modelName, serialNumber, purchaseDate, purchasePriceUsd, officeId, warrantyExpirationDate, employeeId) { }

    public override AssetKind Kind => AssetKind.Desktop;
}

// ------------------------------------------------------------------
// Mobile assets
// ------------------------------------------------------------------

/// <summary>Base class for all mobile devices (phones and tablets).</summary>
public abstract class MobileAsset : Asset
{
    protected MobileAsset() { }

    protected MobileAsset(string brand, string modelName, string serialNumber, DateTime purchaseDate,
        decimal purchasePriceUsd, int officeId, DateTime? warrantyExpirationDate = null, int? employeeId = null)
        : base(brand, modelName, serialNumber, purchaseDate, purchasePriceUsd, officeId, warrantyExpirationDate, employeeId) { }

    public override AssetCategory Category => AssetCategory.Mobile;
}

/// <summary>A mobile phone, e.g. Apple iPhone, Samsung Galaxy or Nokia.</summary>
public class MobilePhone : MobileAsset
{
    /// <summary>Phone brands suggested in the UI. Any other brand is also accepted.</summary>
    public static readonly IReadOnlyList<string> KnownBrands = ["Apple", "Samsung", "Nokia"];

    protected MobilePhone() { }

    public MobilePhone(string brand, string modelName, string serialNumber, DateTime purchaseDate,
        decimal purchasePriceUsd, int officeId, DateTime? warrantyExpirationDate = null, int? employeeId = null)
        : base(brand, modelName, serialNumber, purchaseDate, purchasePriceUsd, officeId, warrantyExpirationDate, employeeId) { }

    public override AssetKind Kind => AssetKind.MobilePhone;
}

public class Tablet : MobileAsset
{
    protected Tablet() { }

    public Tablet(string brand, string modelName, string serialNumber, DateTime purchaseDate,
        decimal purchasePriceUsd, int officeId, DateTime? warrantyExpirationDate = null, int? employeeId = null)
        : base(brand, modelName, serialNumber, purchaseDate, purchasePriceUsd, officeId, warrantyExpirationDate, employeeId) { }

    public override AssetKind Kind => AssetKind.Tablet;
}

// ------------------------------------------------------------------
// Other office equipment
// ------------------------------------------------------------------

/// <summary>Printers, projectors, monitors and other office equipment.</summary>
public class OfficeEquipment : Asset
{
    protected OfficeEquipment() { }

    public OfficeEquipment(string brand, string modelName, string serialNumber, DateTime purchaseDate,
        decimal purchasePriceUsd, int officeId, DateTime? warrantyExpirationDate = null, int? employeeId = null)
        : base(brand, modelName, serialNumber, purchaseDate, purchasePriceUsd, officeId, warrantyExpirationDate, employeeId) { }

    public override AssetKind Kind => AssetKind.OfficeEquipment;
    public override AssetCategory Category => AssetCategory.OfficeEquipment;
}

/// <summary>Creates the correct concrete <see cref="Asset"/> subclass for a given <see cref="AssetKind"/>.</summary>
public static class AssetFactory
{
    public static Asset Create(AssetKind kind, string brand, string modelName, string serialNumber,
        DateTime purchaseDate, decimal purchasePriceUsd, int officeId,
        DateTime? warrantyExpirationDate = null, int? employeeId = null) => kind switch
    {
        AssetKind.Laptop => new Laptop(brand, modelName, serialNumber, purchaseDate, purchasePriceUsd, officeId, warrantyExpirationDate, employeeId),
        AssetKind.Desktop => new Desktop(brand, modelName, serialNumber, purchaseDate, purchasePriceUsd, officeId, warrantyExpirationDate, employeeId),
        AssetKind.MobilePhone => new MobilePhone(brand, modelName, serialNumber, purchaseDate, purchasePriceUsd, officeId, warrantyExpirationDate, employeeId),
        AssetKind.Tablet => new Tablet(brand, modelName, serialNumber, purchaseDate, purchasePriceUsd, officeId, warrantyExpirationDate, employeeId),
        AssetKind.OfficeEquipment => new OfficeEquipment(brand, modelName, serialNumber, purchaseDate, purchasePriceUsd, officeId, warrantyExpirationDate, employeeId),
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown asset kind")
    };
}
