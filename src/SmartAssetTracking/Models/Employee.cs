namespace SmartAssetTracking.Models;

/// <summary>An employee that assets can be assigned to.</summary>
public class Employee
{
    public int Id { get; set; }
    public string FullName { get; set; } = string.Empty;
    public string Department { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;

    public List<Asset> AssignedAssets { get; set; } = [];

    public override string ToString() => $"{FullName} ({Department})";
}
