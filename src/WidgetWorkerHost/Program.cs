using System.Globalization;
using GameBarAlternative.WidgetRuntime;

namespace GameBarAlternative.WidgetWorkerHost;

internal static class Program
{
    public static async Task<int> Main(string[] args)
    {
        try
        {
            var pipeName = RequiredValue(args, "--widget-pipe", 200);
            var instanceId = RequiredValue(args, "--widget-instance", 128);
            var maximumBytes = int.Parse(
                RequiredValue(args, "--max-message-bytes", 16),
                NumberStyles.None,
                CultureInfo.InvariantCulture);
            var packageRoot = RequiredValue(args, "--package-root", 4096);
            var assemblyPath = RequiredValue(args, "--widget-assembly", 4096);
            var typeName = RequiredValue(args, "--widget-type", 512);
            var widget = WidgetAssemblyLoader.Load(packageRoot, assemblyPath, typeName);
            using var shutdown = new CancellationTokenSource();
            Console.CancelKeyPress += (_, eventArgs) =>
            {
                eventArgs.Cancel = true;
                shutdown.Cancel();
            };
            await new WidgetWorkerServer(
                widget, instanceId, pipeName, maximumBytes).RunAsync(shutdown.Token)
                .ConfigureAwait(false);
            return 0;
        }
        catch (WidgetLoadException exception)
        {
            Console.Error.WriteLine($"Widget worker failed ({exception.Code}): {exception.Message}");
            return 2;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"Widget worker failed: {exception.Message}");
            return 1;
        }
    }

    private static string RequiredValue(string[] args, string name, int maximumLength)
    {
        var index = Array.IndexOf(args, name);
        if (index < 0 || index + 1 >= args.Length ||
            string.IsNullOrWhiteSpace(args[index + 1]) || args[index + 1].Length > maximumLength)
            throw new ArgumentException($"Missing or invalid required argument {name}.");
        return args[index + 1];
    }
}
