using System.IO;
using Jellyfin.Plugin.OIDC.Configuration;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Model.Serialization;
using NSubstitute;

namespace Jellyfin.Plugin.OIDC.Tests.Fixtures;

/// <summary>
/// Creates a real OidcPlugin instance (which sets the static OidcPlugin.Instance)
/// with a fresh default configuration. Shared across tests in the same class via
/// IClassFixture&lt;PluginTestFixture&gt;.
/// </summary>
public sealed class PluginTestFixture : IDisposable
{
    private readonly string _tempDir;

    public OidcPlugin Plugin { get; }

    public PluginTestFixture()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"oidc-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);

        var appPaths = Substitute.For<IApplicationPaths>();
        appPaths.PluginConfigurationsPath.Returns(_tempDir);
        appPaths.DataPath.Returns(_tempDir);

        // XmlSerializer stub: returns a fresh default config on Deserialize,
        // and does nothing on Serialize (we don't need disk persistence in tests).
        var xmlSerializer = Substitute.For<IXmlSerializer>();
        xmlSerializer
            .DeserializeFromFile(Arg.Any<Type>(), Arg.Any<string>())
            .Returns(_ => new PluginConfiguration());

        Plugin = new OidcPlugin(appPaths, xmlSerializer);
    }

    /// <summary>
    /// Replaces the plugin configuration for a test scenario.
    /// Also re-pins the static OidcPlugin.Instance to this fixture's plugin, because
    /// xUnit creates all class fixtures before running tests, and each OidcPlugin
    /// constructor overwrites the static field — so the last-constructed fixture wins
    /// unless we correct it here.
    /// </summary>
    public void SetConfiguration(PluginConfiguration config)
    {
        typeof(OidcPlugin)
            .GetProperty(nameof(OidcPlugin.Instance))!
            .SetValue(null, Plugin);
        Plugin.UpdateConfiguration(config);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); }
        catch { /* best-effort cleanup */ }
    }
}
