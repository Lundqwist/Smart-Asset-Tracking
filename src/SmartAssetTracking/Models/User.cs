namespace SmartAssetTracking.Models;

/// <summary>A login account. Passwords are stored as salted PBKDF2 hashes, never in plain text.</summary>
public class User
{
    public int Id { get; set; }
    public string Username { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public UserRole Role { get; set; } = UserRole.Employee;

    /// <summary>Optional link to an employee record (used for "My assets").</summary>
    public int? EmployeeId { get; set; }
    public Employee? Employee { get; set; }

    public bool HasAtLeast(UserRole role) => Role >= role;
}
