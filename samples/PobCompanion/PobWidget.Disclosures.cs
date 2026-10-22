using System.Globalization;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

namespace WidgetRail.Samples.PobCompanion;

public sealed partial class PobWidget
{
    private readonly PobDisclosureState inactiveConfigurationDisclosure = new();
    private string InactiveConfigurationContext() => string.Join("\u001f", documentId ?? "", configSection,
        S(A(State,"configSets").FirstOrDefault(set=>B(set,"active")),"id"));
    private void RevealConfigurationSummary(PobConfigurationOption option)
    {
        if(!InactiveConfiguration(option))return;
        var disclosure=inactiveConfigurationDisclosure.Select(InactiveConfigurationContext(),defaultExpanded:false);
        if(!disclosure.Expanded)inactiveConfigurationDisclosure.Toggle(disclosure.Token);
    }
    private bool HandleDisclosureAction(WidgetActionEvent action)
    {
        var split = action.ActionId.Split('.', 2);
        if (split.Length != 2 || !long.TryParse(split[1], NumberStyles.None, CultureInfo.InvariantCulture, out var token)) return false;
        PobDisclosureState owner;
        string group;
        if (split[0] == "summary-inactive-config" && section == "Configuration" && detailKind.Length == 0 &&
            string.IsNullOrWhiteSpace(filter) && inactiveConfigurationDisclosure.Current?.Context == InactiveConfigurationContext())
        { owner = inactiveConfigurationDisclosure; group = "pob.config.inactive.disclosure"; }
        else return false;
        if (!owner.Toggle(token)) return true;
        unifiedTargetId=null;unifiedIndexedKind=null;unifiedIndexedKey=null;
        // A deliberate collapse also handles pointer invocation while native
        // focus was in a child that is being removed. Never focus on refresh.
        quickControlTarget = group; lastFocusRequestId = CreateFocusRequest().Id;
        return true;
    }
    private ContainerElement DisclosureButton(PobDisclosure disclosure, string title, string actionPrefix, string id, string group)
    {
        quickControlTargets.Add(group);
        return UI.Stack(group, UI.Button((disclosure.Expanded ? "Hide " : "Show ") + title,
            actionPrefix + "." + disclosure.Token.ToString(CultureInfo.InvariantCulture), id).Classes("pob-button"))
            .RememberChildFocus(id);
    }
    private static bool InactiveConfiguration(PobConfigurationOption option) =>
        option.Raw.TryGetProperty("applicable", out var applicable) && applicable.ValueKind == System.Text.Json.JsonValueKind.False;
    private static string ConfigurationSummaryValue(PobConfigurationOption option)
    {
        var value = option.Raw.GetProperty("value");
        var choice = A(option.Raw, "options").FirstOrDefault(item => item.TryGetProperty("value", out var candidate) && candidate.GetRawText() == value.GetRawText());
        return choice.ValueKind == System.Text.Json.JsonValueKind.Object ? S(choice,"label") :
            value.ValueKind == System.Text.Json.JsonValueKind.True ? "On" : value.ValueKind == System.Text.Json.JsonValueKind.False ? "Off" :
                string.IsNullOrWhiteSpace(value.ToString()) ? "Empty" : Limit(InlineLabel(value.ToString()),100);
    }
}
