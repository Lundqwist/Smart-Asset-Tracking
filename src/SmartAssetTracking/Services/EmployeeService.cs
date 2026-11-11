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
        session.Require(UserRole.Admin);
        if (!await employees.DeleteAsync(id, ct))
            throw new NotFoundException($"No employee with ID {id} exists.");
        logger.LogInformation("User {User} deleted employee {EmployeeId}", session.CurrentUser?.Username, id);
    }

    public static bool IsValidEmail(string? email)
    {
        if (string.IsNullOrWhiteSpace(email))
            return false;
        try
        {
            var address = new MailAddress(email.Trim());
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
