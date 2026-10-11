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
    private readonly ScrollViewer? messageViewport;
    private readonly RightStickScroll messageScroll = new();
    internal bool SupportsMessageScroll => messageViewport is not null;
    internal string? SelectedId { get; private set; }
    internal bool ControllerArmed { get; set; }

    internal HostChoiceDialog(string title, string message, IReadOnlyList<HostDialogChoice> entries, bool directDecisionButtons = false)
    {
        this.entries = entries;
        Input.GamepadKeyBoundary.ObserveDialog(this);
        AutomationProperties.SetAutomationId(this, "Host.Choice.Dialog");
        AutomationProperties.SetAutomationId(choices, "Host.Choice.Options");
        AutomationProperties.SetName(choices, "Available options");
        Title = title; CloseButtonText = entries.Count == 0 ? "Done" : "Cancel";
        DefaultButton = ContentDialogButton.Close;
        var content = new StackPanel { Spacing = 12, MaxWidth = 480 };
        if (!string.IsNullOrWhiteSpace(message))
        {
            var text = new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap };
            if (directDecisionButtons)
            {
                messageViewport = new ScrollViewer { Content = text, MaxHeight = 360, IsTabStop = false,
                    HorizontalScrollMode = ScrollMode.Disabled, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                    VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
                AutomationProperties.SetAutomationId(messageViewport, "Host.Choice.Message");
                AutomationProperties.SetName(messageViewport, "Requested access. Scroll to read all permissions.");
                content.Children.Add(messageViewport);
            }
            else content.Children.Add(text);
        }
        if (directDecisionButtons)
        {
            if (entries.Count != 2) throw new ArgumentException("Direct decisions require two choices.", nameof(entries));
            PrimaryButtonText = entries[0].Label;
            SecondaryButtonText = entries[1].Label;
            PrimaryButtonClick += (_, args) => { args.Cancel = true; Choose(0); };
            SecondaryButtonClick += (_, args) => { args.Cancel = true; Choose(1); };
        }
        else if (entries.Count > 0)
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
            if (entries.Count > 1) content.Children.Add(choices);
            PrimaryButtonText = entries.Count == 1 ? entries[0].Label : "Open";
            choices.SelectionChanged += (_, _) => IsPrimaryButtonEnabled = choices.SelectedIndex >= 0;
            PrimaryButtonClick += (_, args) => { args.Cancel = true; Choose(choices.SelectedIndex); };
        }
        Content = content;
        Opened += (_, _) =>
        {
            (GetTemplateChild("CloseButton") as Control)?.Focus(FocusState.Keyboard);
        };
        Loaded += (_, _) =>
        {
            // ContentDialog performs its initial content focus during opening.
            // Admit the scroll stop after that pass so Cancel remains the safe
            // default, while the message is reachable during normal navigation.
            DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
            {
                if (IsLoaded && messageViewport is not null) messageViewport.IsTabStop = true;
            });
        };
    }

    protected override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        // Theme application can replace the button after Opened. Keep the ID
        // on the current template instance rather than the discarded control.
        if (GetTemplateChild("PrimaryButton") is Control primary)
            AutomationProperties.SetAutomationId(primary, "Host.Choice.Primary");
        if (GetTemplateChild("SecondaryButton") is Control secondary)
            AutomationProperties.SetAutomationId(secondary, "Host.Choice.Secondary");
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
        if (messageViewport is not null && IsWithinFocused(messageViewport) && (direction is FocusNavigationDirection.Up or FocusNavigationDirection.Down) &&
            ScrollMessage(direction == FocusNavigationDirection.Up ? -64 : 64)) return;
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

    internal void ConstrainMessageViewport(double rootHeight)
    { if (messageViewport is not null) messageViewport.MaxHeight = Math.Clamp(rootHeight - 200, 40, 360); }
    internal void ResetController()
    { ControllerArmed = false; messageScroll.Reset(); }
    internal void SampleMessageScroll(short x, short y, long timestamp)
    {
        var delta = messageScroll.Sample(x, y, timestamp);
        if (delta.Y != 0) ScrollMessage(delta.Y);
    }
    private bool ScrollMessage(double delta)
    {
        if (messageViewport is null || messageViewport.ScrollableHeight <= 0) return false;
        var target = Math.Clamp(messageViewport.VerticalOffset + delta, 0, messageViewport.ScrollableHeight);
        if (Math.Abs(target - messageViewport.VerticalOffset) < 0.1) return false;
        messageViewport.ChangeView(null, target, null, disableAnimation: true);
        return true;
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

    private bool ListFocused() => IsWithinFocused(choices);
    private bool IsWithinFocused(DependencyObject target)
    {
        for (var node = FocusManager.GetFocusedElement(XamlRoot) as DependencyObject; node is not null; node = VisualTreeHelper.GetParent(node))
            if (ReferenceEquals(node, target)) return true;
        return false;
    }
}
