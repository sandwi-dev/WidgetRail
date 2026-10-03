using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using WidgetRail.WidgetProtocol;

namespace WidgetRail.OverlayFrontend.WinUI.Browser;

internal sealed partial class BrowserLibraryDialog : ContentDialog
{
    private readonly ListView list = new() { MaxHeight = 320, SelectionMode = ListViewSelectionMode.Single, IsItemClickEnabled = true };
    private readonly IReadOnlyList<BrowserLibraryEntry> entries;
    private bool confirming;
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
                AutomationProperties.SetName(item, entry.Title + ", " + entry.Url);
                list.Items.Add(item);
            }
            list.SelectedIndex = 0;
            list.ItemClick += (_, args) => { if (args.ClickedItem is ListViewItem { Tag: string url }) { SelectedUrl = url; Hide(); } };
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
            if (entries.Count > 0) (list.Items[0] as Control)?.Focus(FocusState.Keyboard);
            else (GetTemplateChild("CloseButton") as Control)?.Focus(FocusState.Keyboard);
        };
    }
    private void Choose()
    {
        if (list.SelectedIndex < 0 || list.SelectedIndex >= entries.Count) return;
        SelectedUrl = entries[list.SelectedIndex].Url; Hide();
    }
    private bool ListFocused()
    {
        for (var node = FocusManager.GetFocusedElement(XamlRoot) as DependencyObject; node is not null; node = VisualTreeHelper.GetParent(node))
            if (ReferenceEquals(node, list)) return true;
        return false;
    }
    internal void MoveFocus(FocusNavigationDirection direction)
    {
        if (!confirming && ListFocused() && direction is FocusNavigationDirection.Up or FocusNavigationDirection.Down)
        {
            var next = list.SelectedIndex + (direction == FocusNavigationDirection.Up ? -1 : 1);
            if (next >= 0 && next < entries.Count)
            { list.SelectedIndex = next; list.ScrollIntoView(list.Items[next]); (list.Items[next] as Control)?.Focus(FocusState.Keyboard); return; }
        }
        FocusManager.TryMoveFocus(direction, new FindNextElementOptions { SearchRoot = this });
    }
    internal void Handle(ControllerButton button, ControllerEventPhase phase)
    {
        if (phase != ControllerEventPhase.Pressed) return;
        if (button == ControllerButton.B) Hide();
        else if (button == ControllerButton.A)
        {
            if (!confirming && ListFocused()) Choose();
            else if (FocusManager.GetFocusedElement(XamlRoot) is Button focused)
                (FrameworkElementAutomationPeer.CreatePeerForElement(focused)?.GetPattern(PatternInterface.Invoke) as IInvokeProvider)?.Invoke();
        }
    }
}
