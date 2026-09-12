using WidgetRail.Samples.SpotifyWidget;
using WidgetRail.WidgetRuntime;
using WidgetRail.WindowsSpotifyProvider;

const string publisherId = "widgetrail.samples";
const string packageId = "widgetrail.samples.spotify";

var diagnostics = SpotifyApplicationDiagnostics.CreateDefault();
AppDomain.CurrentDomain.UnhandledException += (_, eventArgs) =>
    diagnostics.Record(
        "unhandled-exception",
        eventArgs.ExceptionObject is Exception exception
            ? SpotifyRuntimeDiagnostics.Code(exception)
            : "unknown-failure");
TaskScheduler.UnobservedTaskException += (_, eventArgs) =>
{
    diagnostics.Record(
        "unobserved-task",
        SpotifyRuntimeDiagnostics.Code(eventArgs.Exception));
    eventArgs.SetObserved();
};

var exitCode = await WidgetApplicationBootstrap.RunAsync(args, () =>
{
    var identity = new SpotifyIntegrationIdentity(publisherId, packageId);
    var configuration = SpotifyClientConfigurationFileStore.CreateDefault();
    var backend = new WindowsSpotifyPlatformBackend(configuration, diagnostics);
    return new SpotifyWidget(
        new SpotifyApplicationService(backend, identity),
        timeProvider: null,
        runtimeDiagnostics: diagnostics,
        playlistCacheRoot: SpotifyPlaylistDiskCache.DefaultRoot());
});
if (exitCode != 0) diagnostics.Record("worker-session", "exit-1");
return exitCode;
