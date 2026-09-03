using System.Net;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Authentication;
using MediaBrowser.Controller.Events;
using MediaBrowser.Controller.Events.Session;
using MediaBrowser.Controller.Plugins;
using Microsoft.Extensions.DependencyInjection;

namespace Jellyfin.Plugin.OIDC.Services;

public class ServiceRegistrator : IPluginServiceRegistrator
{
    public void RegisterServices(IServiceCollection serviceCollection, IServerApplicationHost applicationHost)
    {
        serviceCollection.AddSingleton<IAuthenticationProvider, Auth.OidcAuthProvider>();
        serviceCollection.AddSingleton<StateManager>();
        serviceCollection.AddHostedService(sp => sp.GetRequiredService<StateManager>());

        // Fallback for profile-image downloads when AuthorityGuard can't resolve a pinned
        // address (malformed URL / DNS failure) — redirects still disabled so a validated
        // picture URL can't be used to bounce the request to an unvalidated internal host.
        serviceCollection.AddHttpClient("OidcPluginImage")
            .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false });

        // Injected (rather than called statically) so tests can substitute a mock-backed
        // HttpClient in place of a real pinned socket connection. The bool selects whether
        // redirects are followed — false for the profile-picture fetch (its trust boundary is
        // stricter: a redirect target is unvalidated), true (the default) for discovery fetches.
        serviceCollection.AddSingleton<Func<IPAddress, bool, HttpClient>>(_ => AuthorityGuard.CreatePinnedHttpClient);

        serviceCollection.AddScoped<RbacService>();
        serviceCollection.AddScoped<ProfileImageService>();
        serviceCollection.AddScoped<UserSyncService>();

        // Prunes the back-channel-logout session table when a Jellyfin session ends.
        serviceCollection.AddScoped<IEventConsumer<SessionEndedEventArgs>, OidcSessionEndedConsumer>();
    }
}
