// ============================================================================
// UserSession.cs
// ----------------------------------------------------------------------------
// Remembers WHO is logged in right now. There is only one UserSession object
// for the whole program (it is registered as a "singleton" in
// ServiceRegistration.cs), so every service sees the same logged-in user.
//
// It also does the role checks for "role-based authorization": services call
// session.Require(UserRole.Manager) before doing something only managers
// may do.
// ============================================================================

using SmartAssetTracking.Models;

namespace SmartAssetTracking.Services;

/// <summary>Holds the currently logged-in user and performs role-based authorization checks.</summary>
public class UserSession
{
    // "private set" means anyone can READ CurrentUser, but only this class can CHANGE it.
    public User? CurrentUser { get; private set; }

    // true when someone is logged in.
    public bool IsAuthenticated => CurrentUser is not null;

    public void SignIn(User user) => CurrentUser = user;

    public void SignOut() => CurrentUser = null;

    // "?." returns null if nobody is logged in. "== true" turns that null into false.
    public bool HasAtLeast(UserRole role) => CurrentUser?.HasAtLeast(role) == true;

    /// <summary>Throws <see cref="AccessDeniedException"/> unless the current user has at least <paramref name="role"/>.</summary>
    // Services call this at the top of protected operations. If the user isn't
    // allowed, the operation stops right away with an error message.
    public void Require(UserRole role)
    {
        if (CurrentUser is null)
            throw new AccessDeniedException("You must be logged in.");
        if (!CurrentUser.HasAtLeast(role))
            throw new AccessDeniedException($"This action requires the {role} role (you are {CurrentUser.Role}).");
    }
}
