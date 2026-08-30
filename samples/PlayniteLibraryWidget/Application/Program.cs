using WidgetRail.Samples.PlayniteLibrary;
using WidgetRail.WidgetRuntime;

var paths = PlayniteLibraryApplicationPaths.CreateDefault();

return await WidgetApplicationBootstrap.RunAsync(args, () =>
{
    var service = new PlayniteLibraryApplicationService(
        PlayniteBridgeClient.CreateDefault(),
        new PlayniteLibraryStateFileStore(paths.StateFile));
    return new PlayniteLibraryWidget(service);
});
