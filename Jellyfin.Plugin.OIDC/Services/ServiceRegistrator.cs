using System.Net;
using Jellyfin.Data.Events.Users;
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

        // Own JSON file, not plugin config; hosted so it migrates legacy in-config rows and flushes on shutdown.
        serviceCollection.AddSingleton<UserProviderMapStore>();
        serviceCollection.AddHostedService(sp => sp.GetRequiredService<UserProviderMapStore>());

        // Injected (not static) so tests can substitute a mock-backed HttpClient.
        serviceCollection.AddSingleton<Func<IPAddress, bool, HttpClient>>(_ => AuthorityGuard.CreatePinnedHttpClient);
        serviceCollection.AddSingleton<GuardedHttpClientFactory>();
        serviceCollection.AddSingleton<OidcProtocolService>();
        serviceCollection.AddSingleton<ClaimsResolver>();
        serviceCollection.AddSingleton<LoginFlowService>();

        serviceCollection.AddScoped<RbacService>();
        serviceCollection.AddScoped<ProfileImageService>();
        serviceCollection.AddScoped<UserSyncService>();
        serviceCollection.AddScoped<IEventConsumer<SessionEndedEventArgs>, OidcSessionEndedConsumer>();
        serviceCollection.AddScoped<IEventConsumer<UserDeletedEventArgs>, OidcUserDeletedConsumer>();
    }
}
