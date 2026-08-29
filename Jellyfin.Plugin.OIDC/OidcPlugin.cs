using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Jellyfin.Plugin.OIDC.Configuration;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Serialization;

namespace Jellyfin.Plugin.OIDC;

public class OidcPlugin : BasePlugin<PluginConfiguration>, IHasWebPages
{
    public OidcPlugin(IApplicationPaths applicationPaths, IXmlSerializer xmlSerializer)
        : base(applicationPaths, xmlSerializer)
    {
        Instance = this;
    }

    public static OidcPlugin? Instance { get; private set; }

    /// <summary>
    /// The live plugin configuration. Non-null: once Jellyfin has constructed the plugin - always
    /// the case while a request is being served - this is <c>Instance.Configuration</c>. The empty
    /// fallback only covers the sliver of startup before the constructor runs (and unit tests that
    /// call config-dependent helpers without a plugin host), so call sites don't each need their own
    /// <c>?? default</c>.
    /// </summary>
    internal static PluginConfiguration CurrentConfig => Instance?.Configuration ?? _emptyConfig;

    private static readonly PluginConfiguration _emptyConfig = new();

    // \A/\z (not ^/$) so a trailing newline can't sneak past - $ matches before a final "\n" too.
    private static readonly Regex _validProviderId = new(
        @"\A[A-Za-z0-9._-]{1,64}\z", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private readonly object _configWriteLock = new();

    /// <summary>
    /// Writes the whole plugin config to disk, serialized against a concurrent write (two
    /// providers re-pinning at once) so two <see cref="BasePlugin{T}.SaveConfiguration"/> calls
    /// can't interleave. The plugin config is small, bounded admin state - the identity map lives
    /// in <see cref="Services.UserProviderMapStore"/> and is written separately.
    /// </summary>
    internal void PersistConfiguration()
    {
        lock (_configWriteLock)
        {
            SaveConfiguration();
        }
    }

    public override string Name => "SSO-OIDC Authentication";

    public override Guid Id => Guid.Parse("e1c020c5-3972-4b7b-9538-ee4934cc902c");

    public override string Description => "Advanced OIDC authentication with role-based library access control";

    /// Validates before persisting - the admin UI's own checks (duplicate id, "re-run Test
    /// Connection after editing the Issuer URL") can be bypassed by calling the plugin-configuration
    /// API directly. <see cref="Api.OidcController.GetProvider"/> resolves providers with
    /// <c>FirstOrDefault</c>, so a duplicate <c>ProviderId</c> would silently shadow another
    /// provider; and a config that moves the Issuer URL away from the value its endpoints were
    /// pinned against must not be persisted, or the next login would silently re-pin (TOFU)
    /// against an unverified issuer.
    public override void UpdateConfiguration(BasePluginConfiguration configuration)
    {
        if (configuration is PluginConfiguration config)
        {
            var providers = config.Providers ?? [];

            var duplicate = providers
                .Select(p => (p.ProviderId ?? string.Empty).Trim())
                .Where(id => id.Length > 0)
                .GroupBy(id => id, StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault(g => g.Count() > 1);
            if (duplicate != null)
            {
                throw new ArgumentException(
                    $"Provider ID '{duplicate.Key}' is used by more than one provider. Provider IDs must be unique.");
            }

            // ProviderId flows into route segments and CSS attribute-selector values; keep the
            // charset narrow as defense-in-depth for any sink that assumes it.
            var invalidId = providers.FirstOrDefault(p =>
                !string.IsNullOrEmpty(p.ProviderId) && !_validProviderId.IsMatch(p.ProviderId));
            if (invalidId != null)
            {
                throw new ArgumentException(
                    $"Provider ID '{invalidId.ProviderId}' is invalid. Provider IDs may only contain "
                    + "letters, digits, '.', '_', and '-', up to 64 characters.");
            }

            // PinnedAuthority is the Issuer the current pins were established against; if the incoming
            // Authority differs, the pins are stale for an unverified issuer.
            var stalePin = providers.FirstOrDefault(p =>
                !string.IsNullOrEmpty(p.PinnedAuthority)
                && !string.Equals(
                    (p.Authority ?? string.Empty).Trim(),
                    p.PinnedAuthority.Trim(),
                    StringComparison.OrdinalIgnoreCase));
            if (stalePin != null)
            {
                throw new ArgumentException(
                    $"Provider '{stalePin.ProviderId}': the Issuer URL was changed but its endpoint "
                    + "pins were not re-verified. Run Test Connection for this provider, or clear its "
                    + "Pinned* fields so they re-establish on the next login.");
            }
        }

        base.UpdateConfiguration(configuration);
    }

    public IEnumerable<PluginPageInfo> GetPages()
    {
        var ns = GetType().Namespace;
        return new[]
        {
            new PluginPageInfo
            {
                Name = Name,
                EmbeddedResourcePath = $"{ns}.Configuration.configPage.html",
                EnableInMainMenu = true,
                MenuIcon = "login"
            },
            new PluginPageInfo
            {
                Name = "oidcrbacjs",
                EmbeddedResourcePath = $"{ns}.Configuration.oidcrbac.js"
            }
        };
    }
}
