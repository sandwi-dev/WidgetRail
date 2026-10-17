using System.Collections.ObjectModel;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using WidgetRail.WidgetProtocol;

namespace WidgetRail.OverlayFrontend.WinUI.Shell;

internal sealed record HostDialogChoice(string Id, string Label, long ContentRevision = 0, string? Group = null, string? Value = null);
internal sealed record HostChoicePreference(string? CurrentChoiceId = null, string CloseLabel = "Back");

/// <summary>A small host-owned decision surface with the same release/neutral boundary as install dialogs.</summary>
internal sealed partial class HostChoiceDialog : Presentation.FrameworkDialog
{
    private readonly ListView choices = new() { MaxHeight = 320, SelectionMode = ListViewSelectionMode.Single,
        IsItemClickEnabled = true, HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly ObservableCollection<ListViewItem> choiceItems = [];
    private CollectionViewSource? groupedChoices;
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
    private bool opened, closing, reportingQueued, choiceFocusQueued;
    // Action menus opt in. Permission/replacement decisions keep safe Cancel.
    private bool focusFirstOptionOnOpen;
    internal bool FocusFirstOptionOnOpen
    {
        get => focusFirstOptionOnOpen;
        set { focusFirstOptionOnOpen = value; DefaultButton = value ? ContentDialogButton.None : ContentDialogButton.Close; }
    }
    private string? initialChoiceId;
    private bool initialChoiceScrolled;
    // Settings lists focus their current value (or first option) and use Back/Done.
    // Confirmations retain the constructor's safe Cancel policy.
    internal void ConfigurePreferenceList(string? currentChoiceId = null, string closeLabel = "Back")
    {
        UseActionList();
        FocusFirstOptionOnOpen = true;
        initialChoiceId = currentChoiceId;
        CloseButtonText = closeLabel;
    }
#if ENABLE_WIDGET_VALIDATION
    internal bool IsOpenForValidation => opened;
#endif
    private Func<HostDialogChoice, UIElement>? choiceContent;
    internal Func<string, bool>? KeepOpenOnChoice { get; set; }
    internal Func<string, bool>? ChoiceIsActionable { get; set; }
    internal bool FocusedChoiceIsActionable => FocusedChoice() is not { Tag: string id } || ChoiceIsActionable?.Invoke(id) != false;
    internal event Action? SelectionContextChanged;
    internal void SetChoiceContent(Func<HostDialogChoice, UIElement> create)
    {
        choiceContent = create;
        for (var index = 0; index < entries.Count; index++)
            if (choices.Items[index] is ListViewItem item) item.Content = create(entries[index]);
    }
    internal void FocusFirstChoice()
    {
        initialFocusRequired = false;
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
    private bool choiceScrollPending;
    internal bool SupportsScroll => messageViewport is not null || content.Children.Contains(choices);
    internal string? SelectedId { get; private set; }
    internal bool ControllerArmed { get; set; }

    internal HostChoiceDialog(string title, string message, IReadOnlyList<HostDialogChoice> entries, bool directDecisionButtons = false)
    {
        this.entries = entries.ToList();
        choices.ItemsSource = choiceItems;
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
            if (entries.Count is <1 or >2) throw new ArgumentException("Direct decisions require one or two choices.", nameof(entries));
            PrimaryButtonText = entries[0].Label;
            PrimaryButtonClick += (_, args) => { args.Cancel = true; Choose(0); };
            if(entries.Count==2)
            {
                SecondaryButtonText = entries[1].Label;
                SecondaryButtonClick += (_, args) => { args.Cancel = true; Choose(1); };
            }
        }
        else if (entries.Count > 0)
        {
            for (var index = 0; index < entries.Count; index++)
            {
                var item = CreateChoice(entries[index]);
                AutomationProperties.SetAutomationId(item, "Host.Choice.Option." + index);
                choiceItems.Add(item);
            }
            RefreshChoiceGroups();
            // A one-item list is still a list. Only an explicit decision uses footer actions.
            content.Children.Add(choices);
        }
        choices.ItemClick += (_, args) =>
        {
            // WinUI can report the owned item's content (for example its Grid),
            // rather than the explicitly supplied ListViewItem container.
            var item = Presentation.DialogChoiceNavigation.ClickedItem(choices, args.ClickedItem);
            if (item?.Tag is string id)
                DispatcherQueue.TryEnqueue(() =>
                {
                    // Finish the native ItemClick before tearing down its popup
                    // and allowing a follow-up dialog to take focus.
                    if (opened && !closing) Choose(this.entries.FindIndex(entry => entry.Id == id));
                });
        };
        choices.SelectionChanged += (_, _) => SelectionContextChanged?.Invoke();
        choices.GotFocus += (_, _) => QueueChoiceFocusSync();
        choices.LostFocus += (_, _) => QueueChoiceFocusSync();
        Content = content;
        content.AddHandler(UIElement.PointerPressedEvent, new PointerEventHandler((_, _) => initialFocusRequired = false), true);
        content.AddHandler(UIElement.KeyDownEvent, new KeyEventHandler((_, _) => initialFocusRequired = false), true);
        Opened += (_, _) =>
        {
            opened = true;
            closing = false;
            initialFocusRequired = true;
            initialChoiceScrolled = false;
            InitialFocusTarget()?.Focus(FocusState.Keyboard);
            QueueInitialFocus();
            QueueVisibleChoices();
        };
        choices.LayoutUpdated += ChoicesLayoutUpdated;
        Closing += (_, _) => closing = true;
        Closed += (_, _) => { opened = false; LayoutUpdated -= InitialFocusLayoutUpdated; choices.LayoutUpdated -= ChoicesLayoutUpdated; };
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
    private UIElement CreateChoiceContent(HostDialogChoice entry)
    {
        if (choiceContent is not null) return choiceContent(entry);
        UIElement label = new TextBlock { Text = entry.Label, TextWrapping = TextWrapping.Wrap, MaxLines = 5 };
        if (entry.Value is { } value)
        {
            var row = new Grid { ColumnSpacing = 16 };
            row.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
            var current = new TextBlock { Text = value, Opacity = .75, MaxWidth = 140,
                TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(current, 1);
            row.Children.Add(label); row.Children.Add(current); label = row;
        }
        return label;
    }
    private static string ChoiceAccessibleLabel(HostDialogChoice entry) =>
        (entry.Group is { } group ? group + ". " : "") + entry.Label + (entry.Value is { } value ? ", " + value : "");
    private ListViewItem CreateChoice(HostDialogChoice entry)
    {
        var item = new ListViewItem { Content = CreateChoiceContent(entry),
            Tag = entry.Id, HorizontalContentAlignment = HorizontalAlignment.Stretch };
        AutomationProperties.SetAutomationId(item, "Host.Choice.Item." + entry.Id);
        AutomationProperties.SetName(item, ChoiceAccessibleLabel(entry));
        item.EffectiveViewportChanged += (_, _) => QueueVisibleChoices();
        return item;
    }
    private void RefreshChoiceGroups()
    {
        if (!entries.Any(entry => entry.Group is not null))
        {
            if (groupedChoices is null) return;
            choices.ItemsSource = choiceItems;
            groupedChoices = null; choices.GroupStyle.Clear();
            return;
        }
        var focused = FocusedChoice()?.Tag as string;
        var groups = new ObservableCollection<Presentation.DialogChoiceGroup>();
        Presentation.DialogChoiceGroup? current = null;
        for (var index = 0; index < entries.Count; index++)
        {
            if (current is null || entries[index].Group is not null)
            {
                current = new(entries[index].Group ?? ""); groups.Add(current);
            }
            current.Add(choiceItems[index]);
        }
        groupedChoices = new() { IsSourceGrouped = true, Source = groups };
        if (choices.GroupStyle.Count == 0)
        {
            choices.GroupStyle.Add(new GroupStyle { HidesIfEmpty = true,
                HeaderTemplate = (DataTemplate)Application.Current.Resources["HostChoiceGroupHeaderTemplate"],
                HeaderContainerStyle = (Style)Application.Current.Resources["WidgetIndexedListHeaderContainer"] });
            choices.ItemsPanel = (ItemsPanelTemplate)Application.Current.Resources["HostChoiceGroupedPanel"];
        }
        choices.ItemsSource = groupedChoices.View;
        if (focused is not null) choiceItems.FirstOrDefault(item => item.Tag as string == focused)?.Focus(FocusState.Keyboard);
    }

    private void QueueChoiceFocusSync()
    {
        if (choiceFocusQueued || closing) return;
        // Native focus changes during modal teardown cannot safely mutate a
        // Selector. Reconcile once the focus transaction has completed.
        choiceFocusQueued = DispatcherQueue.TryEnqueue(() =>
        {
            choiceFocusQueued = false;
            if (!opened || closing) return;
            choices.SelectedIndex = FocusedChoiceIndex();
            SelectionContextChanged?.Invoke();
        });
    }
    private int FocusedChoiceIndex()
    {
        ListViewItem? focused = null;
        var node = XamlRoot is null ? null : FocusManager.GetFocusedElement(XamlRoot) as DependencyObject;
        while (node is not null && !ReferenceEquals(node, choices))
        {
            if (node is ListViewItem item) focused = item;
            node = VisualTreeHelper.GetParent(node);
        }
        // Focus can still refer to a retiring container during removal or
        // native modal teardown. Only select an item still in this collection.
        return ReferenceEquals(node, choices) && focused is not null ? choices.Items.IndexOf(focused) : -1;
    }
    private ListViewItem? FocusedChoice() => FocusedChoiceIndex() is var index && index >= 0 ? choices.Items[index] as ListViewItem : null;
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
        var selected = focused ? FocusedChoiceIndex() : -1;
        reportedChoices.Remove(id);
        entries.RemoveAt(index); choiceItems.RemoveAt(index); RefreshChoiceGroups();
        if (entries.Count == 0)
        {
            choices.SelectedIndex = -1;
            if (focused) (GetTemplateChild("CloseButton") as Control)?.Focus(FocusState.Keyboard);
            return;
        }
        choices.SelectedIndex = focused ? Math.Clamp(selected - (index < selected ? 1 : 0), 0, entries.Count - 1) : -1;
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
        entries.Insert(index, entry); choiceItems.Insert(index, CreateChoice(entry)); RefreshChoiceGroups();
    }
    internal void UpdateChoice(HostDialogChoice entry)
    {
        var index = entries.FindIndex(value => value.Id == entry.Id);
        if (index < 0) return;
        if (entries[index] == entry) return;
        var groupChanged = entries[index].Group != entry.Group;
        entries[index] = entry;
        if (choices.Items[index] is ListViewItem item)
        {
            AutomationProperties.SetName(item, ChoiceAccessibleLabel(entry));
            if (choiceContent is null && entry.Group is null && entry.Value is null && item.Content is TextBlock text) text.Text = entry.Label;
            else item.Content = CreateChoiceContent(entry);
        }
        if (groupChanged) RefreshChoiceGroups();
    }

    protected override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        if (panelAnchor is not null) PositionPendingPanel();
        // Theme application can replace the button after Opened. Keep the ID
        // on the current template instance rather than the discarded control.
        foreach (var (part, id, property) in new[] {
            ("PrimaryButton", "Primary", nameof(PrimaryButtonText)),
            ("SecondaryButton", "Secondary", nameof(SecondaryButtonText)),
            ("CloseButton", "Close", nameof(CloseButtonText)) })
        {
            if (GetTemplateChild(part) is not Button button) continue;
            AutomationProperties.SetAutomationId(button, "Host.Choice." + id);
            // Destination/action labels must remain readable in narrow dialogs.
            // Keep native buttons and their focus/automation/activation behavior.
            var label = new TextBlock { TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center };
            label.SetBinding(TextBlock.TextProperty, new Binding { Source = this, Path = new PropertyPath(property), Mode = BindingMode.OneWay });
            button.Content = label;
        }
        if (opened) QueueInitialFocus();
    }

    private bool initialFocusQueued, initialFocusRequired;
    private Control? InitialFocusTarget()
    {
        if (!FocusFirstOptionOnOpen) return GetTemplateChild("CloseButton") as Control;
        if (content.Children.Contains(choices) && choices.Items.Count > 0)
        {
            var target = choices.Items.OfType<ListViewItem>().FirstOrDefault(item => item.Tag as string == initialChoiceId)
                ?? choices.Items[0] as ListViewItem;
            if (initialFocusRequired && !initialChoiceScrolled && target is not null)
            { initialChoiceScrolled = true; choices.ScrollIntoView(target); }
            return target;
        }
        return GetTemplateChild(entries.Count == 1 ? "PrimaryButton" : "CloseButton") as Control;
    }
    private void InitialFocusLayoutUpdated(object? sender, object args) => QueueInitialFocus();
    private void QueueInitialFocus()
    {
        if (!opened || closing || HasCustomPlacement || initialFocusQueued) return;
        LayoutUpdated -= InitialFocusLayoutUpdated;
        LayoutUpdated += InitialFocusLayoutUpdated;
        // Opened can precede native template layout and its own focus pass.
        // Resolve the current template at dispatch time, never a retired button.
        initialFocusQueued = DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
        {
            initialFocusQueued = false;
            if (!opened || closing || HasCustomPlacement) return;
            if (InitialFocusTarget() is not Control { IsLoaded: true, ActualWidth: > 0 } target) return;
            var alreadyInside = IsWithinFocused(content) || new[] { "PrimaryButton", "SecondaryButton", "CloseButton" }
                .Any(name => GetTemplateChild(name) is DependencyObject button && IsWithinFocused(button));
            // WinUI may select the first content tab stop (the Remember box)
            // after Opened. That native default is not a user selection.
            if (!initialFocusRequired && alreadyInside && !IsWithinFocused(target) || target.Focus(FocusState.Keyboard))
            {
                initialFocusRequired = false;
                LayoutUpdated -= InitialFocusLayoutUpdated;
            }
        });
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
        if (closing) return;
        initialFocusRequired = false;
        RevealVisibleChoice();
        if (ListFocused()) choices.SelectedIndex = FocusedChoiceIndex();
        if (messageViewport is not null && IsWithinFocused(messageViewport) && (direction is FocusNavigationDirection.Up or FocusNavigationDirection.Down) &&
            ScrollMessage(direction == FocusNavigationDirection.Up ? -64 : 64)) return;
        if (Presentation.DialogChoiceNavigation.Move(choices, direction)) return;
        FocusManager.TryMoveFocus(direction, new FindNextElementOptions { SearchRoot = this });
    }

    internal void ConstrainMessageViewport(double rootHeight)
    { if (messageViewport is not null) messageViewport.MaxHeight = Math.Clamp(rootHeight - 200, 40, 360); }
    internal void ResetController()
    { ControllerArmed = false; messageScroll.Reset(); choiceScrollPending = false; }
    internal void SampleScroll(short x, short y, long timestamp)
    {
        var delta = messageScroll.Sample(x, y, timestamp);
        if (content.Children.Contains(choices) && FindChoiceViewport() is { } viewport)
        {
            if (delta.Y != 0)
            {
                initialFocusRequired = false;
                var target = Math.Clamp(viewport.VerticalOffset + delta.Y, 0, viewport.ScrollableHeight);
                choiceScrollPending |= Math.Abs(target - viewport.VerticalOffset) > .1;
                viewport.ChangeView(null, target, null, disableAnimation: true);
            }
            if (messageScroll.ShouldSettle(timestamp)) { RevealVisibleChoice(); messageScroll.Reset(); }
        }
        else if (delta.Y != 0) ScrollMessage(delta.Y);
    }
    private ScrollViewer? FindChoiceViewport() => FindViewport(choices);
    private static ScrollViewer? FindViewport(DependencyObject node)
    {
        if (node is ScrollViewer scroll) return scroll;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(node); i++)
            if (FindViewport(VisualTreeHelper.GetChild(node, i)) is { } found) return found;
        return null;
    }
    private void RevealVisibleChoice()
    {
        // Reconcile only a right-stick scroll. Ordinary focus navigation may
        // still be completing ScrollIntoView when A arrives; do not replace it.
        if (!choiceScrollPending) return;
        choiceScrollPending = false;
        if (!ListFocused() || FindChoiceViewport() is not { } viewport || FocusedChoice() is not { } current) return;
        var original = current.TransformToVisual(viewport).TransformBounds(new(0, 0, current.ActualWidth, current.ActualHeight));
        if (original.Top >= 0 && original.Bottom <= viewport.ViewportHeight) return;
        var visible = choiceItems.Where(item => item.IsLoaded).Select(item => (Item: item,
            Rect: item.TransformToVisual(viewport).TransformBounds(new(0, 0, item.ActualWidth, item.ActualHeight))))
            .Where(value => value.Rect.Top >= 0 && value.Rect.Bottom <= viewport.ViewportHeight)
            .OrderBy(value => Math.Abs(value.Rect.Top - original.Top)).FirstOrDefault();
        visible.Item?.Focus(FocusState.Keyboard);
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
        if (closing || !ControllerArmed || phase != ControllerEventPhase.Pressed) return;
        initialFocusRequired = false;
        if (button == ControllerButton.B) { ControllerArmed = false; Hide(); }
        else if (button == ControllerButton.X && SupportsDismissChoice && FocusedChoice() is { Tag: string id })
            DismissChoiceRequested?.Invoke(id);
        else if (button == ControllerButton.A)
        {
            if (ListFocused()) { RevealVisibleChoice(); Choose(FocusedChoiceIndex()); }
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
