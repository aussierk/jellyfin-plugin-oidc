using System.Threading.Tasks;
using MediaBrowser.Controller.Events;
using MediaBrowser.Controller.Events.Session;

namespace Jellyfin.Plugin.OIDC.Services;

/// <summary>
/// Drops a tracked OIDC↔Jellyfin session correlation when the Jellyfin session ends
/// (normal sign-out, admin revoke, expiry) so the back-channel-logout table stays small
/// and never holds a stale device id.
/// </summary>
public sealed class OidcSessionEndedConsumer : IEventConsumer<SessionEndedEventArgs>
{
    private readonly StateManager _stateManager;

    public OidcSessionEndedConsumer(StateManager stateManager) => _stateManager = stateManager;

    public Task OnEvent(SessionEndedEventArgs eventArgs)
    {
        var session = eventArgs.Argument;
        if (session != null)
        {
            _stateManager.UntrackBySessionId(session.Id);
            if (!string.IsNullOrEmpty(session.DeviceId))
            {
                _stateManager.UntrackByDeviceId(session.DeviceId);
            }
        }

        return Task.CompletedTask;
    }
}
