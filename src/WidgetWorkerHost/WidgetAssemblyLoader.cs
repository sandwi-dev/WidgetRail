using System.Reflection;
using System.Runtime.Loader;
using WidgetRail.WidgetSdk;

namespace WidgetRail.WidgetWorkerHost;

internal static class WidgetAssemblyLoader
{
    public static Widget Load(string packageRoot, string assemblyPath, string typeName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packageRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(assemblyPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(typeName);
        if (typeName.Length > 512 || typeName.Any(char.IsControl))
            throw new WidgetLoadException("invalid_type", "Widget entrypoint type is invalid.");

        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(packageRoot));
        var entrypoint = Path.GetFullPath(assemblyPath);
        EnsureContained(root, entrypoint);
        EnsureNoReparsePoints(root, entrypoint);
        if (!File.Exists(entrypoint) || !string.Equals(Path.GetExtension(entrypoint), ".dll", StringComparison.OrdinalIgnoreCase))
            throw new WidgetLoadException("missing_entrypoint", "Widget entrypoint assembly is missing.");

        var context = new PackageLoadContext(root, entrypoint);
        Assembly assembly;
        try
        {
            assembly = context.LoadFromAssemblyPath(entrypoint);
        }
        catch (Exception exception) when (exception is BadImageFormatException or FileLoadException or FileNotFoundException)
        {
            throw new WidgetLoadException("invalid_assembly", "Widget entrypoint assembly could not be loaded.", exception);
        }

        var type = assembly.GetType(typeName, throwOnError: false, ignoreCase: false)
            ?? throw new WidgetLoadException("missing_type", "Widget entrypoint type was not found.");
        if (!typeof(Widget).IsAssignableFrom(type) || type.IsAbstract || type.IsInterface ||
            type.IsGenericTypeDefinition || !type.IsPublic)
            throw new WidgetLoadException(
                "invalid_type", "Widget entrypoint must be a public, concrete Widget type.");
        var constructors = type.GetConstructors(BindingFlags.Instance | BindingFlags.Public)
            .Where(candidate => candidate.GetParameters().All(parameter => parameter.HasDefaultValue))
            .OrderBy(candidate => candidate.GetParameters().Length)
            .ToArray();
        if (constructors.Length == 0)
            throw new WidgetLoadException(
                "invalid_constructor",
                "Widget entrypoint must have a public constructor whose parameters are all optional.");
        var constructor = constructors[0];
        try
        {
            var arguments = constructor.GetParameters()
                .Select(parameter => parameter.DefaultValue)
                .ToArray();
            return (Widget)constructor.Invoke(arguments);
        }
        catch (TargetInvocationException exception)
        {
            throw new WidgetLoadException(
                "constructor_failed", "Widget entrypoint constructor failed.", exception.InnerException ?? exception);
        }
    }

    private static void EnsureContained(string root, string path)
    {
        var relative = Path.GetRelativePath(root, path);
        if (Path.IsPathRooted(relative) || relative is "." or ".." ||
            relative.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal) ||
            relative.StartsWith($"..{Path.AltDirectorySeparatorChar}", StringComparison.Ordinal))
            throw new WidgetLoadException(
                "path_escape", "Widget entrypoint must remain inside its installed package.");
    }

    private static void EnsureNoReparsePoints(string root, string path)
    {
        RejectReparse(root);
        var relative = Path.GetRelativePath(root, path);
        var current = root;
        foreach (var segment in relative.Split(
            [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
            StringSplitOptions.RemoveEmptyEntries))
        {
            current = Path.Combine(current, segment);
            if (File.Exists(current) || Directory.Exists(current)) RejectReparse(current);
        }
    }

    private static void RejectReparse(string path)
    {
        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            throw new WidgetLoadException(
                "reparse_point", "Widget package paths cannot contain reparse points.");
    }

    private sealed class PackageLoadContext(string packageRoot, string entrypoint)
        : AssemblyLoadContext($"wrail-widget-{Guid.NewGuid():N}", isCollectible: false)
    {
        private static readonly HashSet<string> SharedAssemblies =
        [
            typeof(Widget).Assembly.GetName().Name!,
            typeof(WidgetRail.WidgetProtocol.ManifestJson).Assembly.GetName().Name!,
            typeof(WidgetRail.WidgetRuntime.WidgetWorkerServer).Assembly.GetName().Name!,
        ];
        private readonly AssemblyDependencyResolver _resolver = new(entrypoint);
        private readonly string _root = packageRoot;

        protected override Assembly? Load(AssemblyName assemblyName)
        {
            if (assemblyName.Name is null || SharedAssemblies.Contains(assemblyName.Name)) return null;
            var resolved = _resolver.ResolveAssemblyToPath(assemblyName);
            if (resolved is null) return null;
            EnsureContained(_root, resolved);
            EnsureNoReparsePoints(_root, resolved);
            return LoadFromAssemblyPath(resolved);
        }

        protected override nint LoadUnmanagedDll(string unmanagedDllName)
        {
            var resolved = _resolver.ResolveUnmanagedDllToPath(unmanagedDllName);
            if (resolved is null) return 0;
            EnsureContained(_root, resolved);
            EnsureNoReparsePoints(_root, resolved);
            return LoadUnmanagedDllFromPath(resolved);
        }
    }
}

internal sealed class WidgetLoadException(string code, string message, Exception? innerException = null)
    : Exception(message, innerException)
{
    public string Code { get; } = code;
}
