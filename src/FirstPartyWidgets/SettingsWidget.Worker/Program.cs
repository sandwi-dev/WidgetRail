using GameBarAlternative.FirstPartyWidgets.Settings;
using GameBarAlternative.WidgetRuntime;

namespace GameBarAlternative.FirstPartyWidgets.Settings.Worker;

internal static class Program
{
    public static async Task<int> Main(string[] args)
    {
        return await WidgetWorkerBootstrap.RunAsync(
            args,
            () => new SettingsWidget(
                bundledWidgetRoot: OptionalPath(args, "--bundled-widget-root")))
            .ConfigureAwait(false);
    }

    private static string? OptionalPath(string[] args, string name)
    {
        var indexes = Enumerable.Range(0, args.Length)
            .Where(index => string.Equals(args[index], name, StringComparison.Ordinal))
            .ToArray();
        if (indexes.Length == 0) return null;
        if (indexes.Length != 1)
            throw new WidgetWorkerBootstrapException(
                "invalid_settings_root", "The bundled widget root argument is invalid.");
        var index = indexes[0];
        if (index + 1 >= args.Length || string.IsNullOrWhiteSpace(args[index + 1]) ||
            args[index + 1].Length > 1024 || args[index + 1].StartsWith("--", StringComparison.Ordinal))
            throw new WidgetWorkerBootstrapException(
                "invalid_settings_root", "The bundled widget root argument is invalid.");
        try
        {
            return Path.GetFullPath(args[index + 1]);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            throw new WidgetWorkerBootstrapException(
                "invalid_settings_root", "The bundled widget root argument is invalid.",
                innerException: exception);
        }
    }
}
