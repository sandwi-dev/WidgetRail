using WidgetRail.Samples.PlayniteLibrary;
using WidgetRail.WidgetRuntime;
using WidgetRail.WindowsAppLibraryProvider;
using PackageAppLibraryProvider =
    WidgetRail.WindowsAppLibraryProvider.WindowsAppLibraryProvider;

var paths = PlayniteLibraryApplicationPaths.CreateDefault();
var sources = await PlayniteLibrarySourceConfiguration.LoadAsync(paths);

return await WidgetApplicationBootstrap.RunAsync(args, () =>
{
    var provider = new PackageAppLibraryProvider(
        _ => sources.EpicInstalledGamesEnabled,
        _ => sources.GogInstalledGamesEnabled);
    var service = new PlayniteLibraryApplicationService(
        provider,
        new PlayniteLibrarySavedIdIssuer(paths.SavedIdKeyFile),
        new PlayniteLibraryStateFileStore(paths.StateFile));
    return new PlayniteLibraryWidget(service);
});
