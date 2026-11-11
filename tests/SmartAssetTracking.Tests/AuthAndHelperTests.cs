// ============================================================================
// AuthAndHelperTests.cs - tests for login, passwords, roles and input parsing
// ----------------------------------------------------------------------------
//   * passwords are hashed and salted (the same password gives different hashes)
//   * the admin / 1234 login from the assignment works, and wrong passwords fail
//   * only Admins may manage users
//   * email validation, and the number/date parsing used by the input prompts
// ============================================================================

using SmartAssetTracking.Helpers;
using SmartAssetTracking.Models;
using SmartAssetTracking.Services;

namespace SmartAssetTracking.Tests;

public class AuthAndHelperTests
{
    [Fact]
    public void Password_hash_verifies_and_is_salted()
    {
        var a = PasswordHasher.Hash("1234");
        var b = PasswordHasher.Hash("1234");

        Assert.NotEqual(a, b);
        Assert.True(PasswordHasher.Verify("1234", a));
        Assert.False(PasswordHasher.Verify("4321", a));
        Assert.False(PasswordHasher.Verify("1234", "garbage"));
    }

    [Fact]
    public async Task Seeded_admin_can_log_in_with_spec_credentials()
    {
        await using var host = await TestHost.CreateAsync(signedInAs: null, seed: true);
        var auth = host.Get<IAuthService>();
        var session = host.Get<UserSession>();

        Assert.False(await auth.LoginAsync("admin", "wrong"));
        Assert.True(await auth.LoginAsync("admin", "1234"));
        Assert.Equal(UserRole.Admin, session.CurrentUser!.Role);

        auth.Logout();
        Assert.False(session.IsAuthenticated);
    }

    [Fact]
    public async Task Only_admins_manage_users()
    {
        await using var host = await TestHost.CreateAsync(UserRole.Manager, seed: true);

        await Assert.ThrowsAsync<AccessDeniedException>(() =>
            host.Get<IAuthService>().CreateUserAsync("newuser", "secret", UserRole.Employee, null));
    }

    [Fact]
    public async Task Employee_email_is_validated()
    {
        await using var host = await TestHost.CreateAsync();
        var sut = host.Get<IEmployeeService>();

        await Assert.ThrowsAsync<ValidationException>(() => sut.CreateAsync("Jane Doe", "IT", "not-an-email"));
        var created = await sut.CreateAsync("Jane Doe", "IT", "jane.doe@example.com");
        Assert.True(created.Id > 0);
        await Assert.ThrowsAsync<ValidationException>(() => sut.CreateAsync("Jane Two", "IT", "JANE.DOE@example.com"));
    }

    [Theory]
    [InlineData("1299.99", 1299.99)]
    [InlineData("1299,99", 1299.99)]
    [InlineData("1,299.99", 1299.99)]
    [InlineData("1,299", 1299)]
    [InlineData("$ 15", 15)]
    public void Decimal_input_accepts_common_formats(string input, double expected)
    {
        Assert.True(ConsoleInput.TryParseDecimal(input, out var value));
        Assert.Equal((decimal)expected, value);
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("")]
    public void Decimal_input_rejects_garbage(string input) => Assert.False(ConsoleInput.TryParseDecimal(input, out _));

    [Theory]
    [InlineData("2024-02-11", true)]
    [InlineData("2024-2-1", true)]
    [InlineData("20240211", true)]
    [InlineData("2024-13-01", false)]
    [InlineData("11/02/2024", false)]
    public void Date_input_is_strict(string input, bool valid) => Assert.Equal(valid, ConsoleInput.TryParseDate(input, out _));
}
