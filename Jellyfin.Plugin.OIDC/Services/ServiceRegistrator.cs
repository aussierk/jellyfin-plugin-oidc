using MediaBrowser.Controller;
using MediaBrowser.Controller.Authentication;
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

        // Dedicated client for profile-image downloads: redirects disabled so a validated
        // picture URL can't be used to bounce the request to an unvalidated internal host.
        serviceCollection.AddHttpClient("OidcPluginImage")
            .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false });

        serviceCollection.AddScoped<RbacService>();
        serviceCollection.AddScoped<ProfileImageService>();
        serviceCollection.AddScoped<UserSyncService>();
    }
}
