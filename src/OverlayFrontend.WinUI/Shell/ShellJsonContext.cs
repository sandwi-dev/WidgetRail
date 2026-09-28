using System.Text.Json;
using System.Text.Json.Serialization;

namespace WidgetRail.OverlayFrontend.WinUI.Shell;

[JsonSerializable(typeof(OverlayShellOptions))]
[JsonSerializable(typeof(ShellPreferences))]
[JsonSerializable(typeof(PinnedPreferences))]
internal sealed partial class ShellJsonContext : JsonSerializerContext
{
    internal static ShellJsonContext CaseInsensitive { get; } = new(new JsonSerializerOptions
        { PropertyNameCaseInsensitive = true });
}
