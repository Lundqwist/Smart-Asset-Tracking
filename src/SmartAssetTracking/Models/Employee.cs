// ============================================================================
// Employee.cs
// ----------------------------------------------------------------------------
// An employee that assets can be assigned to (Level 5). The PDF asks for:
// Employee Id, Full Name, Department, Email and Assigned Assets.
// EF Core creates the "Employees" table from this class.
// ============================================================================

namespace SmartAssetTracking.Models;

/// <summary>An employee that assets can be assigned to.</summary>
public class Employee
{
    public int Id { get; set; }
    public string FullName { get; set; } = string.Empty;
    public string Department { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;

    // All assets whose EmployeeId points at this employee.
    // EF Core fills this list when we ask it to (with .Include(e => e.AssignedAssets)).
    public List<Asset> AssignedAssets { get; set; } = [];

    // e.g. "Sarah Johnson (IT Support)"
    public override string ToString() => $"{FullName} ({Department})";
}
