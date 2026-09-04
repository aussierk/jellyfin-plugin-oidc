using System;
using System.IO;
using System.Text.RegularExpressions;
using Jellyfin.Plugin.OIDC.Configuration;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.OIDC.Services;

/// <summary>
/// Resolves the actual OIDC client secret to use for a provider, so it doesn't have to be
/// stored in plaintext inside the plugin's own XML config. Checked in order:
/// 1. <see cref="OidcProviderConfig.ClientSecretFile"/> — read from disk (trimmed) if set and
///    readable, e.g. a Docker/Kubernetes-mounted secret file.
/// 2. <see cref="OidcProviderConfig.ClientSecret"/> written as <c>${ENV_VAR_NAME}</c> — resolved
///    from the process environment.
/// 3. <see cref="OidcProviderConfig.ClientSecret"/> verbatim (existing behaviour) otherwise.
/// A configured file or env reference that can't be resolved falls through rather than
/// silently authenticating with an empty secret.
///
/// Resolution is entirely per-provider — this reads only the <see cref="OidcProviderConfig"/>
/// passed in, never any other provider's config. With multiple providers configured, each one
/// needs its own file/variable; pointing two providers at the same source makes them
/// authenticate with the same secret, which the IdP will reject for whichever provider it
/// doesn't actually belong to.
/// </summary>
public static class ClientSecretResolver
{
    private static readonly Regex EnvVarReference = new(
        @"^\$\{([A-Za-z_][A-Za-z0-9_]*)\}$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static string Resolve(OidcProviderConfig provider, ILogger? logger = null)
    {
        if (!string.IsNullOrWhiteSpace(provider.ClientSecretFile))
        {
            try
            {
                return File.ReadAllText(provider.ClientSecretFile).Trim();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
            {
                logger?.LogWarning(
                    "OIDC provider {Provider}: could not read ClientSecretFile '{Path}' ({Message}); falling back to ClientSecret",
                    provider.ProviderId, provider.ClientSecretFile, ex.Message);
            }
        }

        var secret = provider.ClientSecret ?? string.Empty;
        var envMatch = EnvVarReference.Match(secret);
        if (envMatch.Success)
        {
            var varName = envMatch.Groups[1].Value;
            var value = Environment.GetEnvironmentVariable(varName);
            if (value != null)
            {
                return value;
            }

            logger?.LogWarning(
                "OIDC provider {Provider}: environment variable '{Var}' referenced by ClientSecret is not set",
                provider.ProviderId, varName);
        }

        return secret;
    }
}
