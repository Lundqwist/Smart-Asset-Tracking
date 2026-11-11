namespace SmartAssetTracking.Models;

/// <summary>High-level grouping used for sorting and filtering (computers first, then mobiles, then other equipment).</summary>
public enum AssetCategory
{
    Computer = 1,
    Mobile = 2,
    OfficeEquipment = 3
}

/// <summary>The concrete kind of asset. Each value maps to one class in the inheritance hierarchy.</summary>
public enum AssetKind
{
    Laptop,
    Desktop,
    MobilePhone,
    Tablet,
    OfficeEquipment
}

/// <summary>Lifecycle status derived from how much of the asset's lifetime remains.</summary>
public enum LifecycleStatus
{
    Normal,
    Red,
    Yellow,
    Expired
}

/// <summary>System roles. Higher values include the permissions of lower values.</summary>
public enum UserRole
{
    Employee = 1,
    Manager = 2,
    Admin = 3
}

public static class EnumDisplayExtensions
{
    public static string ToDisplayName(this AssetKind kind) => kind switch
    {
        AssetKind.Laptop => "Laptop",
        AssetKind.Desktop => "Desktop",
        AssetKind.MobilePhone => "Mobile Phone",
        AssetKind.Tablet => "Tablet",
        AssetKind.OfficeEquipment => "Office Equipment",
        _ => kind.ToString()
    };

    public static string ToDisplayName(this AssetCategory category) => category switch
    {
        AssetCategory.Computer => "Computers",
        AssetCategory.Mobile => "Mobile Devices",
        AssetCategory.OfficeEquipment => "Office Equipment",
        _ => category.ToString()
    };

    public static string ToDisplayName(this LifecycleStatus status) => status.ToString().ToUpperInvariant();
}
