using GameBarAlternative.FirstPartyWidgets.GameLauncher;
using GameBarAlternative.WidgetRuntime;
using GameBarAlternative.WindowsAppLibraryProvider;
using PackageAppLibraryProvider =
    GameBarAlternative.WindowsAppLibraryProvider.WindowsAppLibraryProvider;

var paths = GameLauncherApplicationPaths.CreateDefault();
var sources = await GameLauncherSourceConfiguration.LoadAsync(paths);

return await WidgetApplicationBootstrap.RunAsync(args, () =>
{
    var provider = new PackageAppLibraryProvider(
        _ => sources.EpicInstalledGamesEnabled,
        _ => sources.GogInstalledGamesEnabled);
    var service = new GameLauncherApplicationService(
        provider,
        new GameLauncherSavedIdIssuer(paths.SavedIdKeyFile),
        new GameLauncherStateFileStore(paths.StateFile));
    return new GameLauncherWidget(service);
});
