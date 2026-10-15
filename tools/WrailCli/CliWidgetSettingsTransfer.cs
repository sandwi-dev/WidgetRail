using WidgetRail.WidgetCatalog;

namespace WidgetRail.WrailCli;

internal sealed class CliWidgetSettingsTransfer(string stateRoot) : IDisposable
{
    private FileStream? inactive;
    public void Dispose() => inactive?.Dispose();

    internal static string ResolveRoot(CommandArguments arguments, string catalogRoot) =>
        Path.Combine(arguments.Option("--settings-root") is { } profile
            ? Path.GetFullPath(profile) : Path.GetDirectoryName(catalogRoot)!, "widget-state");

    internal async Task PrepareAsync(InstalledWidgetVersion previous, InstalledWidgetVersion replacement, CancellationToken token)
    {
        // Disabled catalog state alone does not prove the running host has retired
        // the worker. Its immutable launch lease holds the manifest open on Windows.
        try { inactive = new FileStream(Path.Combine(previous.InstallPath, "manifest.json"), FileMode.Open, FileAccess.Read, FileShare.None); }
        catch (IOException exception)
        {
            throw new CliOperationException("The widget is still in use. Close WidgetRail and retry the update. The current version is still selected.", exception);
        }
        await new WidgetSettingsTransfer(stateRoot).CopyAsync(previous, replacement, token).ConfigureAwait(false);
    }
}
