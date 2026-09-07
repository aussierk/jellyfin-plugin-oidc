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
        var expired = new OidcState
        {
            ProviderId = "p",
            Nonce = "n",
            CodeVerifier = "cv",
            RedirectUri = "https://example.com/callback",
            CsrfToken = "csrf",
        };
        typeof(OidcState)
            .GetProperty(nameof(OidcState.CreatedAt))!
            .SetValue(expired, DateTimeOffset.UtcNow.AddMinutes(-11));

        var key = _manager.StoreState(expired);
        Assert.Null(_manager.ConsumeState(key!));
    }

    [Fact]
    public void StoreState_AtCap_EvictsSomethingAndStillSucceeds()
    {
        // Mirrors TrackSession/RegisterJti: at the cap, a real login must still get through by
        // evicting some entry rather than rejecting it outright. Eviction samples (see
        // SampledEviction) instead of scanning every entry, so this asserts something was evicted
        // to make room - not that it was specifically the single oldest of the 500.
        var keys = new List<string>();
        for (var i = 0; i < 500; i++)
        {
            keys.Add(_manager.StoreState(MakeState())!);
        }

        var newKey = _manager.StoreState(MakeState());

        Assert.NotNull(newKey);
        Assert.NotNull(_manager.ConsumeState(newKey!));
        Assert.Contains(keys, k => _manager.ConsumeState(k) == null);
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
        // Peeking must not consume - a normal Consume should still find it afterwards.
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

    // ── Back-channel logout: jti replay guard ─────────────────────────────────

    [Fact]
    public void RegisterJti_FirstUse_ReturnsTrue()
        => Assert.True(_manager.RegisterJti("jti-1", DateTimeOffset.UtcNow.AddMinutes(10)));

    [Fact]
    public void RegisterJti_SameJti_ReturnsFalse()
    {
        var expiry = DateTimeOffset.UtcNow.AddMinutes(10);
        Assert.True(_manager.RegisterJti("jti-replay", expiry));
        Assert.False(_manager.RegisterJti("jti-replay", expiry));
    }

    [Fact]
    public void RegisterJti_EmptyJti_ReturnsFalse()
        => Assert.False(_manager.RegisterJti("", DateTimeOffset.UtcNow.AddMinutes(10)));

    [Fact]
    public void UnregisterJti_AllowsTheTokenToBeRegisteredAgain()
    {
        var expiry = DateTimeOffset.UtcNow.AddMinutes(10);
        Assert.True(_manager.RegisterJti("jti-retry", expiry));

        _manager.UnregisterJti("jti-retry");

        // A back-channel logout whose revocation errored frees the jti so the IdP's retry works.
        Assert.True(_manager.RegisterJti("jti-retry", expiry));
    }

    // ── Back-channel logout: session correlation table ────────────────────────

    [Fact]
    public void FindTracked_BySid_ReturnsMatchingSession()
    {
        _manager.TrackSession(MakeTracked(sessionId: "s1", sid: "sid-1", subject: "sub-1"));

        var hits = _manager.FindTracked("https://idp.example.com", sub: null, sid: "sid-1");

        Assert.Single(hits);
        Assert.Equal("s1", hits[0].SessionId);
    }

    [Fact]
    public void FindTracked_BySub_ReturnsAllOfThatSubjectsSessions()
    {
        _manager.TrackSession(MakeTracked(sessionId: "s1", sid: "sid-1", subject: "sub-1"));
        _manager.TrackSession(MakeTracked(sessionId: "s2", sid: "sid-2", subject: "sub-1"));
        _manager.TrackSession(MakeTracked(sessionId: "s3", sid: "sid-3", subject: "sub-2"));

        var hits = _manager.FindTracked("https://idp.example.com", sub: "sub-1", sid: null);

        Assert.Equal(2, hits.Count);
    }

    [Fact]
    public void FindTracked_WrongIssuer_ReturnsEmpty()
    {
        _manager.TrackSession(MakeTracked(sessionId: "s1", sid: "sid-1", subject: "sub-1"));

        Assert.Empty(_manager.FindTracked("https://other.example.com", sub: "sub-1", sid: "sid-1"));
    }

    [Fact]
    public void FindTracked_NeitherSubNorSid_ReturnsEmpty()
    {
        _manager.TrackSession(MakeTracked(sessionId: "s1", sid: "sid-1", subject: "sub-1"));

        Assert.Empty(_manager.FindTracked("https://idp.example.com", sub: null, sid: null));
    }

    [Fact]
    public void UntrackBySessionId_RemovesEntry()
    {
        _manager.TrackSession(MakeTracked(sessionId: "s1", sid: "sid-1", subject: "sub-1"));

        _manager.UntrackBySessionId("s1");

        Assert.Empty(_manager.FindTracked("https://idp.example.com", sub: "sub-1", sid: "sid-1"));
    }

    [Fact]
    public void UntrackBySessionId_LeavesOtherSessionsOnSameDeviceIntact()
    {
        // Regression: OidcSessionEndedConsumer used to also call a device-id field-scan removal
        // that dropped every tracked entry sharing a device id - killing back-channel-logout
        // correlation for a still-live second session on the same device. Precise key-based
        // removal (what the consumer does now) must not have that effect.
        _manager.TrackSession(MakeTracked(sessionId: "s1", sid: "sid-1", subject: "sub-1", deviceId: "dev-A"));
        _manager.TrackSession(MakeTracked(sessionId: "s2", sid: "sid-2", subject: "sub-1", deviceId: "dev-A"));

        _manager.UntrackBySessionId("s1");

        var hits = _manager.FindTracked("https://idp.example.com", sub: "sub-1", sid: "sid-2");
        Assert.Single(hits);
        Assert.Equal("s2", hits[0].SessionId);
    }

    [Fact]
    public void UntrackBySessionId_RemovesEntryKeyedByDeviceIdFallback()
    {
        // TrackMintedSession keys a TrackedSession by SessionId ?? DeviceId - when the real
        // session id wasn't available, the entry is keyed by device id instead. The consumer
        // relies on UntrackBySessionId(deviceId) to catch exactly that case.
        _manager.TrackSession(MakeTracked(sessionId: "dev-A", sid: "sid-1", subject: "sub-1", deviceId: "dev-A"));

        _manager.UntrackBySessionId("dev-A");

        Assert.Empty(_manager.FindTracked("https://idp.example.com", sub: "sub-1", sid: "sid-1"));
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

    private static TrackedSession MakeTracked(
        string sessionId,
        string sid,
        string subject,
        string issuer = "https://idp.example.com",
        string deviceId = "device-1") => new()
    {
        ProviderId = "provider",
        Issuer = issuer,
        Subject = subject,
        Sid = sid,
        UserId = Guid.NewGuid(),
        DeviceId = deviceId,
        SessionId = sessionId
    };
}
