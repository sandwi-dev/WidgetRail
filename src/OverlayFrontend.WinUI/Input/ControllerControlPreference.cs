using WidgetRail.PlatformSettings;

namespace WidgetRail.OverlayFrontend.WinUI.Input;

/// <summary>Apply each exclusive-control request once, including explicit retry/reset revisions.</summary>
internal sealed class ControllerControlPreference
{
    private long? revision;
    private bool enabled;

    internal bool? Apply(ControllerSettings preference, Func<bool, bool> apply)
    {
        if (revision == preference.Revision && enabled == preference.ExclusiveControl) return null;
        revision = preference.Revision;
        enabled = preference.ExclusiveControl;
        // Even the first Off receipt must ask the native owner to recover any
        // owned policy left by a crashed process. In-memory Off cannot prove that.
        return apply(enabled);
    }
}
