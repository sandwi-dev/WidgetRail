using System.Reflection;
using System.Runtime.Loader;
using System.Text;
using System.Text.Json;
using GameBarAlternative.WidgetProtocol;
using GameBarAlternative.WidgetSdk;

namespace GameBarAlternative.GbarCli;

internal static class RenderCommand
{
    public static async Task<int> RunAsync(string[] args, TextWriter output)
    {
        var parsed = new CommandArguments(args, "--type", "--output", "--instance");
        if (parsed.Positionals.Count != 1)
            throw new CliUsageException("Usage: gbar render <snapshot.json|widget.dll> [--type <WidgetType>] [--output <snapshot.json>] [--instance <id>]");

        var source = Path.GetFullPath(parsed.Positionals[0]);
        if (!File.Exists(source)) throw new CliUsageException($"File does not exist: {source}");

        ViewSnapshot snapshot;
        if (Path.GetExtension(source).Equals(".json", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                snapshot = SnapshotJson.Deserialize(await File.ReadAllBytesAsync(source));
            }
            catch (Exception exception) when (exception is JsonException or ProtocolValidationException)
            {
                throw new CliOperationException($"Snapshot is invalid: {exception.Message}", exception);
            }
        }
        else if (Path.GetExtension(source).Equals(".dll", StringComparison.OrdinalIgnoreCase))
        {
            var typeName = parsed.Option("--type") ??
                throw new CliUsageException("--type is required when rendering a widget assembly.");
            snapshot = RenderAssembly(source, typeName, parsed.Option("--instance") ?? "preview.instance");
        }
        else
        {
            throw new CliUsageException("Render input must be a .json snapshot or .dll widget assembly.");
        }

        var destination = parsed.Option("--output");
        if (destination is not null)
        {
            var destinationPath = Path.GetFullPath(destination);
            Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
            await File.WriteAllBytesAsync(destinationPath, SnapshotJson.Serialize(snapshot));
            await output.WriteLineAsync($"Snapshot written to {destinationPath}");
        }

        await output.WriteLineAsync(SnapshotPreview.Format(snapshot));
        return 0;
    }

    private static ViewSnapshot RenderAssembly(string assemblyPath, string typeName, string instanceId)
    {
        var loadContext = new WidgetPreviewLoadContext(assemblyPath);
        try
        {
            var assembly = loadContext.LoadFromAssemblyPath(assemblyPath);
            var type = assembly.GetType(typeName, throwOnError: false, ignoreCase: false) ??
                throw new CliOperationException($"Widget type '{typeName}' was not found.");
            if (!typeof(Widget).IsAssignableFrom(type))
                throw new CliOperationException($"Type '{typeName}' does not inherit {typeof(Widget).FullName}.");
            if (Activator.CreateInstance(type) is not Widget widget)
                throw new CliOperationException($"Widget type '{typeName}' requires a public parameterless constructor.");
            return widget.Render().CreateSnapshot(instanceId, 0);
        }
        catch (Exception exception) when (exception is not CliOperationException)
        {
            throw new CliOperationException($"Could not render widget: {exception.GetBaseException().Message}", exception);
        }
        finally
        {
            loadContext.Unload();
        }
    }
}

internal sealed class WidgetPreviewLoadContext(string mainAssemblyPath) : AssemblyLoadContext(isCollectible: true)
{
    private readonly AssemblyDependencyResolver _resolver = new(mainAssemblyPath);

    protected override Assembly? Load(AssemblyName assemblyName)
    {
        if (assemblyName.Name is "WidgetSdk" or "WidgetProtocol")
            return null;
        var path = _resolver.ResolveAssemblyToPath(assemblyName);
        return path is null ? null : LoadFromAssemblyPath(path);
    }
}
