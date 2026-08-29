using System.Linq;
using System.Threading.Tasks;
using Jellyfin.Data.Events.Users;
using Jellyfin.Database.Implementations.Entities;
using Jellyfin.Plugin.OIDC.Configuration;
using Jellyfin.Plugin.OIDC.Services;
using Jellyfin.Plugin.OIDC.Tests.Fixtures;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Jellyfin.Plugin.OIDC.Tests.Services;

[Xunit.Collection("OidcPlugin")]
public class OidcUserDeletedConsumerTests
{
    private readonly PluginTestFixture _fixture;

    public OidcUserDeletedConsumerTests(PluginTestFixture fixture) => _fixture = fixture;

    private static User MakeUser(string name) =>
        new(name, "Jellyfin.Plugin.OIDC.Auth.OidcAuthProvider", "PasswordResetProviderId");

    private Task Fire(User user) =>
        new OidcUserDeletedConsumer(_fixture.MapStore, NullLogger<OidcUserDeletedConsumer>.Instance)
            .OnEvent(new UserDeletedEventArgs(user));

    [Fact]
    public async Task DeletedUser_RowsRemoved_OthersKept()
    {
        var alice = MakeUser("alice");
        var bob = MakeUser("bob");
        _fixture.SetConfiguration(new PluginConfiguration
        {
            UserProviderMap =
            [
                new UserProviderEntry { ProviderId = "kc", Subject = "s-alice", Username = "alice", UserId = alice.Id.ToString() },
                new UserProviderEntry { ProviderId = "kc", Subject = "s-bob", Username = "bob", UserId = bob.Id.ToString() }
            ]
        });

        await Fire(alice);

        var row = Assert.Single(_fixture.Plugin.Configuration.UserProviderMap);
        Assert.Equal("s-bob", row.Subject);
    }

    [Fact]
    public async Task LegacyRow_NoUserId_MatchedByUsername()
    {
        var alice = MakeUser("alice");
        _fixture.SetConfiguration(new PluginConfiguration
        {
            UserProviderMap = [new UserProviderEntry { ProviderId = "kc", Subject = "", Username = "alice", UserId = "" }]
        });

        await Fire(alice);

        Assert.Empty(_fixture.Plugin.Configuration.UserProviderMap);
    }

    [Fact]
    public async Task UnknownUser_NoChange()
    {
        _fixture.SetConfiguration(new PluginConfiguration
        {
            UserProviderMap = [new UserProviderEntry { ProviderId = "kc", Subject = "s-alice", Username = "alice", UserId = MakeUser("alice").Id.ToString() }]
        });

        await Fire(MakeUser("stranger"));

        Assert.Single(_fixture.Plugin.Configuration.UserProviderMap);
    }
}
