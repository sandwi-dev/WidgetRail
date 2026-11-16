using System.Collections.Generic;
namespace WidgetRail.Playnite;
internal sealed partial class AchievementAdapter
{
    internal object ReadCaptureSettings() { EnsureBinding(); return ProviderCaptureSettings.Read(plugin); }
    internal object UpdateCaptureSettings(Dictionary<string, object> body) { EnsureBinding(); return ProviderCaptureSettings.Update(plugin, body); }
}
