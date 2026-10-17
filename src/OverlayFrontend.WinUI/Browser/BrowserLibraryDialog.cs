using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using WidgetRail.WidgetProtocol;

namespace WidgetRail.OverlayFrontend.WinUI.Browser;

internal sealed partial class BrowserLibraryDialog : Presentation.FrameworkDialog
{
    private readonly ListView list = new() { MaxHeight = 320, SelectionMode = ListViewSelectionMode.Single, IsItemClickEnabled = true };
    private readonly IReadOnlyList<BrowserLibraryEntry> entries;
    private bool confirming, opened, closing;
    internal string? SelectedUrl { get; private set; }
    internal bool ClearHistory { get; private set; }
    internal BrowserLibraryDialog(bool bookmarks, IReadOnlyList<BrowserLibraryEntry> entries)
    {
        this.entries = entries;
        Input.GamepadKeyBoundary.ObserveDialog(this);
        Title = bookmarks ? "Bookmarks" : "History";
        CloseButtonText = "Close";
        DefaultButton = ContentDialogButton.None;
        AutomationProperties.SetAutomationId(this, "Browser.Library.Dialog");
        AutomationProperties.SetName(list, bookmarks ? "Saved pages" : "Recently visited pages");
        AutomationProperties.SetAutomationId(list, "Browser.Library.List");
        if (entries.Count == 0)
            Content = new TextBlock { Text = bookmarks ? "No bookmarks yet. Use the star in the toolbar to save a page." : "Pages you visit will appear here.", TextWrapping = TextWrapping.Wrap, MaxWidth = 460 };
        else
        {
            foreach (var entry in entries)
            {
                var copy = new StackPanel { Spacing = 4 };
                copy.Children.Add(new TextBlock { Text = entry.Title, TextTrimming = TextTrimming.CharacterEllipsis, MaxLines = 1 });
                copy.Children.Add(new TextBlock { Text = entry.Url, TextTrimming = TextTrimming.CharacterEllipsis, MaxLines = 1, Opacity = .7 });
                var item = new ListViewItem { Content = copy, HorizontalContentAlignment = HorizontalAlignment.Stretch, Tag = entry.Url };
                AutomationProperties.SetAutomationId(item, "Browser.Library.Item." + list.Items.Count);
                AutomationProperties.SetName(item, entry.Title + ", " + entry.Url);
                list.Items.Add(item);
            }
            list.SelectedIndex = 0;
            list.ItemClick += (_, args) =>
            {
                var item = Presentation.DialogChoiceNavigation.ClickedItem(list, args.ClickedItem);
                if (item is null) return;
                // Complete native pointer routing before retiring the popup.
                DispatcherQueue.TryEnqueue(() =>
                {
                    if (opened && !closing && !confirming) Choose(list.Items.IndexOf(item));
                });
            };
            Content = new Grid { MinWidth = 280, MaxWidth = 540, Children = { list } };
            PrimaryButtonText = "Open";
            if (!bookmarks) SecondaryButtonText = "Clear history";
        }
        PrimaryButtonClick += (_, args) =>
        {
            args.Cancel = true;
            if (confirming) { ClearHistory = true; Hide(); }
            else Choose();
        };
        SecondaryButtonClick += (_, args) =>
        {
            args.Cancel = true;
            confirming = true; Title = "Clear browsing history?";
            Content = new TextBlock { Text = "Remove the recent pages saved for this widget. Your bookmarks will remain.", TextWrapping = TextWrapping.Wrap, MaxWidth = 460 };
            PrimaryButtonText = "Clear history"; SecondaryButtonText = ""; CloseButtonText = "Cancel";
            (GetTemplateChild("CloseButton") as Control)?.Focus(FocusState.Keyboard);
        };
        Opened += (_, _) =>
        {
            opened = true;
            if (entries.Count > 0) (list.Items[0] as Control)?.Focus(FocusState.Keyboard);
            else (GetTemplateChild("CloseButton") as Control)?.Focus(FocusState.Keyboard);
        };
        Closing += (_, _) => closing = true;
        Closed += (_, _) => opened = false;
    }
    private void Choose() => Choose(list.SelectedIndex);
    private void Choose(int index)
    {
        if (closing || index < 0 || index >= entries.Count) return;
        SelectedUrl = entries[index].Url; Hide();
    }
    private bool ListFocused() => Presentation.DialogChoiceNavigation.ContainsFocus(list);
    internal void MoveFocus(FocusNavigationDirection direction)
    {
        if (!confirming && Presentation.DialogChoiceNavigation.Move(list, direction)) return;
        FocusManager.TryMoveFocus(direction, new FindNextElementOptions { SearchRoot = this });
    }
    internal void Handle(ControllerButton button, ControllerEventPhase phase)
    {
        if (phase != ControllerEventPhase.Pressed) return;
        if (button == ControllerButton.B) Hide();
        else if (button == ControllerButton.A)
        {
            if (!confirming && ListFocused()) Choose(list.Items.IndexOf(Presentation.DialogChoiceNavigation.FocusedItem(list)));
            else if (FocusManager.GetFocusedElement(XamlRoot) is Button focused)
                (FrameworkElementAutomationPeer.CreatePeerForElement(focused)?.GetPattern(PatternInterface.Invoke) as IInvokeProvider)?.Invoke();
        }
    }
}
