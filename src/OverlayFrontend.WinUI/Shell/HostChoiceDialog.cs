using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using WidgetRail.WidgetProtocol;

namespace WidgetRail.OverlayFrontend.WinUI.Shell;

internal sealed record HostDialogChoice(string Id, string Label, long ContentRevision = 0, string? Group = null);

/// <summary>A small host-owned decision surface with the same release/neutral boundary as install dialogs.</summary>
internal sealed partial class HostChoiceDialog : Presentation.FrameworkDialog
{
    private readonly ListView choices = new() { MaxHeight = 320, SelectionMode = ListViewSelectionMode.Single,
        IsItemClickEnabled = true, HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly Grid content = new() { RowSpacing = 8, MaxWidth = 480 };
    private readonly StackPanel details = new() { Spacing = 8 };
    internal void ApplyDescriptionBrush(Brush brush)
    {
        foreach (var item in details.Children)
            if (item is TextBlock text) text.Foreground = brush;
            else if (item is ScrollViewer { Content: TextBlock message }) message.Foreground = brush;
    }
    private readonly List<HostDialogChoice> entries;
    private readonly HashSet<string> reportedChoices = [];
    private bool opened, reportingQueued;
#if ENABLE_WIDGET_VALIDATION
    internal bool IsOpenForValidation => opened;
#endif
    private Func<HostDialogChoice, UIElement>? choiceContent;
    internal Func<string, bool>? KeepOpenOnChoice { get; set; }
    internal Func<string, bool>? ChoiceIsActionable { get; set; }
    internal bool FocusedChoiceIsActionable => XamlRoot is null || !ListFocused() || choices.SelectedItem is not ListViewItem { Tag: string id } || ChoiceIsActionable?.Invoke(id) != false;
    internal event Action? SelectionContextChanged;
    internal void SetChoiceContent(Func<HostDialogChoice, UIElement> create)
    {
        choiceContent = create;
        for (var index = 0; index < entries.Count; index++)
            if (choices.Items[index] is ListViewItem item) item.Content = create(entries[index]);
    }
    internal void FocusFirstChoice()
    {
        if (choices.Items.Count == 0) return;
        choices.SelectedIndex = Math.Max(0, choices.SelectedIndex);
        (choices.SelectedItem as Control)?.Focus(FocusState.Keyboard);
    }
    internal void SetListHeight(double height) => choices.MaxHeight = Math.Max(48, height);

    private CheckBox? rememberChoice;
    internal bool RememberChoice => rememberChoice?.IsChecked == true;
    internal void AddRememberChoice(string label)
    {
        if (rememberChoice is not null) return;
        rememberChoice = new CheckBox { Content = label, IsChecked = false };
        AutomationProperties.SetAutomationId(rememberChoice, "Host.Choice.Remember");
        AutomationProperties.SetName(rememberChoice, label);
        Grid.SetRow(rememberChoice, 2);
        content.Children.Add(rememberChoice);
    }
    internal event Action<string>? DismissChoiceRequested;
    internal event Action<string>? ChoiceShown;
    internal bool SupportsDismissChoice => DismissChoiceRequested is not null;
    private readonly ScrollViewer? messageViewport;
    private readonly RightStickScroll messageScroll = new();
    internal bool SupportsMessageScroll => messageViewport is not null;
    internal string? SelectedId { get; private set; }
    internal bool ControllerArmed { get; set; }

    internal HostChoiceDialog(string title, string message, IReadOnlyList<HostDialogChoice> entries, bool directDecisionButtons = false)
    {
        this.entries = entries.ToList();
        Resources["ContentDialogPadding"] = new Thickness(16);
        Resources["ContentDialogTitleMargin"] = new Thickness(0, 0, 0, 8);
        Input.GamepadKeyBoundary.ObserveDialog(this);
        AutomationProperties.SetAutomationId(this, "Host.Choice.Dialog");
        AutomationProperties.SetAutomationId(choices, "Host.Choice.Options");
        AutomationProperties.SetName(choices, "Available options");
        Title = title; CloseButtonText = entries.Count == 0 ? "Done" : "Cancel";
        DefaultButton = ContentDialogButton.Close;
        // Give the list a finite remaining-height row. A vertical StackPanel
        // measures it without a height limit, leaving the outer dialog to clip
        // items that the list incorrectly considers already visible.
        content.RowDefinitions.Add(new() { Height = GridLength.Auto });
        content.RowDefinitions.Add(new() { Height = new GridLength(1, GridUnitType.Star) });
        content.RowDefinitions.Add(new() { Height = GridLength.Auto });
        content.Children.Add(details);
        Grid.SetRow(choices, 1);
        if (!string.IsNullOrWhiteSpace(message))
        {
            var text = new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap };
            if (directDecisionButtons)
            {
                messageViewport = new ScrollViewer { Content = text, MaxHeight = 360, IsTabStop = false,
                    HorizontalScrollMode = ScrollMode.Disabled, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                    VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
                AutomationProperties.SetAutomationId(messageViewport, "Host.Choice.Message");
                AutomationProperties.SetName(messageViewport, "Dialog details. Scroll to read more.");
                details.Children.Add(messageViewport);
            }
            else details.Children.Add(text);
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
                var item = CreateChoice(entries[index]);
                AutomationProperties.SetAutomationId(item, "Host.Choice.Option." + index);
                choices.Items.Add(item);
            }
            choices.SelectedIndex = 0;

            if (entries.Count > 1) content.Children.Add(choices);
            // List entries are already direct actions (pointer, Enter and A).
            // A second generic "Open" button obscures destructive action intent.
            if (entries.Count == 1)
            {
                PrimaryButtonText = entries[0].Label;
                PrimaryButtonClick += (_, args) => { args.Cancel = true; Choose(0); };
            }
        }
        choices.ItemClick += (_, args) => { if (args.ClickedItem is ListViewItem { Tag: string id }) Choose(this.entries.FindIndex(entry => entry.Id == id)); };
        choices.SelectionChanged += (_, _) => SelectionContextChanged?.Invoke();
        choices.GotFocus += (_, _) => SelectionContextChanged?.Invoke();
        choices.LostFocus += (_, _) => SelectionContextChanged?.Invoke();
        Content = content;
        Opened += (_, _) =>
        {
            opened = true;
            (GetTemplateChild("CloseButton") as Control)?.Focus(FocusState.Keyboard);
            QueueVisibleChoices();
        };
        choices.LayoutUpdated += ChoicesLayoutUpdated;
        Closed += (_, _) => { opened = false; choices.LayoutUpdated -= ChoicesLayoutUpdated; };
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
    private ListViewItem CreateChoice(HostDialogChoice entry)
    {
        UIElement label = new TextBlock { Text = entry.Label, TextWrapping = TextWrapping.Wrap, MaxLines = 5 };
        if (entry.Group is { } group)
            label = new StackPanel { Spacing = 4, Children = {
                new TextBlock { Text = group, FontSize = 12, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, Opacity = .8 }, label } };
        var item = new ListViewItem { Content = choiceContent?.Invoke(entry) ?? label,
            Tag = entry.Id, HorizontalContentAlignment = HorizontalAlignment.Stretch };
        AutomationProperties.SetAutomationId(item, "Host.Choice.Item." + entry.Id);
        AutomationProperties.SetName(item, entry.Group is { } groupName ? groupName + ". " + entry.Label : entry.Label);
        item.EffectiveViewportChanged += (_, _) => QueueVisibleChoices();
        return item;
    }
    private void ChoicesLayoutUpdated(object? sender, object args) => QueueVisibleChoices();
    private void QueueVisibleChoices()
    {
        if (!opened || reportingQueued || ChoiceShown is null || reportedChoices.Count == entries.Count) return;
        reportingQueued = DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
        {
            reportingQueued = false;
            if (!opened || !choices.IsLoaded || choices.ActualWidth <= 0 || choices.ActualHeight <= 0) return;
            // Opened can precede the first list layout. Inspect after layout and
            // viewport changes, and call native providers outside XAML layout.
            foreach (var item in choices.Items.OfType<ListViewItem>())
            {
                if (!item.IsLoaded || item.Tag is not string id || reportedChoices.Contains(id)) continue;
                var rect = item.TransformToVisual(choices).TransformBounds(new(0, 0, item.ActualWidth, item.ActualHeight));
                rect.Intersect(new(0, 0, choices.ActualWidth, choices.ActualHeight));
                if (rect.Width > 0 && rect.Height > 0 && reportedChoices.Add(id)) ChoiceShown?.Invoke(id);
            }
        });
    }
    internal void RemoveChoice(string id)
    {
        var index = entries.FindIndex(entry => entry.Id == id);
        if (index < 0) return;
        var focused = ListFocused();
        var selected = choices.SelectedIndex;
        reportedChoices.Remove(id);
        entries.RemoveAt(index); choices.Items.RemoveAt(index);
        if (entries.Count == 0)
        {
            choices.SelectedIndex = -1;
            if (focused) (GetTemplateChild("CloseButton") as Control)?.Focus(FocusState.Keyboard);
            return;
        }
        choices.SelectedIndex = Math.Clamp(selected - (index < selected ? 1 : 0), 0, entries.Count - 1);
        if (focused && index == selected) (choices.SelectedItem as Control)?.Focus(FocusState.Keyboard);
    }
    internal void UseActionList()
    {
        PrimaryButtonText = "";
        if (!content.Children.Contains(choices)) content.Children.Add(choices);
    }
    internal void AppendChoice(HostDialogChoice entry, string? beforeId = null)
    {
        if (entries.Any(value => value.Id == entry.Id)) return;
        var index = beforeId is null ? -1 : entries.FindIndex(value => value.Id == beforeId);
        if (index < 0) index = entries.Count;
        entries.Insert(index, entry); choices.Items.Insert(index, CreateChoice(entry));
    }
    internal void UpdateChoice(HostDialogChoice entry)
    {
        var index = entries.FindIndex(value => value.Id == entry.Id);
        if (index < 0) return;
        if (entries[index] == entry) return;
        entries[index] = entry;
        if (choices.Items[index] is ListViewItem accessible) AutomationProperties.SetName(accessible, entry.Label);
        if (choiceContent is not null && choices.Items[index] is ListViewItem item) { item.Content = choiceContent(entry); return; }
        if (choices.Items[index] is ListViewItem { Content: TextBlock text }) text.Text = entry.Label;
    }

    protected override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        if (panelAnchor is not null) PositionPendingPanel();
        // Theme application can replace the button after Opened. Keep the ID
        // on the current template instance rather than the discarded control.
        if (GetTemplateChild("PrimaryButton") is Control primary)
            AutomationProperties.SetAutomationId(primary, "Host.Choice.Primary");
        if (GetTemplateChild("SecondaryButton") is Control secondary)
            AutomationProperties.SetAutomationId(secondary, "Host.Choice.Secondary");
        if (GetTemplateChild("CloseButton") is Control close)
            AutomationProperties.SetAutomationId(close, "Host.Choice.Close");
    }

    private void Choose(int index)
    {
        if (index < 0 || index >= entries.Count) return;
        if (KeepOpenOnChoice?.Invoke(entries[index].Id) == true) return;
        SelectedId = entries[index].Id;
        ControllerArmed = false;
        Hide();
    }

    internal void MoveFocus(FocusNavigationDirection direction)
    {
        if (messageViewport is not null && IsWithinFocused(messageViewport) && (direction is FocusNavigationDirection.Up or FocusNavigationDirection.Down) &&
            ScrollMessage(direction == FocusNavigationDirection.Up ? -64 : 64)) return;
        if (Presentation.DialogChoiceNavigation.Move(choices, direction)) return;
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
        else if (button == ControllerButton.X && SupportsDismissChoice && ListFocused() && choices.SelectedItem is ListViewItem { Tag: string id })
            DismissChoiceRequested?.Invoke(id);
        else if (button == ControllerButton.A)
        {
            if (ListFocused()) Choose(choices.SelectedIndex);
            else if (rememberChoice is not null && IsWithinFocused(rememberChoice))
                rememberChoice.IsChecked = !RememberChoice;
            else if (FocusManager.GetFocusedElement(XamlRoot) is Button focused)
            {
                (FrameworkElementAutomationPeer.CreatePeerForElement(focused)?.GetPattern(PatternInterface.Invoke) as IInvokeProvider)?.Invoke();
            }
        }
    }

    private bool ListFocused() => Presentation.DialogChoiceNavigation.ContainsFocus(choices);
    private bool IsWithinFocused(DependencyObject target)
    {
        for (var node = FocusManager.GetFocusedElement(XamlRoot) as DependencyObject; node is not null; node = VisualTreeHelper.GetParent(node))
            if (ReferenceEquals(node, target)) return true;
        return false;
    }
}
