using GameBarAlternative.Samples.SpotifyWidget;
using GameBarAlternative.WidgetRuntime;
using GameBarAlternative.WindowsSpotifyProvider;

const string publisherId = "org.gbar.samples";
const string packageId = "org.gbar.samples.spotify";

return await WidgetApplicationBootstrap.RunAsync(args, () =>
{
    var identity = new SpotifyIntegrationIdentity(publisherId, packageId);
    var configuration = SpotifyClientConfigurationFileStore.CreateDefault();
    var backend = new WindowsSpotifyPlatformBackend(configuration);
    return new SpotifyWidget(new SpotifyApplicationService(backend, identity));
});
