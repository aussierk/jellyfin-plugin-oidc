using Xunit;

namespace Jellyfin.Plugin.OIDC.Tests.Fixtures;

/// <summary>
/// Forces sequential execution for all test classes that depend on the
/// OidcPlugin.Instance static singleton. Without this, parallel test class
/// execution causes races when different fixtures overwrite the static field.
/// </summary>
[CollectionDefinition("OidcPlugin")]
public class OidcPluginCollection : ICollectionFixture<PluginTestFixture>
{
}
