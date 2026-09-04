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

    /// <summary>
    /// When true, the callback drives Jellyfin Quick Connect (for logging in a native/mobile
    /// app) instead of storing web-client credentials in the browser's localStorage.
    /// </summary>
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

    /// <summary>The OIDC <c>sub</c> claim — the stable identity key.</summary>
    public string? Subject { get; init; }

    /// <summary>The OIDC <c>sid</c> (session id) claim, when the IdP issues one. Used to target back-channel logout.</summary>
    public string? Sid { get; init; }

    /// <summary>The token issuer, carried so a back-channel logout token can be correlated to the right provider.</summary>
    public string? Issuer { get; init; }

    /// <summary>The <c>email</c> claim value, when present.</summary>
    public string? Email { get; init; }

    /// <summary>True when the token asserted <c>email_verified</c>.</summary>
    public bool EmailVerified { get; init; }

    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
}

/// <summary>
/// A minted Jellyfin session correlated to its OIDC identity, so an OIDC back-channel
/// logout token can target it. Held in memory only — after a Jellyfin restart, sid-scoped
/// logout falls back to revoking all of the subject's sessions.
/// </summary>
public sealed class TrackedSession
{
    public required string ProviderId { get; init; }
    public required string Issuer { get; init; }
    public required string Subject { get; init; }
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

    private readonly ConcurrentDictionary<string, OidcState> _pendingStates = new();
    private readonly ConcurrentDictionary<string, AuthorizedSession> _authorizedSessions = new();
    private readonly ConcurrentDictionary<string, TrackedSession> _trackedSessions = new();

    // logout-token jti -> the instant it becomes safe to forget (the token's own expiry
    // plus clock skew). Keeping the entry for the token's whole validity window means a
    // captured logout_token can't be replayed even if the fixed cleanup pass is slow.
    private readonly ConcurrentDictionary<string, DateTimeOffset> _seenJti = new();
    private readonly ILogger<StateManager> _logger;
    private Timer? _cleanupTimer;

    public StateManager(ILogger<StateManager> logger)
    {
        _logger = logger;
    }

    public string? StoreState(OidcState state)
    {
        if (_pendingStates.Count >= MaxPendingStates)
        {
            _logger.LogWarning("Pending OIDC state cap ({Max}) reached — rejecting new auth request", MaxPendingStates);
            return null;
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
            _logger.LogWarning("Authorized session cap ({Max}) reached — rejecting new session", MaxAuthorizedSessions);
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

    /// <summary>
    /// Returns the authorized session without removing it, so a caller can validate it across
    /// multiple attempts (e.g. a mistyped Quick Connect code). Expired sessions are evicted and
    /// return null. Invalidate explicitly with <see cref="InvalidateAuthorizedSession"/> once done.
    /// </summary>
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

    // ── back-channel logout support ───────────────────────────────────────────

    public void TrackSession(TrackedSession session)
    {
        if (_trackedSessions.Count >= MaxTrackedSessions)
        {
            // Evict the oldest entry. Single pass rather than an O(n log n) sort on every
            // insert once the cap is hit (e.g. under a login flood).
            string? oldestKey = null;
            var oldestAt = DateTimeOffset.MaxValue;
            foreach (var (key, s) in _trackedSessions)
            {
                if (s.CreatedAt < oldestAt)
                {
                    oldestAt = s.CreatedAt;
                    oldestKey = key;
                }
            }

            if (oldestKey != null)
            {
                _trackedSessions.TryRemove(oldestKey, out _);
            }
        }

        _trackedSessions[session.SessionId] = session;
    }

    public void UntrackBySessionId(string sessionId) => _trackedSessions.TryRemove(sessionId, out _);

    public void UntrackByDeviceId(string deviceId)
    {
        foreach (var (key, s) in _trackedSessions)
        {
            if (string.Equals(s.DeviceId, deviceId, StringComparison.Ordinal))
            {
                _trackedSessions.TryRemove(key, out _);
            }
        }
    }

    public IReadOnlyList<TrackedSession> FindTracked(string issuer, string? sub, string? sid)
        => _trackedSessions.Values.Where(s =>
                string.Equals(s.Issuer, issuer, StringComparison.Ordinal)
                && ((!string.IsNullOrEmpty(sid) && string.Equals(s.Sid, sid, StringComparison.Ordinal))
                    || (!string.IsNullOrEmpty(sub) && string.Equals(s.Subject, sub, StringComparison.Ordinal))))
            .ToList();

    /// <summary>
    /// Records a logout-token <c>jti</c>; returns false if it was already seen (replay).
    /// <paramref name="forgetAfter"/> is when the entry may be cleaned up — pass the token's
    /// own expiry (plus skew) so the guard covers the whole window the token is valid for.
    /// </summary>
    public bool RegisterJti(string jti, DateTimeOffset forgetAfter)
        => !string.IsNullOrEmpty(jti) && _seenJti.TryAdd(jti, forgetAfter);

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
