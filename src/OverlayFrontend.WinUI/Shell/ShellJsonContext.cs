using System.Text.Json;
using System.Text.Json.Serialization;

namespace WidgetRail.OverlayFrontend.WinUI.Shell;

[JsonSerializable(typeof(OverlayShellOptions))]
[JsonSerializable(typeof(ShellPreferences))]
[JsonSerializable(typeof(PinnedPreferences))]
[JsonSerializable(typeof(WidgetRail.WidgetProtocol.WidgetSurfaceHints))]
internal sealed partial class ShellJsonContext : JsonSerializerContext
{
    internal static ShellJsonContext CaseInsensitive { get; } = new(new JsonSerializerOptions
        { PropertyNameCaseInsensitive = true });
}
