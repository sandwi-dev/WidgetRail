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
        inactiveConfigurationDisclosure.SetExpanded(disclosure.Token, true);
    }
    private bool HandleDisclosureAction(WidgetActionEvent action)
    {
        var split = action.ActionId.Split('.');
        if (split.Length != 3 || split[0] != "summary-inactive-config" || split[1] is not ("expand" or "collapse") ||
            !long.TryParse(split[2], NumberStyles.None, CultureInfo.InvariantCulture, out var token)) return false;
        if (section != "Configuration" || detailKind.Length != 0 || !string.IsNullOrWhiteSpace(filter) ||
            inactiveConfigurationDisclosure.Current?.Context != InactiveConfigurationContext()) return true;
        if (!inactiveConfigurationDisclosure.SetExpanded(token, split[1] == "expand")) return true;
        unifiedTargetId=null;unifiedIndexedKind=null;unifiedIndexedKey=null;quickControlTarget=null;
        // Native disclosure ownership recovers removed-child focus. Expansion and
        // outside focus need no author-generated focus request.
        return true;
    }
    private ContainerElement DisclosureButton(PobDisclosure disclosure, string title, string actionPrefix, string id, string group)
    {
        quickControlTargets.Add(group);
        return UI.Stack(group, UI.DisclosureHeader(id, (disclosure.Expanded ? "Hide " : "Show ") + title,
            disclosure.Expanded, group + ".content",
            actionPrefix + ".expand." + disclosure.Token.ToString(CultureInfo.InvariantCulture),
            actionPrefix + ".collapse." + disclosure.Token.ToString(CultureInfo.InvariantCulture)).Classes("pob-button"))
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
