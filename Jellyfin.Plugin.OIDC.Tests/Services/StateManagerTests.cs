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
            CsrfToken = "csrf",
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
    public void ConsumeAuthorizedSession_PictureUrlSet_IsPreserved()
    {
        var token = _manager.StoreAuthorizedSession(MakeSession(pictureUrl: "https://idp.example.com/avatar.png"));

        var session = _manager.ConsumeAuthorizedSession(token!);

        Assert.NotNull(session);
        Assert.Equal("https://idp.example.com/avatar.png", session!.PictureUrl);
    }

    [Fact]
    public void ConsumeAuthorizedSession_PictureUrlNotSet_DefaultsToNull()
    {
        var token = _manager.StoreAuthorizedSession(MakeSession());

        var session = _manager.ConsumeAuthorizedSession(token!);

        Assert.NotNull(session);
        Assert.Null(session!.PictureUrl);
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

    // ── PeekAuthorizedSession ──────────────────────────────────────────────────

    [Fact]
    public void PeekAuthorizedSession_ValidToken_ReturnsSessionWithoutRemovingIt()
    {
        var token = _manager.StoreAuthorizedSession(MakeSession("alice", "keycloak"));

        var first = _manager.PeekAuthorizedSession(token!);
        var second = _manager.PeekAuthorizedSession(token!);

        Assert.NotNull(first);
        Assert.NotNull(second);
        Assert.Equal("alice", first!.Username);
        Assert.Equal("alice", second!.Username);
        // Peeking must not consume — a normal Consume should still find it afterwards.
        Assert.NotNull(_manager.ConsumeAuthorizedSession(token!));
    }

    [Fact]
    public void PeekAuthorizedSession_UnknownToken_ReturnsNull()
    {
        Assert.Null(_manager.PeekAuthorizedSession("does-not-exist"));
    }

    [Fact]
    public void PeekAuthorizedSession_ExpiredSession_ReturnsNullAndRemovesIt()
    {
        var session = MakeSession();
        typeof(AuthorizedSession)
            .GetProperty(nameof(AuthorizedSession.CreatedAt))!
            .SetValue(session, DateTimeOffset.UtcNow.AddMinutes(-6));

        var token = _manager.StoreAuthorizedSession(session);

        Assert.Null(_manager.PeekAuthorizedSession(token!));
        // Expired entries are evicted on peek, so a subsequent consume must also miss.
        Assert.Null(_manager.ConsumeAuthorizedSession(token!));
    }

    // ── InvalidateAuthorizedSession ────────────────────────────────────────────

    [Fact]
    public void InvalidateAuthorizedSession_RemovesSession()
    {
        var token = _manager.StoreAuthorizedSession(MakeSession());

        _manager.InvalidateAuthorizedSession(token!);

        Assert.Null(_manager.PeekAuthorizedSession(token!));
    }

    [Fact]
    public void InvalidateAuthorizedSession_UnknownToken_DoesNotThrow()
    {
        _manager.InvalidateAuthorizedSession("does-not-exist");
    }

    // ── OidcState.QuickConnect ─────────────────────────────────────────────────

    [Fact]
    public void ConsumeState_QuickConnectTrue_IsPreserved()
    {
        var state = MakeState(quickConnect: true);
        var key = _manager.StoreState(state);

        var consumed = _manager.ConsumeState(key!);

        Assert.NotNull(consumed);
        Assert.True(consumed!.QuickConnect);
    }

    [Fact]
    public void ConsumeState_QuickConnectDefaultsToFalse()
    {
        var key = _manager.StoreState(MakeState());

        var consumed = _manager.ConsumeState(key!);

        Assert.NotNull(consumed);
        Assert.False(consumed!.QuickConnect);
    }

    // ── Lifecycle ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task StartStop_DoesNotThrow()
    {
        await _manager.StartAsync(CancellationToken.None);
        await _manager.StopAsync(CancellationToken.None);
    }

    // ── helpers ────────────────────────────────────────────────────────────────

    private static OidcState MakeState(string providerId = "test-provider", bool quickConnect = false) => new()
    {
        ProviderId = providerId,
        Nonce = Guid.NewGuid().ToString("N"),
        CodeVerifier = Guid.NewGuid().ToString("N"),
        RedirectUri = "https://jellyfin.example.com/sso/OIDC/Callback/test",
        CsrfToken = Guid.NewGuid().ToString("N"),
        QuickConnect = quickConnect
    };

    private static AuthorizedSession MakeSession(
        string username = "user", string providerId = "provider", string? pictureUrl = null) => new()
    {
        ProviderId = providerId,
        Username = username,
        DisplayName = username,
        PictureUrl = pictureUrl,
        Roles = []
    };
}
