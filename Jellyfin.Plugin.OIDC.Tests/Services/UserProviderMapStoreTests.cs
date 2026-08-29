using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.OIDC.Configuration;
using Jellyfin.Plugin.OIDC.Services;
using Jellyfin.Plugin.OIDC.Tests.Fixtures;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Jellyfin.Plugin.OIDC.Tests.Services;

[Xunit.Collection("OidcPlugin")]
public class UserProviderMapStoreTests : IDisposable
{
    private readonly PluginTestFixture _fixture;
    private readonly string _path;

    public UserProviderMapStoreTests(PluginTestFixture fixture)
    {
        _fixture = fixture;
        _path = Path.Combine(Path.GetTempPath(), $"oidc-mapstore-{Guid.NewGuid():N}.json");
    }

    public void Dispose()
    {
        try { File.Delete(_path); } catch { /* best effort */ }
    }

    private UserProviderMapStore NewStore() => new(_path, NullLogger<UserProviderMapStore>.Instance);

    private static UserProviderEntry Row(string sub, string user, string id = "u") =>
        new() { ProviderId = "kc", Subject = sub, Username = user, UserId = id };

    [Fact]
    public void Upsert_RoundTripsThroughItsOwnFile()
    {
        var store = NewStore();
        store.Upsert(Row("s-1", "alice", "u1"));
        store.Upsert(Row("s-2", "bob", "u2"));
        store.Save();

        var reloaded = NewStore().Snapshot();
        Assert.Equal(2, reloaded.Count);
        Assert.Equal(new[] { "alice", "bob" }, reloaded.Select(e => e.Username).OrderBy(x => x));
    }

    [Fact]
    public void Upsert_ReplacesRowsCollidingOnUsernameOrSubjectProvider()
    {
        var store = NewStore();
        store.Upsert(Row("s-1", "alice", "u1"));
        store.Upsert(Row("s-1", "alice-renamed", "u1")); // same (subject, provider) → replaces

        var row = Assert.Single(store.Snapshot());
        Assert.Equal("alice-renamed", row.Username);
    }

    [Fact]
    public void FindBySubject_ReturnsCopy()
    {
        var store = NewStore();
        store.Upsert(Row("s-1", "alice", "u1"));

        var bySub = store.FindBySubject("kc", "s-1");
        Assert.NotNull(bySub);
        bySub!.Username = "mutated"; // must not affect the store
        Assert.Equal("alice", store.FindBySubject("kc", "s-1")!.Username);
        Assert.Null(store.FindBySubject("kc", "nope"));
    }

    [Fact]
    public void BindOidcLogin_UnchangedRow_DoesNotWrite()
    {
        var store = NewStore();
        store.Upsert(Row("s-1", "alice", "u1"));

        Assert.Equal(MapBindOutcome.Unchanged,
            store.BindOidcLogin("kc", "s-1", "alice", "u1", string.Empty, false, trustLink: false).Outcome);
    }

    [Fact]
    public void BindOidcLogin_BackfillsLegacyRow()
    {
        var store = NewStore();
        store.Upsert(new UserProviderEntry { ProviderId = "kc", Subject = "", Username = "alice", UserId = "" });

        var r = store.BindOidcLogin("kc", "s-1", "alice", "u1", string.Empty, false, trustLink: false);

        Assert.Equal(MapBindOutcome.Rebound, r.Outcome);
        Assert.False(r.WasIdentityMismatch); // legacy back-fill, not a provider migration
        var row = store.FindBySubject("kc", "s-1")!;
        Assert.Equal("u1", row.UserId);
    }

    [Fact]
    public void BindOidcLogin_ProviderMigration_WithTrustLink_Rebinds()
    {
        var store = NewStore();
        store.Upsert(Row("s-1", "alice", "u1"));

        var r = store.BindOidcLogin("authentik", "s-1", "alice", "u1", string.Empty, false, trustLink: true);

        Assert.Equal(MapBindOutcome.Rebound, r.Outcome);
        Assert.True(r.WasIdentityMismatch);
        Assert.True(r.MismatchOnProvider);
        Assert.Equal("kc", r.PreviousProviderId);
        Assert.Equal("authentik", store.FindBySubject("authentik", "s-1")!.ProviderId);
    }

    [Fact]
    public void BindOidcLogin_DifferentSubjectSameUsername_DifferentUser_IsConflict_EvenWithTrustLink()
    {
        var store = NewStore();
        store.Upsert(Row("s-1", "alice", "u1"));

        // A different subject presenting the same preferred_username, resolving to a different
        // Jellyfin user - account takeover attempt. No-displace: even a trusted link can't rebind
        // a row that already points at another user.
        foreach (var trust in new[] { false, true })
        {
            var denied = store.BindOidcLogin("kc", "s-2", "alice", "u2", string.Empty, false, trustLink: trust);
            Assert.Equal(MapBindOutcome.Conflict, denied.Outcome);
            Assert.Equal("s-1", store.FindBySubject("kc", "s-1")!.Subject); // row untouched
            Assert.Equal("u1", store.FindBySubject("kc", "s-1")!.UserId);
        }
    }

    [Fact]
    public void BindOidcLogin_DifferentSubjectSameUser_WithTrustLink_Rebinds()
    {
        // The verified-email link resolved to the SAME Jellyfin user the row already owns
        // (e.g. an IdP-side identity rebuild changed the subject) - allowed to re-bind.
        var store = NewStore();
        store.Upsert(Row("s-1", "alice", "u1"));

        var r = store.BindOidcLogin("kc", "s-2", "alice", "u1", string.Empty, false, trustLink: true);

        Assert.Equal(MapBindOutcome.Rebound, r.Outcome);
        Assert.Equal("s-2", store.FindBySubject("kc", "s-2")!.Subject);
    }

    [Fact]
    public void BindOidcLogin_NoRow_Creates()
    {
        var store = NewStore();

        var r = store.BindOidcLogin("kc", "s-9", "carol", "u9", string.Empty, false, trustLink: false);
        Assert.Equal(MapBindOutcome.Created, r.Outcome);
        Assert.Equal("u9", store.FindBySubject("kc", "s-9")!.UserId);
    }

    [Fact]
    public void SetLogoutSid_And_ResolveForLogout()
    {
        var store = NewStore();
        store.Upsert(Row("s-1", "alice", "u1"));

        Assert.True(store.SetLogoutSid("kc", "s-1", "sess-42"));
        Assert.False(store.SetLogoutSid("kc", "s-1", "sess-42")); // unchanged

        Assert.Equal("u1", store.ResolveForLogout("kc", sub: "", sid: "sess-42", username: null)!.UserId);
        Assert.Equal("u1", store.ResolveForLogout("kc", sub: "s-1", sid: null, username: null)!.UserId);
        Assert.Null(store.ResolveForLogout("kc", sub: "", sid: "", username: null));
    }

    [Fact]
    public async Task SaveDebounced_CoalescesAndFlushesOnStop()
    {
        var store = NewStore();
        for (var i = 0; i < 25; i++)
        {
            store.Upsert(Row($"s-{i}", $"u{i}", $"id{i}")); // each calls SaveDebounced internally
        }

        Assert.False(File.Exists(_path)); // nothing written on the calling path

        await store.StopAsync(CancellationToken.None); // shutdown flush
        Assert.True(File.Exists(_path));
        Assert.Equal(25, NewStore().Count);
    }

    [Fact]
    public void PruneUser_RemovesMatchingRowsAndPersists()
    {
        var store = NewStore();
        store.Upsert(Row("s-1", "alice", "u1"));
        store.Upsert(Row("s-2", "bob", "u2"));
        store.Upsert(new UserProviderEntry { ProviderId = "kc", Subject = "leg", Username = "legacy", UserId = "" });

        Assert.Equal(1, store.PruneUser(Guid.Empty, "legacy")); // legacy row matched by username
        Assert.Equal(0, store.PruneUser(Guid.NewGuid(), "nobody"));

        Assert.Equal(2, NewStore().Count);
    }

    [Fact]
    public void Load_CorruptFile_StartsEmpty()
    {
        File.WriteAllText(_path, "{ not json");

        Assert.Empty(NewStore().Snapshot());
    }

    [Fact]
    public void Construction_RemovesStaleTempFile()
    {
        File.WriteAllText(_path + ".tmp", "leftover from a crash mid-write");

        _ = NewStore();

        Assert.False(File.Exists(_path + ".tmp"));
    }

    [Fact]
    public async Task StartAsync_MigratesRowsOutOfPluginConfig()
    {
        _fixture.SetConfiguration(new PluginConfiguration
        {
            UserProviderMap = [Row("s-1", "alice", "u1"), Row("s-2", "bob", "u2")]
        });

        var store = NewStore(); // file absent
        await store.StartAsync(CancellationToken.None);

        Assert.Equal(2, store.Count);
        Assert.True(File.Exists(_path));
        Assert.Empty(_fixture.Plugin.Configuration.UserProviderMap); // old copy cleared
    }
}
