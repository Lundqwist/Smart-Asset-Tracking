// ============================================================================
// EmployeeService.cs
// ----------------------------------------------------------------------------
// Business rules for employees (Level 5). Managers may add and edit
// employees, but only Admins may delete them. Email addresses must be valid
// and unique. See AssetService.cs for a longer explanation of services.
// ============================================================================

using System.Net.Mail;
using Microsoft.Extensions.Logging;
using SmartAssetTracking.Models;
using SmartAssetTracking.Repositories;

namespace SmartAssetTracking.Services;

public interface IEmployeeService
{
    Task<IReadOnlyList<Employee>> GetAllAsync(CancellationToken ct = default);
    Task<Employee> GetByIdAsync(int id, CancellationToken ct = default);
    Task<Employee> CreateAsync(string fullName, string department, string email, CancellationToken ct = default);
    Task UpdateAsync(int id, string fullName, string department, string email, CancellationToken ct = default);
    Task DeleteAsync(int id, CancellationToken ct = default);
}

public class EmployeeService(
    IEmployeeRepository employees,
    UserSession session,
    ILogger<EmployeeService> logger) : IEmployeeService
{
    public async Task<IReadOnlyList<Employee>> GetAllAsync(CancellationToken ct = default) => await employees.GetAllAsync(ct);

    public async Task<Employee> GetByIdAsync(int id, CancellationToken ct = default) =>
        await employees.GetByIdAsync(id, ct) ?? throw new NotFoundException($"No employee with ID {id} exists.");

    public async Task<Employee> CreateAsync(string fullName, string department, string email, CancellationToken ct = default)
    {
        session.Require(UserRole.Manager);
        await ValidateAsync(fullName, department, email, null, ct);

        var employee = new Employee { FullName = fullName.Trim(), Department = department.Trim(), Email = email.Trim() };
        await employees.AddAsync(employee, ct);
        logger.LogInformation("User {User} created employee {Employee}", session.CurrentUser?.Username, employee);
        return employee;
    }

    public async Task UpdateAsync(int id, string fullName, string department, string email, CancellationToken ct = default)
    {
        session.Require(UserRole.Manager);
        var employee = await GetByIdAsync(id, ct);
        await ValidateAsync(fullName, department, email, id, ct);

        employee.FullName = fullName.Trim();
        employee.Department = department.Trim();
        employee.Email = email.Trim();
        await employees.UpdateAsync(employee, ct);
        logger.LogInformation("User {User} updated employee {EmployeeId}", session.CurrentUser?.Username, id);
    }

    public async Task DeleteAsync(int id, CancellationToken ct = default)
    {
        session.Require(UserRole.Admin); // deleting is more dangerous, so only Admins may do it
        if (!await employees.DeleteAsync(id, ct))
            throw new NotFoundException($"No employee with ID {id} exists.");
        logger.LogInformation("User {User} deleted employee {EmployeeId}", session.CurrentUser?.Username, id);
    }

    // Checks that an email address looks valid, e.g. "anna@company.com".
    // "public static" so the menu can also use it to check input while typing.
    public static bool IsValidEmail(string? email)
    {
        if (string.IsNullOrWhiteSpace(email))
            return false;

        // MailAddress is .NET's built-in email parser. It throws a FormatException
        // if the text is not an email address, so we catch that and return false.
        try
        {
            var address = new MailAddress(email.Trim());
            // Also require a dot in the domain part ("company.com", not just "company").
            return address.Address == email.Trim() && address.Host.Contains('.');
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private async Task ValidateAsync(string fullName, string department, string email, int? existingId, CancellationToken ct)
    {
        var errors = new ValidationErrors()
            .RequireText(fullName, "Full name", 100)
            .RequireText(department, "Department", 60)
            .RequireText(email, "Email", 150)
            .Require(string.IsNullOrWhiteSpace(email) || IsValidEmail(email), $"'{email}' is not a valid email address.");

        if (IsValidEmail(email) && await employees.EmailExistsAsync(email, existingId, ct))
            errors.Add($"The email '{email.Trim()}' is already used by another employee.");

        errors.ThrowIfAny();
    }
}
