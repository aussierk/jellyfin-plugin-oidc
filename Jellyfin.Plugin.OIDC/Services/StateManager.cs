using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.OIDC.Services;

public sealed class OidcState
{
    public required string ProviderId { get; init; }
    public required string Nonce { get; init; }
    public required string CodeVerifier { get; init; }
    public required string RedirectUri { get; init; }
    public required string CsrfToken { get; init; }

    /// When true, the callback drives Quick Connect instead of a web-client localStorage login.
    public bool QuickConnect { get; init; }

    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
}

public sealed class AuthorizedSession
{
    public required string ProviderId { get; init; }
    public required string Username { get; init; }
    public string? DisplayName { get; init; }
    public string? PictureUrl { get; init; }
    public required string[] Roles { get; init; }

    /// The OIDC <c>sub</c> claim - the stable identity key.
    public string? Subject { get; init; }

    /// The OIDC <c>sid</c> (session id) claim, when the IdP issues one. Used to target back-channel logout.
    public string? Sid { get; init; }

    /// The token issuer, carried so a back-channel logout token can be correlated to the right provider.
    public string? Issuer { get; init; }

    /// The <c>email</c> claim value, when present.
    public string? Email { get; init; }

    /// True when the token asserted <c>email_verified</c>.
    public bool EmailVerified { get; init; }

    /// <summary>
    /// Set once the Jellyfin user has been provisioned/synced for this session (Quick Connect
    /// peeks the session and retries on a mistyped code - this lets the retry skip re-running
    /// SyncUser + RBAC). Mutable because the session object is reused across peeks.
    /// </summary>
    public Guid? SyncedUserId { get; set; }

    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
}

/// 
/// A minted session correlated to its OIDC identity for back-channel logout targeting.
/// In-memory only, so a Jellyfin restart drops every entry here. That's fine for a bare
/// <c>sub</c> logout (resolved straight from <see cref="Configuration.PluginConfiguration.UserProviderMap"/>,
/// which is persisted), but a <c>sid</c>-only <c>logout_token</c> has no <c>sub</c> to fall
/// back to - see <see cref="Configuration.UserProviderEntry.LogoutSid"/>, which is what
/// actually keeps a sid-scoped logout resolvable across a restart, independently of this table.
/// 
public sealed class TrackedSession
{
    public required string ProviderId { get; init; }
    public required string Issuer { get; init; }
    public required string Subject { get; init; }

    /// The OIDC <c>sid</c> claim for this in-memory record - not the persisted one; see <see cref="Configuration.UserProviderEntry.LogoutSid"/>.
    public string? Sid { get; init; }
    public required Guid UserId { get; init; }
    public required string DeviceId { get; init; }
    public required string SessionId { get; init; }
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
}

public sealed class StateManager : IHostedService, IDisposable
{
    internal static readonly TimeSpan StateExpiry = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan SessionExpiry = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan CleanupInterval = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan TrackedSessionMaxAge = TimeSpan.FromDays(90);

    // Hard caps prevent unbounded memory growth from unauthenticated flood attacks.
    private const int MaxPendingStates = 500;
    private const int MaxAuthorizedSessions = 200;
    private const int MaxTrackedSessions = 5000;
    private const int MaxSeenJti = 5000;

    private readonly ConcurrentDictionary<string, OidcState> _pendingStates = new();
    private readonly ConcurrentDictionary<string, AuthorizedSession> _authorizedSessions = new();
    private readonly ConcurrentDictionary<string, TrackedSession> _trackedSessions = new();

    // logout-token jti -> when it's safe to forget (token expiry + skew), so replay stays blocked for its full validity window.
    private readonly ConcurrentDictionary<string, DateTimeOffset> _seenJti = new();
    private readonly ILogger<StateManager> _logger;
    private Timer? _cleanupTimer;

    public StateManager(ILogger<StateManager> logger)
    {
        _logger = logger;
    }

    public string? StoreState(OidcState state)
    {
        if (_pendingStates.Count >= MaxPendingStates
            && SampledEviction.EvictSampled(_pendingStates, s => s.CreatedAt))
        {
            // Oldest pending state is past StateExpiry anyway; evicting it beats rejecting a real login.
            _logger.LogWarning("Pending OIDC state cap ({Max}) reached - evicted oldest pending state", MaxPendingStates);
        }

        var key = Guid.NewGuid().ToString("N");
        _pendingStates[key] = state;
        return key;
    }

    public OidcState? ConsumeState(string stateKey)
    {
        if (!_pendingStates.TryRemove(stateKey, out var state))
        {
            return null;
        }

        if (DateTimeOffset.UtcNow - state.CreatedAt > StateExpiry)
        {
            _logger.LogWarning("OIDC state expired for provider {ProviderId}", state.ProviderId);
            return null;
        }

        return state;
    }

    public string? StoreAuthorizedSession(AuthorizedSession session)
    {
        if (_authorizedSessions.Count >= MaxAuthorizedSessions)
        {
            _logger.LogWarning("Authorized session cap ({Max}) reached - rejecting new session", MaxAuthorizedSessions);
            return null;
        }

        var token = Guid.NewGuid().ToString("N");
        _authorizedSessions[token] = session;
        return token;
    }

    public AuthorizedSession? ConsumeAuthorizedSession(string token)
    {
        if (!_authorizedSessions.TryRemove(token, out var session))
        {
            return null;
        }

        if (DateTimeOffset.UtcNow - session.CreatedAt > SessionExpiry)
        {
            _logger.LogWarning("Authorized session expired for user {Username}", session.Username);
            return null;
        }

        return session;
    }

    /// Reads without removing, so a caller can retry (e.g. a mistyped Quick Connect code). Invalidate explicitly when done.
    public AuthorizedSession? PeekAuthorizedSession(string token)
    {
        if (!_authorizedSessions.TryGetValue(token, out var session))
        {
            return null;
        }

        if (DateTimeOffset.UtcNow - session.CreatedAt > SessionExpiry)
        {
            _authorizedSessions.TryRemove(token, out _);
            _logger.LogWarning("Authorized session expired for user {Username}", session.Username);
            return null;
        }

        return session;
    }

    public void InvalidateAuthorizedSession(string token)
    {
        _authorizedSessions.TryRemove(token, out _);
    }

    public void TrackSession(TrackedSession session)
    {
        if (_trackedSessions.Count >= MaxTrackedSessions)
        {
            SampledEviction.EvictSampled(_trackedSessions, s => s.CreatedAt);
        }

        _trackedSessions[session.SessionId] = session;
    }

    // Callers pass whichever id might be the key (session id, or the device-id fallback); a miss is a no-op.
    public void UntrackBySessionId(string sessionId) => _trackedSessions.TryRemove(sessionId, out _);

    // Self-enforcing precedence (sid, when present, wins outright) rather than relying on every
    // caller to null out sub whenever sid is set - a caller passing both would otherwise silently
    // widen a sid-scoped lookup to every session sharing that subject.
    public IReadOnlyList<TrackedSession> FindTracked(string issuer, string? sub, string? sid)
    {
        if (string.IsNullOrEmpty(sid) && string.IsNullOrEmpty(sub))
        {
            return Array.Empty<TrackedSession>();
        }

        return _trackedSessions.Values.Where(s =>
                string.Equals(s.Issuer, issuer, StringComparison.Ordinal)
                && (!string.IsNullOrEmpty(sid)
                    ? string.Equals(s.Sid, sid, StringComparison.Ordinal)
                    : string.Equals(s.Subject, sub, StringComparison.Ordinal)))
            .ToList();
    }

    /// Records a logout-token <c>jti</c>; returns false if already seen (replay).
    public bool RegisterJti(string jti, DateTimeOffset forgetAfter)
    {
        if (string.IsNullOrEmpty(jti))
        {
            return false;
        }

        if (_seenJti.Count >= MaxSeenJti)
        {
            // Evict the entry expiring soonest - it was about to be cleaned up anyway.
            SampledEviction.EvictSampled(_seenJti, forgetAt => forgetAt);
        }

        return _seenJti.TryAdd(jti, forgetAfter);
    }

    /// Undoes <see cref="RegisterJti"/> so the token can be retried - for when the logout that
    /// consumed it revoked nothing because every attempt errored.
    public void UnregisterJti(string jti)
    {
        if (!string.IsNullOrEmpty(jti))
        {
            _seenJti.TryRemove(jti, out _);
        }
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _cleanupTimer = new Timer(Cleanup, null, CleanupInterval, CleanupInterval);
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _cleanupTimer?.Change(Timeout.Infinite, 0);
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        _cleanupTimer?.Dispose();
    }

    private void Cleanup(object? state)
    {
        var now = DateTimeOffset.UtcNow;

        foreach (var (key, oidcState) in _pendingStates)
        {
            if (now - oidcState.CreatedAt > StateExpiry)
            {
                _pendingStates.TryRemove(key, out _);
            }
        }

        foreach (var (key, session) in _authorizedSessions)
        {
            if (now - session.CreatedAt > SessionExpiry)
            {
                _authorizedSessions.TryRemove(key, out _);
            }
        }

        foreach (var (key, tracked) in _trackedSessions)
        {
            if (now - tracked.CreatedAt > TrackedSessionMaxAge)
            {
                _trackedSessions.TryRemove(key, out _);
            }
        }

        foreach (var (jti, forgetAfter) in _seenJti)
        {
            if (now > forgetAfter)
            {
                _seenJti.TryRemove(jti, out _);
            }
        }
    }
}
