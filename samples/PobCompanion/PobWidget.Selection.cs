using WidgetRail.WidgetSdk;

namespace WidgetRail.Samples.PobCompanion;

public sealed partial class PobWidget
{
    // A remembered/upstream choice may disappear after a set or engine change.
    // Preserve the unresolved state explicitly; never choose a gameplay value
    // merely to satisfy the dropdown contract. Existing option actions stay intact.
    private static SelectElement SelectWithUnavailable(string label,IReadOnlyList<SelectOption> options,string id,
        string unavailable="Current selection unavailable — choose a value")
    {
        if(!options.Any(option=>option.IsSelected))
            options=[new("unavailable-selection",InlineLabel(unavailable),"selection-unavailable",true,IsDisabled:true),..options];
        return UI.Select(label,options,id);
    }
}
