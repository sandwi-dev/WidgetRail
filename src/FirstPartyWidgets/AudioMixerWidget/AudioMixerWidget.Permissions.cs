using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

namespace WidgetRail.FirstPartyWidgets.AudioMixer;

public sealed partial class AudioMixerWidget
{
    private static readonly string[] MixerPermissions = [
        "system.audio.sessions.read.v1", "system.audio.sessions.control.v1",
        "system.audio.output.read.v1", "system.audio.output.control.v1",
        "system.audio.devices.read.v1", "system.audio.devices.control.v1",
        "system.audio.input.read.v1", "system.audio.input.control.v1"];
    private static readonly string[] SpatialPermissions = [
        "system.audio.spatial.read.v1", "system.audio.spatial.control.v1"];

    private void RefreshApprovedAudioSections()
    {
        RetryOptionalSection(AudioMixerProviderSection.Devices);
        RetryOptionalSection(AudioMixerProviderSection.Input);
        RetryOptionalSection(AudioMixerProviderSection.Spatial);
    }

    private string[] ActionPermissions(WidgetActionEvent action)
    {
        if (TryResolveSessionAction(action.ActionId, out _, out var sessionAction))
            return sessionAction == SessionAction.ToggleStar ? [] : ["system.audio.sessions.control.v1"];
        if (action.ActionId.StartsWith("output.", StringComparison.Ordinal)) return ["system.audio.output.control.v1"];
        if (action.ActionId.StartsWith("input.", StringComparison.Ordinal)) return action.ActionId == "input.retry" ? ["system.audio.input.read.v1"] : ["system.audio.input.control.v1"];
        if (action.ActionId.StartsWith("device.", StringComparison.Ordinal)) return ["system.audio.devices.read.v1", "system.audio.devices.control.v1"];
        if (action.ActionId.StartsWith("spatial.set.", StringComparison.Ordinal)) return ["system.audio.spatial.control.v1"];
        return action.ActionId switch { "devices.retry" => ["system.audio.devices.read.v1"], "spatial.retry" => ["system.audio.spatial.read.v1"], "retry" => ["system.audio.sessions.read.v1", "system.audio.output.read.v1"], _ => [] };
    }
    private void PermissionFeedback(string text) { lock (_stateLock) { _deviceFeedback = text; } Invalidate(); }
}
