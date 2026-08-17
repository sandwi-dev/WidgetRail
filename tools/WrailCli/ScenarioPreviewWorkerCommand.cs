using System.Reflection;
using System.Runtime.Loader;
using WidgetRail.WidgetRuntime;
using WidgetRail.WidgetSdk;

namespace WidgetRail.WrailCli;

internal static class ScenarioPreviewWorkerCommand
{
    internal static async Task<int> RunAsync(string[] args, CancellationToken cancellationToken)
    {
        try
        {
            ValidateArguments(args);
            var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(
                Required(args, "--scenario-root", 4096)));
            var assemblyPath = Path.GetFullPath(Required(args, "--scenario-assembly", 4096));
            var providerType = Required(args, "--provider-type", 512);
            var factoryName = Required(args, "--scenario-factory", 128);
            var pipe = Required(args, "--widget-pipe", 200);
            var instance = Required(args, "--widget-instance", 128);
            var sessionNonce = Required(args, "--widget-session-nonce", 64);
            var maximumBytes = int.Parse(Required(args, "--max-message-bytes", 16),
                System.Globalization.CultureInfo.InvariantCulture);
            EnsureContained(root, assemblyPath);
            EnsureNoReparse(root, assemblyPath);

            var context = new ScenarioLoadContext(root, assemblyPath);
            var assembly = context.LoadFromAssemblyPath(assemblyPath);
            var type = assembly.GetType(providerType, throwOnError: false, ignoreCase: false)
                ?? throw new ScenarioWorkerException("missing_provider");
            var factories = type.GetMethods(BindingFlags.Public | BindingFlags.Static)
                .Where(method => method.Name == factoryName && !method.IsGenericMethod &&
                    method.GetParameters().Length == 0 &&
                    method.ReturnType == typeof(WidgetScenarioDefinition))
                .ToArray();
            if (factories.Length != 1)
                throw new ScenarioWorkerException("invalid_factory");
            WidgetScenarioDefinition definition;
            try
            {
                definition = (WidgetScenarioDefinition?)factories[0].Invoke(null, null)
                    ?? throw new ScenarioWorkerException("null_scenario");
            }
            catch (TargetInvocationException exception)
            {
                throw new ScenarioWorkerException("factory_failed", exception.InnerException);
            }
            await new WidgetWorkerServer(definition.Widget, instance, pipe, maximumBytes,
                    definition.HostServices.Capabilities, sessionNonce)
                .RunAsync(cancellationToken).ConfigureAwait(false);
            return 0;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return 0;
        }
        catch (ScenarioWorkerException exception)
        {
            Console.Error.WriteLine($"Scenario worker failed ({exception.Code}).");
            return 72;
        }
        catch
        {
            Console.Error.WriteLine("Scenario worker failed (invalid_scenario).");
            return 72;
        }
    }

    private static void ValidateArguments(string[] args)
    {
        string[] names =
        [
            "--scenario-root", "--scenario-assembly", "--provider-type",
            "--scenario-factory", "--widget-pipe", "--widget-instance",
            "--widget-session-nonce", "--max-message-bytes",
        ];
        if (args.Length != names.Length * 2 ||
            names.Any(name => args.Count(value => value == name) != 1))
            throw new ScenarioWorkerException("invalid_arguments");
    }

    private static string Required(string[] args, string name, int maximumLength)
    {
        var indices = Enumerable.Range(0, args.Length)
            .Where(index => args[index] == name).ToArray();
        if (indices.Length != 1 || indices[0] + 1 >= args.Length)
            throw new ScenarioWorkerException("invalid_arguments");
        var value = args[indices[0] + 1];
        if (string.IsNullOrWhiteSpace(value) || value.Length > maximumLength ||
            value.StartsWith("--", StringComparison.Ordinal))
            throw new ScenarioWorkerException("invalid_arguments");
        return value;
    }

    private static void EnsureContained(string root, string path)
    {
        var relative = Path.GetRelativePath(root, path);
        if (Path.IsPathRooted(relative) || relative is "." or ".." ||
            relative.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal) ||
            relative.StartsWith($"..{Path.AltDirectorySeparatorChar}", StringComparison.Ordinal))
            throw new ScenarioWorkerException("path_escape");
    }

    private static void EnsureNoReparse(string root, string path)
    {
        var current = root;
        if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
            throw new ScenarioWorkerException("reparse_point");
        foreach (var segment in Path.GetRelativePath(root, path).Split(
                     [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
                     StringSplitOptions.RemoveEmptyEntries))
        {
            current = Path.Combine(current, segment);
            if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new ScenarioWorkerException("reparse_point");
        }
    }

    private sealed class ScenarioLoadContext(string root, string entrypoint)
        : AssemblyLoadContext($"wrail-scenario-{Guid.NewGuid():N}", isCollectible: false)
    {
        private static readonly HashSet<string> Shared =
        [
            typeof(Widget).Assembly.GetName().Name!,
            typeof(WidgetRail.WidgetProtocol.ViewSnapshot).Assembly.GetName().Name!,
            typeof(WidgetWorkerServer).Assembly.GetName().Name!,
        ];
        private readonly AssemblyDependencyResolver _resolver = new(entrypoint);

        protected override Assembly? Load(AssemblyName assemblyName)
        {
            if (assemblyName.Name is null || Shared.Contains(assemblyName.Name)) return null;
            var resolved = _resolver.ResolveAssemblyToPath(assemblyName);
            if (resolved is null) return null;
            EnsureContained(root, resolved);
            EnsureNoReparse(root, resolved);
            return LoadFromAssemblyPath(resolved);
        }

        protected override nint LoadUnmanagedDll(string unmanagedDllName)
        {
            var resolved = _resolver.ResolveUnmanagedDllToPath(unmanagedDllName);
            if (resolved is null) return 0;
            EnsureContained(root, resolved);
            EnsureNoReparse(root, resolved);
            return LoadUnmanagedDllFromPath(resolved);
        }
    }

    private sealed class ScenarioWorkerException(string code, Exception? inner = null)
        : Exception("Scenario worker failed.", inner)
    {
        internal string Code { get; } = code;
    }
}
