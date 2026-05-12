using System;
using System.Collections.Generic;
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

    public override string Name => "OIDC RBAC";

    public override Guid Id => Guid.Parse("d4e5f6a7-b8c9-0d1e-2f3a-4b5c6d7e8f90");

    public override string Description => "Advanced OIDC authentication with role-based library access control";

    public IEnumerable<PluginPageInfo> GetPages()
    {
        var ns = GetType().Namespace;
        return new[]
        {
            new PluginPageInfo
            {
                Name = Name,
                EmbeddedResourcePath = $"{ns}.Configuration.configPage.html"
            },
            new PluginPageInfo
            {
                Name = "oidcrbacjs",
                EmbeddedResourcePath = $"{ns}.Configuration.oidcrbac.js"
            }
        };
    }
}
