using WidgetRail.Samples.SpotifyWidget;
using WidgetRail.WidgetRuntime;
using WidgetRail.WindowsSpotifyProvider;

const string publisherId = "widgetrail.samples";
const string packageId = "widgetrail.samples.spotify";

return await WidgetApplicationBootstrap.RunAsync(args, () =>
{
    var identity = new SpotifyIntegrationIdentity(publisherId, packageId);
    var configuration = SpotifyClientConfigurationFileStore.CreateDefault();
    var backend = new WindowsSpotifyPlatformBackend(configuration);
    return new SpotifyWidget(new SpotifyApplicationService(backend, identity));
});
