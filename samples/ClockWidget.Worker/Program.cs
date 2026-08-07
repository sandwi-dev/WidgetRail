using System.Globalization;
using GameBarAlternative.Samples.ClockWidget;
using GameBarAlternative.WidgetRuntime;

namespace GameBarAlternative.Samples.ClockWidget.Worker;

public static class WorkerMarker;

internal static class Program
{
    public static async Task<int> Main(string[] args)
    {
        try
        {
            var pipeName = RequiredValue(args, "--widget-pipe");
            var instanceId = RequiredValue(args, "--widget-instance");
            var maximumBytes = int.Parse(
                RequiredValue(args, "--max-message-bytes"), CultureInfo.InvariantCulture);
            using var shutdown = new CancellationTokenSource();
            Console.CancelKeyPress += (_, eventArgs) =>
            {
                eventArgs.Cancel = true;
                shutdown.Cancel();
            };
            var server = new WidgetWorkerServer(
                new ClockWidget(), instanceId, pipeName, maximumBytes);
            await server.RunAsync(shutdown.Token).ConfigureAwait(false);
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"Clock widget worker failed: {exception.Message}");
            return 1;
        }
    }

    private static string RequiredValue(string[] args, string name)
    {
        var index = Array.IndexOf(args, name);
        if (index < 0 || index + 1 >= args.Length || string.IsNullOrWhiteSpace(args[index + 1]))
            throw new ArgumentException($"Missing required argument {name}.");
        return args[index + 1];
    }
}

