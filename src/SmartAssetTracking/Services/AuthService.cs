using Microsoft.Extensions.Logging;
using SmartAssetTracking.Helpers;
using SmartAssetTracking.Models;
using SmartAssetTracking.Repositories;

namespace SmartAssetTracking.Services;

public interface IAuthService
{
    Task<bool> LoginAsync(string username, string password, CancellationToken ct = default);
    void Logout();
    Task<IReadOnlyList<User>> GetUsersAsync(CancellationToken ct = default);
    Task<User> CreateUserAsync(string username, string password, UserRole role, int? employeeId, CancellationToken ct = default);
    Task ChangeRoleAsync(int userId, UserRole role, CancellationToken ct = default);
    Task ResetPasswordAsync(int userId, string newPassword, CancellationToken ct = default);
    Task ChangeOwnPasswordAsync(string currentPassword, string newPassword, CancellationToken ct = default);
    Task DeleteUserAsync(int userId, CancellationToken ct = default);
}

/// <summary>Simulated authentication and user management (Admin only, except changing your own password).</summary>
public class AuthService(
    IUserRepository users,
    IEmployeeRepository employees,
    UserSession session,
    ILogger<AuthService> logger) : IAuthService
{
    public const int MinPasswordLength = 4;

    public async Task<bool> LoginAsync(string username, string password, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrEmpty(password))
            return false;

        var user = await users.GetByUsernameAsync(username, ct);
        if (user is null || !PasswordHasher.Verify(password, user.PasswordHash))
        {
            logger.LogWarning("Failed login attempt for {Username}", username);
            return false;
        }

        session.SignIn(user);
        logger.LogInformation("User {Username} ({Role}) logged in", user.Username, user.Role);
        return true;
    }

    public void Logout()
    {
        if (session.CurrentUser is not null)
            logger.LogInformation("User {Username} logged out", session.CurrentUser.Username);
        session.SignOut();
    }

    public async Task<IReadOnlyList<User>> GetUsersAsync(CancellationToken ct = default)
    {
        session.Require(UserRole.Admin);
        return await users.GetAllAsync(ct);
    }

    public async Task<User> CreateUserAsync(string username, string password, UserRole role, int? employeeId, CancellationToken ct = default)
    {
        session.Require(UserRole.Admin);

        var errors = new ValidationErrors()
            .RequireText(username, "Username", 50)
            .Require(string.IsNullOrWhiteSpace(username) || username.Trim().All(c => char.IsLetterOrDigit(c) || c is '.' or '_' or '-'),
                "Username may only contain letters, digits, '.', '_' and '-'.")
            .Require(password?.Length >= MinPasswordLength, $"Password must be at least {MinPasswordLength} characters.")
            .Require(Enum.IsDefined(role), "Unknown role.");

        if (!string.IsNullOrWhiteSpace(username) && await users.GetByUsernameAsync(username, ct) is not null)
            errors.Add($"Username '{username.Trim()}' is already taken.");

        if (employeeId is not null && await employees.GetByIdAsync(employeeId.Value, ct) is null)
            errors.Add($"Employee {employeeId} does not exist.");

        errors.ThrowIfAny();

        var user = new User
        {
            Username = username.Trim(),
            PasswordHash = PasswordHasher.Hash(password!),
            Role = role,
            EmployeeId = employeeId
        };
        await users.AddAsync(user, ct);
        logger.LogInformation("Admin {Admin} created user {Username} ({Role})", session.CurrentUser?.Username, user.Username, role);
        return user;
    }

    public async Task ChangeRoleAsync(int userId, UserRole role, CancellationToken ct = default)
    {
        session.Require(UserRole.Admin);
        var user = await GetUserAsync(userId, ct);
        if (user.Id == session.CurrentUser!.Id && role != UserRole.Admin)
            throw new ValidationException("You cannot remove your own Admin role.");

        user.Role = role;
        await users.UpdateAsync(user, ct);
        logger.LogInformation("Admin {Admin} changed role of {Username} to {Role}", session.CurrentUser.Username, user.Username, role);
    }

    public async Task ResetPasswordAsync(int userId, string newPassword, CancellationToken ct = default)
    {
        session.Require(UserRole.Admin);
        ValidatePassword(newPassword);
        var user = await GetUserAsync(userId, ct);
        user.PasswordHash = PasswordHasher.Hash(newPassword);
        await users.UpdateAsync(user, ct);
        logger.LogInformation("Admin {Admin} reset password for {Username}", session.CurrentUser?.Username, user.Username);
    }

    public async Task ChangeOwnPasswordAsync(string currentPassword, string newPassword, CancellationToken ct = default)
    {
        session.Require(UserRole.Employee);
        var user = await GetUserAsync(session.CurrentUser!.Id, ct);
        if (!PasswordHasher.Verify(currentPassword, user.PasswordHash))
            throw new ValidationException("The current password is incorrect.");
        ValidatePassword(newPassword);

        user.PasswordHash = PasswordHasher.Hash(newPassword);
        await users.UpdateAsync(user, ct);
        session.SignIn(user);
        logger.LogInformation("User {Username} changed their password", user.Username);
    }

    public async Task DeleteUserAsync(int userId, CancellationToken ct = default)
    {
        session.Require(UserRole.Admin);
        if (userId == session.CurrentUser!.Id)
            throw new ValidationException("You cannot delete your own account while logged in.");
        if (!await users.DeleteAsync(userId, ct))
            throw new NotFoundException($"No user with ID {userId} exists.");
        logger.LogInformation("Admin {Admin} deleted user {UserId}", session.CurrentUser.Username, userId);
    }

    private async Task<User> GetUserAsync(int id, CancellationToken ct) =>
        await users.GetByIdAsync(id, ct) ?? throw new NotFoundException($"No user with ID {id} exists.");

    private static void ValidatePassword(string? password)
    {
        if (password is null || password.Length < MinPasswordLength)
            throw new ValidationException($"Password must be at least {MinPasswordLength} characters.");
    }
}
