using SmartAssetTracking.Models;

namespace SmartAssetTracking.Services;

/// <summary>Holds the currently logged-in user and performs role-based authorization checks.</summary>
public class UserSession
{
    public User? CurrentUser { get; private set; }

    public bool IsAuthenticated => CurrentUser is not null;

    public void SignIn(User user) => CurrentUser = user;

    public void SignOut() => CurrentUser = null;

    public bool HasAtLeast(UserRole role) => CurrentUser?.HasAtLeast(role) == true;

    /// <summary>Throws <see cref="AccessDeniedException"/> unless the current user has at least <paramref name="role"/>.</summary>
    public void Require(UserRole role)
    {
        if (CurrentUser is null)
            throw new AccessDeniedException("You must be logged in.");
        if (!CurrentUser.HasAtLeast(role))
            throw new AccessDeniedException($"This action requires the {role} role (you are {CurrentUser.Role}).");
    }
}
