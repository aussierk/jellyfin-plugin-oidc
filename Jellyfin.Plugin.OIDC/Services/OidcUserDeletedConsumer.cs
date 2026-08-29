using System.Threading.Tasks;
using Jellyfin.Data.Events.Users;
using MediaBrowser.Controller.Events;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.OIDC.Services;

/// Removes a deleted Jellyfin user's rows from the OIDC identity map. Without this the map only
/// grows - every login's <c>FirstOrDefault</c> scan would keep walking rows for accounts that no
/// longer exist.
public sealed class OidcUserDeletedConsumer : IEventConsumer<UserDeletedEventArgs>
{
    private readonly UserProviderMapStore _mapStore;
    private readonly ILogger<OidcUserDeletedConsumer> _logger;

    public OidcUserDeletedConsumer(UserProviderMapStore mapStore, ILogger<OidcUserDeletedConsumer> logger)
    {
        _mapStore = mapStore;
        _logger = logger;
    }

    public Task OnEvent(UserDeletedEventArgs eventArgs)
    {
        var user = eventArgs.Argument;
        if (user == null)
        {
            return Task.CompletedTask;
        }

        var removed = _mapStore.PruneUser(user.Id, user.Username);
        if (removed > 0)
        {
            _logger.LogInformation(
                "OIDC: pruned {Count} identity map row(s) for deleted Jellyfin user {UserId}", removed, user.Id);
        }

        return Task.CompletedTask;
    }
}
