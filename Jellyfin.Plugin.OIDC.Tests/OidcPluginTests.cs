using Jellyfin.Plugin.OIDC.Configuration;
using Jellyfin.Plugin.OIDC.Tests.Fixtures;
using Xunit;

namespace Jellyfin.Plugin.OIDC.Tests;

[Xunit.Collection("OidcPlugin")]
public class OidcPluginTests
{
    private readonly PluginTestFixture _fixture;

    public OidcPluginTests(PluginTestFixture fixture) => _fixture = fixture;

    [Fact]
    public void UpdateConfiguration_DuplicateProviderId_Throws()
    {
        var config = new PluginConfiguration
        {
            Providers =
            [
                new OidcProviderConfig { ProviderId = "keycloak", DisplayName = "A" },
                new OidcProviderConfig { ProviderId = "keycloak", DisplayName = "B" }
            ]
        };

        var ex = Assert.Throws<ArgumentException>(() => _fixture.SetConfiguration(config));
        Assert.Contains("keycloak", ex.Message);
    }

    [Fact]
    public void UpdateConfiguration_DuplicateProviderId_DifferentCase_Throws()
    {
        var config = new PluginConfiguration
        {
            Providers =
            [
                new OidcProviderConfig { ProviderId = "Keycloak", DisplayName = "A" },
                new OidcProviderConfig { ProviderId = "keycloak", DisplayName = "B" }
            ]
        };

        Assert.Throws<ArgumentException>(() => _fixture.SetConfiguration(config));
    }

    [Fact]
    public void UpdateConfiguration_DuplicateProviderId_WhitespaceVariant_Throws()
    {
        var config = new PluginConfiguration
        {
            Providers =
            [
                new OidcProviderConfig { ProviderId = "keycloak", DisplayName = "A" },
                new OidcProviderConfig { ProviderId = " keycloak ", DisplayName = "B" }
            ]
        };

        Assert.Throws<ArgumentException>(() => _fixture.SetConfiguration(config));
    }

    [Fact]
    public void UpdateConfiguration_MultipleBlankProviderIds_DoesNotThrow()
    {
        // Blank IDs aren't a collision - an incomplete-but-unique-enough draft row shouldn't
        // block saving the rest of the form; GetProvider already requires a non-empty match.
        var config = new PluginConfiguration
        {
            Providers =
            [
                new OidcProviderConfig { ProviderId = "", DisplayName = "New Provider" },
                new OidcProviderConfig { ProviderId = "", DisplayName = "Another New Provider" }
            ]
        };

        _fixture.SetConfiguration(config);

        Assert.Equal(2, _fixture.Plugin.Configuration.Providers.Count);
    }

    [Theory]
    [InlineData("key cloak")]
    [InlineData("keycloak/../etc")]
    [InlineData("keycloak\n")]
    [InlineData("keycloak\"")]
    public void UpdateConfiguration_ProviderIdWithDisallowedCharacters_Throws(string providerId)
    {
        var config = new PluginConfiguration
        {
            Providers = [new OidcProviderConfig { ProviderId = providerId, DisplayName = "A" }]
        };

        var ex = Assert.Throws<ArgumentException>(() => _fixture.SetConfiguration(config));
        Assert.Contains("invalid", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void UpdateConfiguration_ProviderIdWithAllowedCharacters_DoesNotThrow()
    {
        var config = new PluginConfiguration
        {
            Providers = [new OidcProviderConfig { ProviderId = "keycloak-01.prod_test", DisplayName = "A" }]
        };

        _fixture.SetConfiguration(config);

        Assert.Single(_fixture.Plugin.Configuration.Providers);
    }

    [Fact]
    public void UpdateConfiguration_UniqueProviderIds_DoesNotThrow()
    {
        var config = new PluginConfiguration
        {
            Providers =
            [
                new OidcProviderConfig { ProviderId = "keycloak", DisplayName = "A" },
                new OidcProviderConfig { ProviderId = "okta", DisplayName = "B" }
            ]
        };

        _fixture.SetConfiguration(config);

        Assert.Equal(2, _fixture.Plugin.Configuration.Providers.Count);
    }

    [Fact]
    public void UpdateConfiguration_IssuerUrlChangedWithoutRepinning_Throws()
    {
        // The config-page blocks this in the UI; the plugin-configuration API can't be trusted to.
        var config = new PluginConfiguration
        {
            Providers =
            [
                new OidcProviderConfig
                {
                    ProviderId = "keycloak",
                    Authority = "https://new-idp.example.com/realms/x",
                    PinnedAuthority = "https://old-idp.example.com/realms/x",
                    PinnedIssuer = "https://old-idp.example.com/realms/x"
                }
            ]
        };

        var ex = Assert.Throws<ArgumentException>(() => _fixture.SetConfiguration(config));
        Assert.Contains("keycloak", ex.Message);
        Assert.Contains("Test Connection", ex.Message);
    }

    [Fact]
    public void UpdateConfiguration_AuthorityMatchesPinnedAuthority_DoesNotThrow()
    {
        var config = new PluginConfiguration
        {
            Providers =
            [
                new OidcProviderConfig
                {
                    ProviderId = "keycloak",
                    Authority = "https://idp.example.com/realms/x",
                    PinnedAuthority = "https://idp.example.com/realms/x",
                    PinnedIssuer = "https://idp.example.com/realms/x"
                }
            ]
        };

        _fixture.SetConfiguration(config);

        Assert.Single(_fixture.Plugin.Configuration.Providers);
    }

    [Fact]
    public void UpdateConfiguration_LegacyPinsWithNoPinnedAuthority_DoesNotThrow()
    {
        // Pre-rework config: PinnedIssuer set, PinnedAuthority empty. The save-time guard only
        // fires when PinnedAuthority is known; login-time ValidateOrPinEndpoints handles the rest.
        var config = new PluginConfiguration
        {
            Providers =
            [
                new OidcProviderConfig
                {
                    ProviderId = "keycloak",
                    Authority = "https://idp.example.com/realms/x",
                    PinnedAuthority = "",
                    PinnedIssuer = "https://idp.example.com/realms/x"
                }
            ]
        };

        _fixture.SetConfiguration(config);

        Assert.Single(_fixture.Plugin.Configuration.Providers);
    }
}
