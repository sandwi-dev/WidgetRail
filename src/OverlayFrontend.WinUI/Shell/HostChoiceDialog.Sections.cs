using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace WidgetRail.OverlayFrontend.WinUI.Shell;

internal sealed partial class HostChoiceDialog
{
    private readonly Dictionary<string, List<HostDialogChoice>> sectionChildren = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> sectionForChoice = new(StringComparer.Ordinal);
    private string? expandedSection;

    private List<HostDialogChoice> SectionHeaders(IReadOnlyList<HostDialogChoice> source)
    {
        var visible = new List<HostDialogChoice>();
        foreach (var choice in source)
        {
            if (choice.Section is not { } section) { visible.Add(choice); continue; }
            var id = "section:" + section.Id;
            if (!sectionChildren.TryGetValue(id, out var children))
            {
                sectionChildren.Add(id, children = []);
                visible.Add(new(id, section.Label));
            }
            children.Add(choice);
            sectionForChoice.Add(choice.Id, id);
        }
        return visible;
    }

    private ChoiceSectionItem CreateSectionHeader(HostDialogChoice section)
    {
        var item = new ChoiceSectionItem(section.Label, value => ExpandSection(section.Id, value))
            { Tag = section.Id, HorizontalContentAlignment = HorizontalAlignment.Stretch, MinHeight = 44, Padding = new(12, 6, 12, 6) };
        AutomationProperties.SetAutomationId(item, "Host.Choice.Section." + section.Id["section:".Length..]);
        AutomationProperties.SetName(item, section.Label);
        return item;
    }

    private void ExpandSection(string id, bool expand)
    {
        if (closing || (expandedSection == id) == expand) return;
        if (expandedSection is { } previous)
        {
            var index = entries.FindIndex(entry => entry.Id == previous);
            var count = sectionChildren[previous].Count;
            entries.RemoveRange(index + 1, count);
            for (var i = 0; i < count; i++) choiceItems.RemoveAt(index + 1);
            ((ChoiceSectionItem)choiceItems[index]).SetExpanded(false);
        }
        expandedSection = expand ? id : null;
        var header = entries.FindIndex(entry => entry.Id == id);
        if (expand)
        {
            var insert = header + 1;
            foreach (var child in sectionChildren[id])
            {
                var item = CreateChoice(child);
                item.Padding = new(38, 4, 12, 4);
                entries.Insert(insert, child);
                choiceItems.Insert(insert++, item);
            }
            ((ChoiceSectionItem)choiceItems[header]).SetExpanded(true);
        }
        if (opened)
        {
            initialFocusRequired = false;
            var target = choiceItems[header];
            choices.ScrollIntoView(target);
            target.Focus(FocusState.Keyboard);
        }
    }

    private bool MoveSection(bool right)
    {
        if (sectionChildren.Count == 0 || FocusedChoiceId is not { } id) return false;
        if (sectionChildren.ContainsKey(id))
        {
            if (right && expandedSection == id)
            {
                var first = choiceItems[entries.FindIndex(entry => entry.Id == id) + 1];
                choices.ScrollIntoView(first);
                first.Focus(FocusState.Keyboard);
            }
            else ExpandSection(id, right);
            return true;
        }
        if (right || !sectionForChoice.TryGetValue(id, out var section)) return false;
        ExpandSection(section, false);
        return true;
    }
}

internal sealed partial class ChoiceSectionItem : ListViewItem
{
    private readonly FontIcon chevron = new() { Glyph = "\uE76C", FontSize = 14, FontFamily = new FontFamily("Segoe Fluent Icons") };
    private readonly Action<bool> change;
    private bool expanded;
    internal ChoiceSectionItem(string title, Action<bool> change)
    {
        this.change = change;
        var row = new Grid { ColumnSpacing = 12, ColumnDefinitions = { new() { Width = new(1, GridUnitType.Star) }, new() { Width = GridLength.Auto } } };
        row.Children.Add(new TextBlock { Text = title, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center });
        Grid.SetColumn(chevron, 1);
        row.Children.Add(chevron);
        AutomationProperties.SetAccessibilityView(chevron, AccessibilityView.Raw);
        Content = row;
    }
    internal void SetExpanded(bool value)
    {
        var previous = State;
        expanded = value;
        chevron.Glyph = value ? "\uE70D" : "\uE76C";
        FrameworkElementAutomationPeer.FromElement(this)?.RaisePropertyChangedEvent(
            ExpandCollapsePatternIdentifiers.ExpandCollapseStateProperty, previous, State);
    }
    private ExpandCollapseState State => expanded ? ExpandCollapseState.Expanded : ExpandCollapseState.Collapsed;
    protected override AutomationPeer OnCreateAutomationPeer() => new SectionPeer(this);
    private sealed partial class SectionPeer(ChoiceSectionItem item) : ListViewItemAutomationPeer(item), IExpandCollapseProvider
    {
        public ExpandCollapseState ExpandCollapseState => item.State;
        public void Expand() => item.change(true);
        public void Collapse() => item.change(false);
        protected override object GetPatternCore(PatternInterface pattern) => pattern == PatternInterface.ExpandCollapse ? this : base.GetPatternCore(pattern);
    }
}
