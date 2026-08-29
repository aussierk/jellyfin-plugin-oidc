using System;
using System.Collections.Generic;
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
    // Checked against Content-Length and again while streaming, so a chunked response can't bypass it.
    private const long MaxProfileImageBytes = 5 * 1024 * 1024;

    // Raster only, SVG excluded: the user-influenced picture claim gets the narrowest allowlist,
    // and an avatar is never consumed as a CSS url() value the way a provider button icon is.
    private static readonly HashSet<string> _allowedImageMimeTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "image/png", "image/jpeg", "image/jpg", "image/gif", "image/webp"
    };

    private readonly GuardedHttpClientFactory _guardedHttp;
    private readonly IUserManager _userManager;
    private readonly IServerConfigurationManager _serverConfigurationManager;
    private readonly IProviderManager _providerManager;
    private readonly ILogger<ProfileImageService> _logger;

    public ProfileImageService(
        GuardedHttpClientFactory guardedHttp,
        IUserManager userManager,
        IServerConfigurationManager serverConfigurationManager,
        IProviderManager providerManager,
        ILogger<ProfileImageService> logger)
    {
        _guardedHttp = guardedHttp;
        _userManager = userManager;
        _serverConfigurationManager = serverConfigurationManager;
        _providerManager = providerManager;
        _logger = logger;
    }

    /// 
    /// Downloads the image at <paramref name="pictureUrl"/> and sets it as the user's profile
    /// image, overwriting any existing one. Never throws: avatar sync must not break login.
    /// 
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

        // The picture claim is user-influenced, so it must NOT inherit the provider's loopback/
        // link-local trust flags (those cover the admin-fixed Authority URL). Loopback and link-local
        // (which includes the 169.254.169.254 cloud-metadata endpoint) are always blocked here;
        // only the global BlockPrivateNetworkAuthorities still applies.
        var guarded = await _guardedHttp.CreateAsync(
            pictureUrl, allowLoopback: false, allowLinkLocal: false).ConfigureAwait(false);
        if (guarded.Blocked)
        {
            _logger.LogWarning("Blocked profile image fetch for {Url}: {Reason}", pictureUrl, guarded.BlockReason);
            return;
        }

        try
        {
            using var httpClient = guarded.Client!;

            // ResponseHeadersRead so a chunked/no-Content-Length body can't bypass the size cap below.
            using var response = await httpClient.GetAsync(pictureUrl, HttpCompletionOption.ResponseHeadersRead).ConfigureAwait(false);
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
            if (string.IsNullOrEmpty(mimeType) || !_allowedImageMimeTypes.Contains(mimeType))
            {
                _logger.LogWarning(
                    "Profile image URL {Url} returned unsupported content type '{ContentType}'",
                    pictureUrl, mimeType ?? "(none)");
                return;
            }

            using var imageBuffer = await ReadCappedAsync(response, MaxProfileImageBytes).ConfigureAwait(false);
            if (imageBuffer == null)
            {
                _logger.LogWarning(
                    "Profile image at {Url} exceeded the {MaxBytes}-byte limit while streaming",
                    pictureUrl, MaxProfileImageBytes);
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

            await _providerManager.SaveImage(imageBuffer, mimeType, user.ProfileImage.Path).ConfigureAwait(false);

            await _userManager.UpdateUserAsync(user).ConfigureAwait(false);

            _logger.LogInformation("Applied OIDC profile image for user {Username}", user.Username);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Failed to apply profile image from {Url}", pictureUrl);
        }
    }

    /// Copies the response body into memory, stopping and returning null if it exceeds <paramref name="maxBytes"/>.
    private static async Task<MemoryStream?> ReadCappedAsync(HttpResponseMessage response, long maxBytes)
    {
        var buffer = new MemoryStream();
        var source = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
        await using (source.ConfigureAwait(false))
        {
            var chunk = new byte[81920];
            int read;
            while ((read = await source.ReadAsync(chunk).ConfigureAwait(false)) > 0)
            {
                if (buffer.Length + read > maxBytes)
                {
                    buffer.Dispose();
                    return null;
                }

                buffer.Write(chunk, 0, read);
            }
        }

        buffer.Position = 0;
        return buffer;
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
