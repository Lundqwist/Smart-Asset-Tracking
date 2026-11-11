// ============================================================================
// Enums.cs
// ----------------------------------------------------------------------------
// An "enum" (enumeration) is a type that can only hold one of a fixed list of
// named values. Using enums instead of plain strings or numbers means the
// compiler stops us from typing mistakes like "Lapptop", and the code becomes
// easier to read: AssetKind.Laptop is clearer than the number 0.
// ============================================================================

// A "namespace" groups related classes together, like a folder for code.
// Everything in the Models folder lives in the SmartAssetTracking.Models namespace.
namespace SmartAssetTracking.Models;

/// <summary>High-level grouping used for sorting and filtering (computers first, then mobiles, then other equipment).</summary>
// The "= 1", "= 2", "= 3" give each value a number. Enums are sorted by these
// numbers, so when we sort assets by category, computers come first.
public enum AssetCategory
{
    Computer = 1,
    Mobile = 2,
    OfficeEquipment = 3
}

/// <summary>The concrete kind of asset. Each value maps to one class in the inheritance hierarchy.</summary>
// When no numbers are given, C# numbers them automatically: Laptop = 0, Desktop = 1, and so on.
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
    Normal,   // more than 6 months of life left
    Red,      // less than 6 months left
    Yellow,   // less than 3 months left
    Expired   // the 3-year lifetime is over
}

/// <summary>System roles. Higher values include the permissions of lower values.</summary>
// Because Admin (3) > Manager (2) > Employee (1), we can check permissions with a
// simple comparison: "is the user's role >= Manager?" (see User.HasAtLeast).
public enum UserRole
{
    Employee = 1,
    Manager = 2,
    Admin = 3
}

// ----------------------------------------------------------------------------
// "Extension methods" let us add new methods to an existing type without
// changing it. Because the first parameter is marked with "this", we can write
//     AssetKind.MobilePhone.ToDisplayName()
// as if ToDisplayName were built into the enum. Extension methods must live in
// a "static class" (a class you never create objects from).
// ----------------------------------------------------------------------------
public static class EnumDisplayExtensions
{
    // "=>" followed by an expression is a short way to write a method that only
    // returns one value (called an "expression-bodied member").
    // A "switch expression" picks a result depending on the value of "kind".
    // The "_" at the end is the fallback for any other value.
    public static string ToDisplayName(this AssetKind kind) => kind switch
    {
        AssetKind.Laptop => "Laptop",
        AssetKind.Desktop => "Desktop",
        AssetKind.MobilePhone => "Mobile Phone",
        AssetKind.Tablet => "Tablet",
        AssetKind.OfficeEquipment => "Office Equipment",
        _ => kind.ToString()
    };

    // The same method name with a different parameter type is called "overloading".
    // C# picks the right version based on the type you call it on.
    public static string ToDisplayName(this AssetCategory category) => category switch
    {
        AssetCategory.Computer => "Computers",
        AssetCategory.Mobile => "Mobile Devices",
        AssetCategory.OfficeEquipment => "Office Equipment",
        _ => category.ToString()
    };

    // ToString() gives the name, e.g. "Yellow". ToUpperInvariant() makes it "YELLOW".
    // "Invariant" means it ignores the computer's language settings, so the
    // result is always the same everywhere.
    public static string ToDisplayName(this LifecycleStatus status) => status.ToString().ToUpperInvariant();
}
