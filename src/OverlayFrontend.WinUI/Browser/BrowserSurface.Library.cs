using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using WidgetRail.OverlayFrontend.WinUI.Presentation;

namespace WidgetRail.OverlayFrontend.WinUI.Browser;

internal sealed partial class BrowserSurface
{
    private readonly BrowserLibraryStore? library;
    private BrowserLibraryDialog? libraryDialog;
    private bool toolbarFocused, chromeOwnedA;
    private Button? rememberedToolbar;
    private readonly Dictionary<Button, Action> chromeCommands = [];
    internal bool IsToolbarFocused => toolbarFocused;

    private void ToggleToolbar()
    {
        CancelPointer(); wheelX = wheelY = 0; cursorDirty = false;
        if (toolbarFocused)
        { toolbarFocused = false; interacting = browsing = true; FocusInteraction?.Invoke(); }
        else
        {
            toolbarFocused = true; interacting = true; browsing = false;
            (rememberedToolbar is { IsEnabled: true, Visibility: Visibility.Visible } remembered ? remembered : addressButton).Focus(FocusState.Keyboard);
        }
        DrawCursor(); InteractionChanged?.Invoke();
    }
    private void MoveToolbar(FocusNavigationDirection direction)
    {
        if (direction == FocusNavigationDirection.Down) { ToggleToolbar(); return; }
        if (direction is not (FocusNavigationDirection.Left or FocusNavigationDirection.Right)) return;
        var available = buttons.Where(button => button.IsEnabled && button.Visibility == Visibility.Visible).ToArray();
        var index = Array.IndexOf(available, FocusManager.GetFocusedElement(XamlRoot) as Button);
        var next = Math.Clamp(index + (direction == FocusNavigationDirection.Left ? -1 : 1), 0, available.Length - 1);
        rememberedToolbar = available[next];
        available[next].Focus(FocusState.Keyboard);
    }
    private (string Url, string Title)? pendingVisit;
    private bool recordingVisit;
    private void RecordVisit()
    {
        if (library is null || IsProviderContent || core is not { } current || !WidgetProtocol.WebBrowserDocument.IsWebUrl(current.Source)) return;
        pendingVisit = (current.Source, current.DocumentTitle);
        if (recordingVisit) return;
        recordingVisit = true;
        Run(async () =>
        {
            try
            {
                // A webpage can change its URL rapidly. Keep only one queued update,
                // rather than building an unbounded queue of disk writes.
                while (!retired && pendingVisit is not null)
                {
                    await Task.Delay(200, lifetime.Token);
                    if (pendingVisit is not { } visit) continue;
                    pendingVisit = null;
                    await library.RecordAsync(visit.Url, visit.Title, lifetime.Token);
                    if (!retired) UpdateBookmark();
                }
            }
            finally { recordingVisit = false; }
        });
    }
    private async Task ToggleBookmarkAsync()
    {
        if (library is null || core is not { } current || !WidgetProtocol.WebBrowserDocument.IsWebUrl(current.Source)) return;
        var url = current.Source;
        if (!library.IsBookmarked(url) && library.Bookmarks.Count >= BrowserLibraryStore.MaximumBookmarks)
        { ShowStatus("You have 100 bookmarks. Remove a saved bookmark before adding another."); return; }
        await library.ToggleBookmarkAsync(url, current.DocumentTitle, lifetime.Token);
        if (!retired) { UpdateBookmark(); ShowStatus(library.IsBookmarked(url) ? "Bookmark saved." : "Bookmark removed."); }
    }
    private async Task ShowLibraryAsync(bool bookmarks)
    {
        if (library is null || !input || retired || HasDialog) return;
        CancelPointer(); wheelX = wheelY = 0; cursorDirty = false;
        var returnButton = bookmarks ? this.bookmarks : history;
        rememberedToolbar = returnButton;
        var dialog = new BrowserLibraryDialog(bookmarks, bookmarks ? library.Bookmarks : library.History) { XamlRoot = XamlRoot };
        libraryDialog = dialog; DrawCursor(); InteractionChanged?.Invoke();
        try
        {
            using var theme = NativePopupTheme.Dialog(dialog, this);
            await dialog.ShowAsync();
            if (!input || retired) return;
            if (dialog.ClearHistory) { pendingVisit = null; await library.ClearHistoryAsync(lifetime.Token); }
            if (input && !retired && dialog.SelectedUrl is { } url && WidgetProtocol.WebBrowserDocument.IsWebUrl(url))
            { libraryDialog = null; toolbarFocused = false; browsing = interacting = true; core?.Navigate(url); }
        }
        finally
        {
            if (ReferenceEquals(libraryDialog, dialog)) libraryDialog = null;
            if (input && !retired)
            {
                if (toolbarFocused) { rememberedToolbar = returnButton; returnButton.Focus(FocusState.Keyboard); }
                else FocusInteraction?.Invoke();
                DrawCursor(); InteractionChanged?.Invoke();
            }
        }
    }
}
