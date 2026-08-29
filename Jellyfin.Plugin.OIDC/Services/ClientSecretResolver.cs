using System;
using System.IO;
using System.Text.RegularExpressions;
using Jellyfin.Plugin.OIDC.Configuration;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.OIDC.Services;

/// 
/// Resolves a provider's client secret: <see cref="OidcProviderConfig.ClientSecretFile"/> if
/// readable, then <see cref="OidcProviderConfig.ClientSecret"/> as <c>${ENV_VAR}</c> if it
/// matches that form, else the value verbatim. An unresolvable file/env reference falls through
/// rather than silently authenticating with an empty secret.
/// 
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
