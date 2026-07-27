using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Jellyfin.Database.Implementations.Entities;
using MediaBrowser.Controller.Configuration;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Providers;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.OIDC.Services;

public class ProfileImageService
{
    // Cap the download so a malicious/compromised picture-claim host can't exhaust memory or
    // disk via an oversized response. Partial mitigation: a host that omits Content-Length
    // isn't caught by this check, only by whatever limits the underlying HttpClient applies.
    private const long MaxProfileImageBytes = 5 * 1024 * 1024;

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IUserManager _userManager;
    private readonly IServerConfigurationManager _serverConfigurationManager;
    private readonly IProviderManager _providerManager;
    private readonly ILogger<ProfileImageService> _logger;

    public ProfileImageService(
        IHttpClientFactory httpClientFactory,
        IUserManager userManager,
        IServerConfigurationManager serverConfigurationManager,
        IProviderManager providerManager,
        ILogger<ProfileImageService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _userManager = userManager;
        _serverConfigurationManager = serverConfigurationManager;
        _providerManager = providerManager;
        _logger = logger;
    }

    /// <summary>
    /// Downloads the image at <paramref name="pictureUrl"/> and sets it as the user's profile
    /// image, overwriting any existing one. Never throws: avatar sync must not break login.
    /// </summary>
    public async Task ApplyProfileImageAsync(Guid userId, string? pictureUrl, string providerId)
    {
        if (string.IsNullOrWhiteSpace(pictureUrl))
        {
            return;
        }

        if (!Uri.TryCreate(pictureUrl, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            _logger.LogWarning("Rejected profile image URL with disallowed scheme: {Url}", pictureUrl);
            return;
        }

        // The picture claim can come from the IdP's userinfo/token payload — and on some IdPs
        // (e.g. Keycloak, Authentik) end users can set it themselves — so it gets the same
        // SSRF guard as Authority, using that provider's own loopback/link-local trust settings.
        var provider = OidcPlugin.Instance?.Configuration?.Providers
            .FirstOrDefault(p => string.Equals(p.ProviderId, providerId, StringComparison.OrdinalIgnoreCase));
        var blockPrivateNetworks = OidcPlugin.Instance?.Configuration?.BlockPrivateNetworkAuthorities ?? false;
        var blockReason = await AuthorityGuard.ValidateAsync(
            pictureUrl,
            provider?.AllowLoopbackAuthority ?? false,
            provider?.AllowLinkLocalAuthority ?? false,
            blockPrivateNetworks).ConfigureAwait(false);
        if (blockReason != null)
        {
            _logger.LogWarning("Blocked profile image fetch for {Url}: {Reason}", pictureUrl, blockReason);
            return;
        }

        try
        {
            var httpClient = _httpClientFactory.CreateClient("OidcPluginImage");
            using var response = await httpClient.GetAsync(pictureUrl).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning(
                    "Failed to download profile image from {Url}: HTTP {Status}",
                    pictureUrl, (int)response.StatusCode);
                return;
            }

            var contentLength = response.Content.Headers.ContentLength;
            if (contentLength.HasValue && contentLength.Value > MaxProfileImageBytes)
            {
                _logger.LogWarning(
                    "Profile image at {Url} exceeds the {MaxBytes}-byte limit (Content-Length: {Length})",
                    pictureUrl, MaxProfileImageBytes, contentLength.Value);
                return;
            }

            var mimeType = response.Content.Headers.ContentType?.MediaType;
            if (string.IsNullOrEmpty(mimeType) || !mimeType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogWarning(
                    "Profile image URL {Url} returned non-image content type '{ContentType}'",
                    pictureUrl, mimeType ?? "(none)");
                return;
            }

            var extension = GetExtensionForMimeType(mimeType);

            var user = _userManager.GetUserById(userId);
            if (user == null)
            {
                _logger.LogWarning("User {UserId} not found while applying profile image", userId);
                return;
            }

            var userDataPath = Path.Combine(
                _serverConfigurationManager.ApplicationPaths.UserConfigurationDirectoryPath,
                user.Username);
            var imagePath = Path.Combine(userDataPath, "profile" + extension);

            if (user.ProfileImage is not null)
            {
                await _userManager.ClearProfileImageAsync(user).ConfigureAwait(false);
            }

            user.ProfileImage = new ImageInfo(imagePath);

            var stream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
            await using (stream.ConfigureAwait(false))
            {
                await _providerManager.SaveImage(stream, mimeType, user.ProfileImage.Path).ConfigureAwait(false);
            }

            await _userManager.UpdateUserAsync(user).ConfigureAwait(false);

            _logger.LogInformation("Applied OIDC profile image for user {Username}", user.Username);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to apply profile image from {Url}", pictureUrl);
        }
    }

    private static string GetExtensionForMimeType(string mimeType)
    {
        return mimeType.ToLowerInvariant() switch
        {
            "image/png" => ".png",
            "image/gif" => ".gif",
            "image/webp" => ".webp",
            "image/jpg" or "image/jpeg" => ".jpg",
            _ => ".jpg"
        };
    }
}
