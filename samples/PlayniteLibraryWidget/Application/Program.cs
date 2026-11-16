using WidgetRail.Samples.PlayniteLibrary;
using WidgetRail.WidgetRuntime;

var paths = PlayniteLibraryApplicationPaths.CreateDefault();
var diagnostics = PlayniteLibraryApplicationDiagnostics.CreateDefault();

return await WidgetApplicationBootstrap.RunAsync(args, () =>
{
    var connections = new PlayniteLocalConnectionStore(paths.Root);
    var credentials = new PlayniteProfileCredentialStore(connections);
    var service = new PlayniteLibraryApplicationService(
        PlayniteBridgeClient.CreateLocal(connections, credentials),
        new PlayniteLibraryStateFileStore(paths.StateFile),
        diagnostics);
    return new PlayniteLibraryWidget(service, playniteClient: PlayniteBridgeClient.CreateLocal(connections, credentials),
        setupService: new PlayniteSetupService(paths.Root, Path.Combine(AppContext.BaseDirectory, "integration"),
            credentials: credentials, localConnections: connections));
});
