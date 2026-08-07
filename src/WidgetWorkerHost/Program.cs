using GameBarAlternative.WidgetRuntime;

namespace GameBarAlternative.WidgetWorkerHost;

internal static class Program
{
    public static async Task<int> Main(string[] args)
    {
        return await WidgetWorkerBootstrap.RunAsync(args, _ =>
        {
            try
            {
                return WidgetAssemblyLoader.Load(
                    RequiredValue(args, "--package-root", 4096),
                    RequiredValue(args, "--widget-assembly", 4096),
                    RequiredValue(args, "--widget-type", 512));
            }
            catch (WidgetLoadException exception)
            {
                throw new WidgetWorkerBootstrapException(
                    exception.Code, exception.Message, exitCode: 2, exception);
            }
        }).ConfigureAwait(false);
    }

    private static string RequiredValue(string[] args, string name, int maximumLength)
    {
        var matches = Enumerable.Range(0, args.Length)
            .Where(index => string.Equals(args[index], name, StringComparison.Ordinal))
            .ToArray();
        if (matches.Length != 1)
            throw new ArgumentException($"Missing or invalid required argument {name}.");
        var index = matches[0];
        if (index + 1 >= args.Length || string.IsNullOrWhiteSpace(args[index + 1]) ||
            args[index + 1].Length > maximumLength || args[index + 1].StartsWith("--", StringComparison.Ordinal))
            throw new ArgumentException($"Invalid argument {name}.");
        return args[index + 1];
    }
}
