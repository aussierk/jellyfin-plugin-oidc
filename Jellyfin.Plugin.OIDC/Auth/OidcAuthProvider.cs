using System;
using System.Threading.Tasks;
using Jellyfin.Database.Implementations.Entities;
using MediaBrowser.Controller.Authentication;

namespace Jellyfin.Plugin.OIDC.Auth;

/// <summary>
/// Registered as the authentication provider for OIDC-managed users.
/// Actual authentication happens via the OIDC flow in the controller;
/// this provider blocks direct username/password login for SSO users.
/// </summary>
public class OidcAuthProvider : IAuthenticationProvider
{
    public string Name => "OIDC RBAC";

    public bool IsEnabled => true;

    public Task<ProviderAuthenticationResult> Authenticate(string username, string password)
    {
        throw new AuthenticationException("This account uses OIDC authentication. Please use the SSO login button.");
    }

    public bool HasPassword(User user)
    {
        return false;
    }

    public Task ChangePassword(User user, string newPassword)
    {
        throw new NotSupportedException("Password changes are managed by the identity provider.");
    }
}
