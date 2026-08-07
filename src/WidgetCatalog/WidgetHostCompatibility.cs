using System.Runtime.InteropServices;
using GameBarAlternative.WidgetProtocol;

namespace GameBarAlternative.WidgetCatalog;

public sealed record WidgetHostContext(int HostApiMajor, string Architecture)
{
    public static WidgetHostContext Current { get; } = new(
        ProtocolConstants.CurrentVersion,
        RuntimeInformation.ProcessArchitecture switch
        {
            System.Runtime.InteropServices.Architecture.X64 => "x64",
            System.Runtime.InteropServices.Architecture.Arm64 => "arm64",
            _ => "unsupported",
        });
}

public sealed record WidgetCompatibilityResult(bool IsSupported, string Code, string Message)
{
    public static WidgetCompatibilityResult Supported { get; } =
        new(true, "supported", "Compatible with this host.");
}

public static class WidgetHostCompatibility
{
    public static WidgetCompatibilityResult Evaluate(
        WidgetManifest manifest,
        WidgetHostContext? context = null)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        context ??= WidgetHostContext.Current;
        if (context.HostApiMajor < 1 || string.IsNullOrWhiteSpace(context.Architecture))
            throw new ArgumentOutOfRangeException(nameof(context));

        if (!Version.TryParse(manifest.HostApi?.Minimum, out var minimum))
            return new(false, "invalid_host_api", "The package host API declaration is invalid.");
        if (minimum.Major > context.HostApiMajor)
            return new(false, "requires_newer_host_api",
                $"Requires host API {minimum.Major}; this host provides {context.HostApiMajor}.");
        if (manifest.HostApi!.MaximumMajor < context.HostApiMajor)
            return new(false, "unsupported_host_api",
                $"Supports host API through {manifest.HostApi.MaximumMajor}; this host provides {context.HostApiMajor}.");
        if (manifest.Architectures is null ||
            !manifest.Architectures.Contains(context.Architecture, StringComparer.Ordinal))
            return new(false, "unsupported_architecture",
                $"Does not support this host architecture ({context.Architecture}).");
        return WidgetCompatibilityResult.Supported;
    }
}
