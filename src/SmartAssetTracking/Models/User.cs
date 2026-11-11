// ============================================================================
// User.cs
// ----------------------------------------------------------------------------
// A login account (Level 5: user roles and authentication). A User is the
// account you log in with. An Employee is a person who can have assets
// assigned. A user can optionally be linked to an employee, so the
// "My assets" menu knows whose assets to show.
// ============================================================================

namespace SmartAssetTracking.Models;

/// <summary>A login account. Passwords are stored as salted PBKDF2 hashes, never in plain text.</summary>
public class User
{
    public int Id { get; set; }
    public string Username { get; set; } = string.Empty;

    // We NEVER store the real password. We only store a "hash": a scrambled
    // fingerprint of the password that can't be turned back into the password.
    // When someone logs in, we hash what they typed and compare the two
    // fingerprints. See Helpers/PasswordHasher.cs.
    public string PasswordHash { get; set; } = string.Empty;

    public UserRole Role { get; set; } = UserRole.Employee;

    /// <summary>Optional link to an employee record (used for "My assets").</summary>
    public int? EmployeeId { get; set; }
    public Employee? Employee { get; set; }

    // Returns true if this user's role is the given role or higher.
    // Example: an Admin "has at least" Manager, but an Employee does not.
    // This works because the enum values are numbers (Admin = 3, Manager = 2, Employee = 1).
    public bool HasAtLeast(UserRole role) => Role >= role;
}
