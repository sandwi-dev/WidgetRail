using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using WidgetRail.WidgetProtocol;

namespace WidgetRail.OverlayFrontend.WinUI.Shell;

internal sealed record HostDialogChoice(string Id, string Label);

/// <summary>A small host-owned decision surface with the same release/neutral boundary as install dialogs.</summary>
internal sealed partial class HostChoiceDialog : ContentDialog
{
    private readonly ListView choices = new() { MaxHeight = 320, SelectionMode = ListViewSelectionMode.Single,
        IsItemClickEnabled = true, HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly IReadOnlyList<HostDialogChoice> entries;
    internal string? SelectedId { get; private set; }
    internal bool ControllerArmed { get; set; }

    internal HostChoiceDialog(string title, string message, IReadOnlyList<HostDialogChoice> entries)
    {
        this.entries = entries;
        Input.GamepadKeyBoundary.ObserveDialog(this);
        AutomationProperties.SetAutomationId(this, "Host.Choice.Dialog");
        AutomationProperties.SetAutomationId(choices, "Host.Choice.Options");
        AutomationProperties.SetName(choices, "Available options");
        Title = title; CloseButtonText = entries.Count == 0 ? "Done" : "Cancel";
        DefaultButton = ContentDialogButton.Close;
        var content = new StackPanel { Spacing = 12, MaxWidth = 480 };
        if (!string.IsNullOrWhiteSpace(message)) content.Children.Add(new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap });
        if (entries.Count > 0)
        {
            for (var index = 0; index < entries.Count; index++)
            {
                var item = new ListViewItem { Content = entries[index].Label, Tag = index,
                    HorizontalContentAlignment = HorizontalAlignment.Left };
                AutomationProperties.SetAutomationId(item, "Host.Choice.Option." + index);
                choices.Items.Add(item);
            }
            choices.SelectedIndex = 0;
            choices.ItemClick += (_, args) => { if (args.ClickedItem is ListViewItem { Tag: int index }) Choose(index); };
            content.Children.Add(choices);
            PrimaryButtonText = "Open";
            choices.SelectionChanged += (_, _) => IsPrimaryButtonEnabled = choices.SelectedIndex >= 0;
            PrimaryButtonClick += (_, args) => { args.Cancel = true; Choose(choices.SelectedIndex); };
        }
        Content = content;
        Opened += (_, _) => (GetTemplateChild("CloseButton") as Control)?.Focus(FocusState.Keyboard);
    }

    private void Choose(int index)
    {
        if (index < 0 || index >= entries.Count) return;
        SelectedId = entries[index].Id;
        ControllerArmed = false;
        Hide();
    }

    internal void MoveFocus(FocusNavigationDirection direction)
    {
        if (ListFocused() && direction is FocusNavigationDirection.Up or FocusNavigationDirection.Down)
        {
            var next = choices.SelectedIndex + (direction == FocusNavigationDirection.Up ? -1 : 1);
            if (next >= 0 && next < entries.Count)
            {
                choices.SelectedIndex = next;
                choices.ScrollIntoView(choices.Items[next]);
                (choices.Items[next] as ListViewItem)?.Focus(FocusState.Keyboard);
                return;
            }
        }
        FocusManager.TryMoveFocus(direction, new FindNextElementOptions { SearchRoot = this });
    }

    internal void Handle(ControllerButton button, ControllerEventPhase phase)
    {
        if (!ControllerArmed || phase != ControllerEventPhase.Pressed) return;
        if (button == ControllerButton.B) { ControllerArmed = false; Hide(); }
        else if (button == ControllerButton.A)
        {
            if (ListFocused()) Choose(choices.SelectedIndex);
            else if (FocusManager.GetFocusedElement(XamlRoot) is Button focused)
            {
                ControllerArmed = false;
                (FrameworkElementAutomationPeer.CreatePeerForElement(focused)?.GetPattern(PatternInterface.Invoke) as IInvokeProvider)?.Invoke();
            }
        }
    }

    private bool ListFocused()
    {
        for (var node = FocusManager.GetFocusedElement(XamlRoot) as DependencyObject; node is not null; node = VisualTreeHelper.GetParent(node))
            if (ReferenceEquals(node, choices)) return true;
        return false;
    }
}
