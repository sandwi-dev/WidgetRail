using System.Text.Json.Serialization;

namespace WidgetRail.PlatformSettings;

// Settings retain their existing per-store options and strict schema handling.
// Generated metadata makes those contracts available with reflection disabled.
[JsonSerializable(typeof(PlatformSettingsDocument))]
[JsonSerializable(typeof(WidgetConfigurationDocument))]
[JsonSerializable(typeof(ThemeManifestDocument))]
internal sealed partial class PlatformSettingsJsonContext : JsonSerializerContext;
