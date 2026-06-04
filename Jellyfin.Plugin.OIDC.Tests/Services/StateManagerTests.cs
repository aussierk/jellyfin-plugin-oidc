using Jellyfin.Plugin.OIDC.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Jellyfin.Plugin.OIDC.Tests.Services;

public class StateManagerTests : IDisposable
{
    private readonly StateManager _manager = new(NullLogger<StateManager>.Instance);

    public void Dispose() => _manager.Dispose();

    // ── OidcState ──────────────────────────────────────────────────────────────

    [Fact]
    public void StoreState_ReturnNonNullKey()
    {
        var key = _manager.StoreState(MakeState());
        Assert.NotNull(key);
        Assert.NotEmpty(key);
    }

    [Fact]
    public void ConsumeState_ValidKey_ReturnsStateAndRemovesIt()
    {
        var key = _manager.StoreState(MakeState("provider-a"));
        var state = _manager.ConsumeState(key!);
        Assert.NotNull(state);
        Assert.Equal("provider-a", state!.ProviderId);
        // Second consume must return null (one-time use)
        Assert.Null(_manager.ConsumeState(key!));
    }

    [Fact]
    public void ConsumeState_UnknownKey_ReturnsNull()
    {
        Assert.Null(_manager.ConsumeState("does-not-exist"));
    }

    [Fact]
    public void ConsumeState_ExpiredState_ReturnsNull()
    {
        // Create a state whose CreatedAt is already > 10 minutes ago
        var expired = new OidcState
        {
            ProviderId = "p",
            Nonce = "n",
            CodeVerifier = "cv",
            RedirectUri = "https://example.com/callback",
            // Override the default CreatedAt to a past time
        };
        // Use reflection to set CreatedAt to a stale time
        typeof(OidcState)
            .GetProperty(nameof(OidcState.CreatedAt))!
            .SetValue(expired, DateTimeOffset.UtcNow.AddMinutes(-11));

        var key = _manager.StoreState(expired);
        Assert.Null(_manager.ConsumeState(key!));
    }

    [Fact]
    public void StoreState_AtCap_ReturnsNull()
    {
        // Fill up to the 500-entry cap
        for (var i = 0; i < 500; i++)
            _manager.StoreState(MakeState());

        // The 501st should be rejected
        Assert.Null(_manager.StoreState(MakeState()));
    }

    // ── AuthorizedSession ──────────────────────────────────────────────────────

    [Fact]
    public void StoreAuthorizedSession_ReturnsNonNullToken()
    {
        var token = _manager.StoreAuthorizedSession(MakeSession());
        Assert.NotNull(token);
    }

    [Fact]
    public void ConsumeAuthorizedSession_ValidToken_ReturnsSessionAndRemovesIt()
    {
        var token = _manager.StoreAuthorizedSession(MakeSession("alice", "keycloak"));
        var session = _manager.ConsumeAuthorizedSession(token!);
        Assert.NotNull(session);
        Assert.Equal("alice", session!.Username);
        Assert.Equal("keycloak", session.ProviderId);
        Assert.Null(_manager.ConsumeAuthorizedSession(token!));
    }

    [Fact]
    public void ConsumeAuthorizedSession_UnknownToken_ReturnsNull()
    {
        Assert.Null(_manager.ConsumeAuthorizedSession("unknown"));
    }

    [Fact]
    public void ConsumeAuthorizedSession_ExpiredSession_ReturnsNull()
    {
        var session = MakeSession();
        typeof(AuthorizedSession)
            .GetProperty(nameof(AuthorizedSession.CreatedAt))!
            .SetValue(session, DateTimeOffset.UtcNow.AddMinutes(-6));

        var token = _manager.StoreAuthorizedSession(session);
        Assert.Null(_manager.ConsumeAuthorizedSession(token!));
    }

    [Fact]
    public void StoreAuthorizedSession_AtCap_ReturnsNull()
    {
        for (var i = 0; i < 200; i++)
            _manager.StoreAuthorizedSession(MakeSession());

        Assert.Null(_manager.StoreAuthorizedSession(MakeSession()));
    }

    // ── Lifecycle ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task StartStop_DoesNotThrow()
    {
        await _manager.StartAsync(CancellationToken.None);
        await _manager.StopAsync(CancellationToken.None);
    }

    // ── helpers ────────────────────────────────────────────────────────────────

    private static OidcState MakeState(string providerId = "test-provider") => new()
    {
        ProviderId = providerId,
        Nonce = Guid.NewGuid().ToString("N"),
        CodeVerifier = Guid.NewGuid().ToString("N"),
        RedirectUri = "https://jellyfin.example.com/sso/OIDC/Callback/test"
    };

    private static AuthorizedSession MakeSession(
        string username = "user", string providerId = "provider") => new()
    {
        ProviderId = providerId,
        Username = username,
        DisplayName = username,
        Roles = []
    };
}
