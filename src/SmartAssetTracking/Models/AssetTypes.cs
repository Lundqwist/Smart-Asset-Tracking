// ============================================================================
// AssetTypes.cs
// ----------------------------------------------------------------------------
// The concrete asset classes. The inheritance tree looks like this:
//
//                 Asset (abstract)
//          ┌────────┼──────────────┐
//   ComputerAsset  MobileAsset   OfficeEquipment
//    ┌────┴────┐   ┌────┴─────┐
//  Laptop  Desktop MobilePhone Tablet
//
// "ComputerAsset : Asset" means "ComputerAsset inherits from Asset".
// Each level only adds what is new. ComputerAsset decides the category
// (Computer), and Laptop decides the kind (Laptop). Everything else, such as
// Brand, Price and Office, comes from Asset.
//
// The constructors look repetitive, but each one passes its values up to the
// parent class with ": base(...)". In C#, constructors are not inherited
// automatically, so every class must declare the ones it wants to offer.
// ============================================================================

namespace SmartAssetTracking.Models;

// ------------------------------------------------------------------
// Computer assets
// ------------------------------------------------------------------

/// <summary>Base class for all computers (laptops and desktops).</summary>
public abstract class ComputerAsset : Asset
{
    // Empty constructor for EF Core. "{ }" on one line means it does nothing extra.
    protected ComputerAsset() { }

    // ": base(...)" calls the constructor of the parent class (Asset) with the same values.
    protected ComputerAsset(string brand, string modelName, string serialNumber, DateTime purchaseDate,
        decimal purchasePriceUsd, int officeId, DateTime? warrantyExpirationDate = null, int? employeeId = null)
        : base(brand, modelName, serialNumber, purchaseDate, purchasePriceUsd, officeId, warrantyExpirationDate, employeeId) { }

    // "override" gives the abstract Category from Asset a real value.
    // Every computer (laptop or desktop) belongs to the Computer category.
    public override AssetCategory Category => AssetCategory.Computer;
}

// Laptop is NOT abstract, so we can create real Laptop objects with "new Laptop(...)".
public class Laptop : ComputerAsset
{
    protected Laptop() { }

    // This constructor is "public", so any code may use it to create a laptop.
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
// The assignment lists iPhone, Samsung and Nokia. Instead of one class per brand
// (which would be identical), we use one MobilePhone class and store the brand
// in the Brand property. KnownBrands is the list we suggest in the menu.
public class MobilePhone : MobileAsset
{
    /// <summary>Phone brands suggested in the UI. Any other brand is also accepted.</summary>
    // "static" means the list belongs to the class itself, not to each phone object.
    // There is only one copy, shared by everyone: MobilePhone.KnownBrands.
    // "readonly" means the variable can't be pointed at a different list later.
    // IReadOnlyList means nobody can add to or remove from the list.
    // The [ ... ] syntax is a "collection expression" that creates the list.
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
// OfficeEquipment inherits directly from Asset, so it must provide BOTH Kind and Category itself.
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
// This is the "Factory" design pattern: one place that knows how to create the
// right kind of object. The menu asks the user for an AssetKind (e.g. Tablet),
// and the factory turns that choice into "new Tablet(...)". Without it, every
// place that creates assets would need its own big switch statement.
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

        // "throw" stops the program flow with an error (an "exception").
        // This should never happen, but it protects us if a new AssetKind is added and forgotten here.
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown asset kind")
    };
}
