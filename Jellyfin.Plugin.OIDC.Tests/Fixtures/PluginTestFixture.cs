using System.Collections.Generic;
using System.IO;
using Jellyfin.Plugin.OIDC.Configuration;
using Jellyfin.Plugin.OIDC.Services;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Model.Serialization;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Jellyfin.Plugin.OIDC.Tests.Fixtures;

/// Creates a real OidcPlugin (sets the static OidcPlugin.Instance) shared via IClassFixture.
public sealed class PluginTestFixture : IDisposable
{
    private readonly string _tempDir;

    public OidcPlugin Plugin { get; }

    /// An in-memory map store backed by the current config's <c>UserProviderMap</c> list, so tests
    /// that seed / assert via <c>config.UserProviderMap</c> keep working. Rebuilt by <see cref="SetConfiguration"/>.
    public UserProviderMapStore MapStore { get; private set; }

    public PluginTestFixture()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"oidc-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);

        var appPaths = Substitute.For<IApplicationPaths>();
        appPaths.PluginConfigurationsPath.Returns(_tempDir);
        appPaths.DataPath.Returns(_tempDir);

        var xmlSerializer = Substitute.For<IXmlSerializer>();
        xmlSerializer
            .DeserializeFromFile(Arg.Any<Type>(), Arg.Any<string>())
            .Returns(_ => new PluginConfiguration());

        Plugin = new OidcPlugin(appPaths, xmlSerializer);
        MapStore = new UserProviderMapStore(new List<UserProviderEntry>(), NullLogger<UserProviderMapStore>.Instance);
    }

    /// Sets the config and re-pins OidcPlugin.Instance to this fixture - xUnit constructs all
    /// class fixtures up front, and each constructor overwrites that static field.
    public void SetConfiguration(PluginConfiguration config)
    {
        typeof(OidcPlugin)
            .GetProperty(nameof(OidcPlugin.Instance))!
            .SetValue(null, Plugin);
        Plugin.UpdateConfiguration(config);
        MapStore = new UserProviderMapStore(Plugin.Configuration.UserProviderMap, NullLogger<UserProviderMapStore>.Instance);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); }
        catch { /* best-effort cleanup */ }
    }
}
