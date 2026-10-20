using WidgetRail.Samples.PlayniteLibrary;
using WidgetRail.WidgetRuntime;

var paths = PlayniteLibraryApplicationPaths.CreateDefault();
var diagnostics = PlayniteLibraryApplicationDiagnostics.CreateDefault();

return await WidgetApplicationBootstrap.RunAsync(args, () =>
{
    var service = new PlayniteLibraryApplicationService(
        PlayniteBridgeClient.CreateDefault(),
        new PlayniteLibraryStateFileStore(paths.StateFile),
        diagnostics);
    return new PlayniteLibraryWidget(service, setupService: new PlayniteSetupService(paths.Root,
        Path.Combine(AppContext.BaseDirectory, "bridge")));
});
