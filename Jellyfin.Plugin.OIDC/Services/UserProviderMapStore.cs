using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.OIDC.Configuration;
using MediaBrowser.Common.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.OIDC.Services;

/// <summary>
/// The OIDC identity → Jellyfin account map, persisted to its own small JSON file next to the
/// plugin config rather than inside it, so a login that writes a row serializes just this file
/// and does so off the request path (debounced). Owns its own lock: every read returns a copy
/// and every write is one atomic lock-mutate-persist, so callers can't forget to synchronize.
/// On first run it migrates any rows still in <see cref="PluginConfiguration.UserProviderMap"/>.
/// </summary>
public sealed class UserProviderMapStore : IHostedService
{
    private const string FileName = "Jellyfin.Plugin.OIDC.UserProviderMap.json";
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };
    private static readonly TimeSpan FlushDelay = TimeSpan.FromSeconds(5);

    private readonly string? _path;
    private readonly ILogger<UserProviderMapStore> _logger;
    private readonly object _gate = new();
    private readonly List<UserProviderEntry> _entries;
    private readonly bool _fileExisted;

    private readonly object _flushLock = new();
    private bool _flushPending;
    private bool _flushScheduled;

    // Serializes the actual disk write below - PruneUser (synchronous), the debounced background
    // flush, and MigrateFromConfig (startup) can all call WriteFile independently of each other and
    // of _gate (which only protects _entries). Without this, two overlapping writers race on the
    // same fixed ".tmp" path: whichever's File.Move loses finds the temp file already gone and the
    // write is silently dropped (logged as an error). One lock per store instance is enough since
    // they never span multiple files.
    private readonly object _fileWriteLock = new();

    public UserProviderMapStore(IApplicationPaths applicationPaths, ILogger<UserProviderMapStore> logger)
        : this(Path.Combine(applicationPaths.PluginConfigurationsPath, FileName), logger)
    {
    }

    internal UserProviderMapStore(string filePath, ILogger<UserProviderMapStore> logger)
    {
        _path = filePath;
        _logger = logger;
        CleanupTempFile();
        (_entries, _fileExisted) = Load();
    }

    /// Test-only: wraps an existing list with no disk persistence and no migration.
    internal UserProviderMapStore(List<UserProviderEntry> entries, ILogger<UserProviderMapStore> logger)
    {
        _path = null;
        _logger = logger;
        _entries = entries;
        _fileExisted = true;
    }

    /// <summary>Row count (for status/observability).</summary>
    public int Count
    {
        get
        {
            lock (_gate)
            {
                return _entries.Count;
            }
        }
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        MigrateFromConfig();
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        FlushPending();
        return Task.CompletedTask;
    }

    /// The row for this exact (provider, subject) identity, or null.
    public UserProviderEntry? FindBySubject(string providerId, string subject)
        => Find(e => string.Equals(e.ProviderId, providerId, StringComparison.OrdinalIgnoreCase)
                     && string.Equals(e.Subject, subject, StringComparison.Ordinal));

    /// Any row previously stored with this verified email (across providers), or null.
    public UserProviderEntry? FindByVerifiedEmail(string email)
        => Find(e => e.EmailVerified && string.Equals(e.Email, email, StringComparison.OrdinalIgnoreCase));

    /// The row for a back-channel logout that matched no live session: by the persisted sid first
    /// (a sid-scoped logout must not widen to every row for the subject), then subject, then a
    /// legacy row's username.
    public UserProviderEntry? ResolveForLogout(string providerId, string sub, string? sid, string? username)
    {
        lock (_gate)
        {
            UserProviderEntry? e = null;
            if (!string.IsNullOrEmpty(sid))
            {
                e = _entries.FirstOrDefault(x =>
                    string.Equals(x.ProviderId, providerId, StringComparison.OrdinalIgnoreCase)
                    && !string.IsNullOrEmpty(x.LogoutSid)
                    && string.Equals(x.LogoutSid, sid, StringComparison.Ordinal));
            }

            if (e == null && !string.IsNullOrEmpty(sub))
            {
                e = _entries.FirstOrDefault(x =>
                    string.Equals(x.ProviderId, providerId, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(x.Subject, sub, StringComparison.Ordinal));
            }

            if (e == null && !string.IsNullOrEmpty(username))
            {
                e = _entries.FirstOrDefault(x =>
                    string.Equals(x.ProviderId, providerId, StringComparison.OrdinalIgnoreCase)
                    && string.IsNullOrEmpty(x.Subject)
                    && string.Equals(x.Username, username, StringComparison.OrdinalIgnoreCase));
            }

            return e == null ? null : Copy(e);
        }
    }

    /// A point-in-time copy of every row.
    public IReadOnlyList<UserProviderEntry> Snapshot()
    {
        lock (_gate)
        {
            return _entries.Select(Copy).ToList();
        }
    }

    /// <summary>
    /// A fresh ownership row. A verified, non-empty email is kept (it may later be a link
    /// target - see <see cref="FindByVerifiedEmail"/>); anything else is dropped so an
    /// unverified address never becomes one. The single place that rule lives.
    /// </summary>
    public static UserProviderEntry CreateEntry(
        string username, string providerId, string subject, string userId, string email, bool emailVerified)
    {
        var storeEmail = emailVerified && email.Length > 0;
        return new UserProviderEntry
        {
            Username = username,
            ProviderId = providerId,
            Subject = subject,
            UserId = userId,
            Email = storeEmail ? email : string.Empty,
            EmailVerified = storeEmail
        };
    }

    /// <summary>
    /// Establishes ownership for a first login / local-account migration: drops any row that
    /// collides on username or on (subject, provider), then adds a fresh row. Use this only when
    /// there is no existing row to preserve - it does not carry over <c>LogoutSid</c>.
    /// </summary>
    public void Upsert(UserProviderEntry entry)
    {
        lock (_gate)
        {
            _entries.RemoveAll(e =>
                string.Equals(e.Username, entry.Username, StringComparison.OrdinalIgnoreCase)
                || (!string.IsNullOrEmpty(entry.Subject)
                    && string.Equals(e.Subject, entry.Subject, StringComparison.Ordinal)
                    && string.Equals(e.ProviderId, entry.ProviderId, StringComparison.OrdinalIgnoreCase)));
            _entries.Add(Copy(entry));
        }

        SaveDebounced();
    }

    /// <summary>
    /// Binds an existing-Jellyfin-user OIDC login to its map row, atomically: finds the owning row
    /// (by subject, else username), and - all under one lock -
    /// <list type="bullet">
    /// <item>rejects the login (<see cref="MapBindOutcome.Conflict"/>, no mutation) when the row is
    /// already owned by a different provider/subject and <paramref name="trustLink"/> is false;</item>
    /// <item>adds a fresh row when none exists (<see cref="MapBindOutcome.Created"/>);</item>
    /// <item>re-points the row at this identity when a field changed - IdP switch, legacy back-fill,
    /// email refresh - preserving <c>LogoutSid</c> (<see cref="MapBindOutcome.Rebound"/>);</item>
    /// <item>does nothing when the row is already correct (<see cref="MapBindOutcome.Unchanged"/>).</item>
    /// </list>
    /// The result carries the mismatch flags and previous provider so the caller can log outside
    /// the lock. Persists (debounced) only when a row was added or changed.
    /// </summary>
    public MapBindResult BindOidcLogin(
        string providerId, string subject, string username, string userId, string email, bool emailVerified, bool trustLink)
    {
        MapBindResult result;
        var write = false;

        lock (_gate)
        {
            var e = _entries.FirstOrDefault(x =>
                (subject.Length > 0 && string.Equals(x.Subject, subject, StringComparison.Ordinal))
                || string.Equals(x.Username, username, StringComparison.OrdinalIgnoreCase));

            var providerMismatch = e != null && !string.IsNullOrEmpty(e.ProviderId)
                                   && !string.Equals(e.ProviderId, providerId, StringComparison.OrdinalIgnoreCase);
            var subjectMismatch = e != null && !string.IsNullOrEmpty(e.Subject) && subject.Length > 0
                                  && !string.Equals(e.Subject, subject, StringComparison.Ordinal);
            var mismatch = providerMismatch || subjectMismatch;
            var previousProvider = e?.ProviderId ?? string.Empty;

            // Never repoint a row already bound to a different Jellyfin user; not even trustLink
            // overrides this, or one account's login would be handed to another identity.
            var boundToDifferentUser = e != null && !string.IsNullOrEmpty(e.UserId)
                                       && !string.Equals(e.UserId, userId, StringComparison.Ordinal);

            if (boundToDifferentUser || (mismatch && !trustLink))
            {
                result = new MapBindResult(MapBindOutcome.Conflict, true, providerMismatch, previousProvider);
            }
            else if (e == null)
            {
                _entries.Add(CreateEntry(username, providerId, subject, userId, email, emailVerified));
                write = true;
                result = new MapBindResult(MapBindOutcome.Created, false, false, previousProvider);
            }
            else
            {
                var emailChanged = emailVerified && email.Length > 0
                                   && !string.Equals(e.Email, email, StringComparison.OrdinalIgnoreCase);
                if (!(mismatch || string.IsNullOrEmpty(e.Subject) || string.IsNullOrEmpty(e.UserId) || emailChanged))
                {
                    result = new MapBindResult(MapBindOutcome.Unchanged, false, false, previousProvider);
                }
                else
                {
                    e.ProviderId = providerId;
                    e.Subject = subject.Length > 0 ? subject : e.Subject;
                    e.UserId = userId;
                    e.Username = username;
                    if (emailVerified && email.Length > 0)
                    {
                        e.Email = email;
                        e.EmailVerified = true;
                    }

                    write = true;
                    result = new MapBindResult(MapBindOutcome.Rebound, mismatch, providerMismatch, previousProvider);
                }
            }
        }

        if (write)
        {
            SaveDebounced();
        }

        return result;
    }

    /// <summary>Renames the row for this user (matched by id, else old username). Returns true (and persists) if it changed.</summary>
    public bool Rename(string userId, string oldUsername, string newUsername)
    {
        lock (_gate)
        {
            var e = _entries.FirstOrDefault(x => string.Equals(x.UserId, userId, StringComparison.Ordinal))
                    ?? _entries.FirstOrDefault(x => string.Equals(x.Username, oldUsername, StringComparison.OrdinalIgnoreCase));
            if (e == null || string.Equals(e.Username, newUsername, StringComparison.Ordinal))
            {
                return false;
            }

            e.Username = newUsername;
        }

        SaveDebounced();
        return true;
    }

    /// <summary>Records this login's sid on the (provider, subject) row. Returns true (and persists) if it changed.</summary>
    public bool SetLogoutSid(string providerId, string subject, string sid)
    {
        lock (_gate)
        {
            var e = _entries.FirstOrDefault(x =>
                string.Equals(x.ProviderId, providerId, StringComparison.OrdinalIgnoreCase)
                && string.Equals(x.Subject, subject, StringComparison.Ordinal));
            if (e == null || string.Equals(e.LogoutSid, sid, StringComparison.Ordinal))
            {
                return false;
            }

            e.LogoutSid = sid;
        }

        SaveDebounced();
        return true;
    }

    /// <summary>Removes every row bound to a deleted Jellyfin user. Returns the number removed.</summary>
    public int PruneUser(Guid userId, string username)
    {
        var id = userId.ToString();
        List<UserProviderEntry> snapshot;
        int removed;
        lock (_gate)
        {
            removed = _entries.RemoveAll(e =>
                string.Equals(e.UserId, id, StringComparison.OrdinalIgnoreCase)
                || (string.IsNullOrEmpty(e.UserId)
                    && string.Equals(e.Username, username, StringComparison.OrdinalIgnoreCase)));
            if (removed == 0)
            {
                return 0;
            }

            snapshot = _entries.ToList();
        }

        WriteFile(snapshot);
        return removed;
    }

    /// <summary>Writes the map file now (used by migration and tests; the login path uses <see cref="SaveDebounced"/>).</summary>
    internal void Save() => WriteFile(SnapshotRaw());

    private void SaveDebounced()
    {
        lock (_flushLock)
        {
            _flushPending = true;
            if (_flushScheduled)
            {
                return;
            }

            _flushScheduled = true;
        }

        _ = Task.Run(async () =>
        {
            await Task.Delay(FlushDelay).ConfigureAwait(false);
            lock (_flushLock)
            {
                _flushScheduled = false;
            }

            try
            {
                FlushPending();
            }
            catch (Exception ex)
            {
                // Fire-and-forget boundary - never let a background write fault go unobserved.
                _logger.LogError(ex, "OIDC identity map background flush failed");
            }
        });
    }

    private void FlushPending()
    {
        lock (_flushLock)
        {
            if (!_flushPending)
            {
                return;
            }

            _flushPending = false;
        }

        if (!WriteFile(SnapshotRaw()))
        {
            lock (_flushLock)
            {
                _flushPending = true;
            }
        }
    }

    private List<UserProviderEntry> SnapshotRaw()
    {
        lock (_gate)
        {
            return _entries.ToList();
        }
    }

    private UserProviderEntry? Find(Func<UserProviderEntry, bool> predicate)
    {
        lock (_gate)
        {
            var e = _entries.FirstOrDefault(predicate);
            return e == null ? null : Copy(e);
        }
    }

    private static UserProviderEntry Copy(UserProviderEntry e) => new()
    {
        Username = e.Username,
        ProviderId = e.ProviderId,
        Subject = e.Subject,
        UserId = e.UserId,
        Email = e.Email,
        EmailVerified = e.EmailVerified,
        LogoutSid = e.LogoutSid,
    };

    /// <summary>Serializes <paramref name="snapshot"/> to disk atomically, outside the map lock. Returns false (logged) on failure.</summary>
    private bool WriteFile(List<UserProviderEntry> snapshot)
    {
        if (_path is null)
        {
            return true; // in-memory test store
        }

        lock (_fileWriteLock)
        {
            try
            {
                var tmp = _path + ".tmp";
                File.WriteAllText(tmp, JsonSerializer.Serialize(snapshot, JsonOpts));
                File.Move(tmp, _path, overwrite: true);
                return true;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
            {
                _logger.LogError(ex, "Failed to persist the OIDC identity map to {Path}", _path);
                return false;
            }
        }
    }

    private void CleanupTempFile()
    {
        try
        {
            if (_path is not null)
            {
                File.Delete(_path + ".tmp"); // leftover from a crash mid-write; no-op if absent
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "Could not remove stale OIDC identity map temp file {Path}", _path + ".tmp");
        }
    }

    private (List<UserProviderEntry> Entries, bool Existed) Load()
    {
        try
        {
            if (File.Exists(_path))
            {
                var list = JsonSerializer.Deserialize<List<UserProviderEntry>>(File.ReadAllText(_path), JsonOpts);
                return (list ?? new List<UserProviderEntry>(), true);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            _logger.LogError(
                ex, "Failed to read the OIDC identity map at {Path}; starting empty - rows self-heal on the next login", _path);
        }

        return (new List<UserProviderEntry>(), false);
    }

    private void MigrateFromConfig()
    {
        if (_fileExisted)
        {
            return;
        }

        var legacy = OidcPlugin.Instance?.Configuration?.UserProviderMap;
        if (legacy is not { Count: > 0 })
        {
            return;
        }

        List<UserProviderEntry> snapshot;
        lock (_gate)
        {
            _entries.AddRange(legacy);
            snapshot = _entries.ToList();
        }

        if (!WriteFile(snapshot))
        {
            // Leave the config copy in place; retry on the next startup (file still absent).
            lock (_gate)
            {
                _entries.Clear();
            }

            return;
        }

        legacy.Clear();
        try
        {
            OidcPlugin.Instance?.PersistConfiguration();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            _logger.LogWarning(ex, "Migrated the OIDC identity map but could not rewrite the plugin config to drop the old copy");
        }

        _logger.LogInformation("Migrated {Count} OIDC identity row(s) from the plugin config to {Path}", snapshot.Count, _path);
    }
}

/// What <see cref="UserProviderMapStore.BindOidcLogin"/> did.
public enum MapBindOutcome
{
    /// The row was already correct for this login - nothing written.
    Unchanged,

    /// No row existed; a fresh one was added.
    Created,

    /// An existing row was re-pointed at this identity.
    Rebound,

    /// The row is owned by a different provider/subject and no verified-email link vouched for it - nothing written, login must be denied.
    Conflict,
}

/// <summary>Outcome of a <see cref="UserProviderMapStore.BindOidcLogin"/> call, for the caller to log outside the store lock.</summary>
public readonly record struct MapBindResult(
    MapBindOutcome Outcome,
    bool WasIdentityMismatch,
    bool MismatchOnProvider,
    string PreviousProviderId);
