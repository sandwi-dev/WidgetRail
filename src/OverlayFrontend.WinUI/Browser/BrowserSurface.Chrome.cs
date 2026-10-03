using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Input;
using WidgetRail.OverlayFrontend.WinUI.Presentation;

namespace WidgetRail.OverlayFrontend.WinUI.Browser;

internal sealed partial class BrowserSurface
{
    private readonly Grid header = new() { ColumnSpacing = 4, Padding = new(8, 4, 8, 4) };
    private readonly ProgressBar progress = new() { Height = 2, IsIndeterminate = true, IsHitTestVisible = false };
    private Button back = null!, forward = null!, reload = null!, addressButton = null!, bookmark = null!, bookmarks = null!, history = null!, external = null!;
    private readonly TextBlock zoomLabel = new() { Text = "100%", VerticalAlignment = VerticalAlignment.Center };
    private IDisposable? chromeTheme;

    private void CreateChrome()
    {
        RowDefinitions.Add(new() { Height = GridLength.Auto });
        RowDefinitions.Add(new() { Height = new(2) });
        RowDefinitions.Add(new() { Height = new(1, GridUnitType.Star) });
        RowDefinitions.Add(new() { Height = GridLength.Auto });
        for (var i = 0; i < 9; ++i) header.ColumnDefinitions.Add(new() { Width = i == 2 ? new(1, GridUnitType.Star) : GridLength.Auto });
        back = Add("Previous page", "Browser.Back", Symbol.Back, 0, () => { if (core?.CanGoBack == true) core.GoBack(); });
        forward = Add("Forward", "Browser.Forward", Symbol.Forward, 1, () => { if (core?.CanGoForward == true) core.GoForward(); });
        addressButton = Add("Search Google or enter a URL", "Browser.Address", Symbol.Globe, 2, () => Run(() => EditAsync(true)));
        addressButton.HorizontalContentAlignment = HorizontalAlignment.Stretch;
        addressButton.Content = address; address.Text = document.Url == WidgetProtocol.WebBrowserDocument.StartPage ? "Search Google or enter a URL" : new Uri(document.Url).IdnHost;
        reload = Add("Reload", "Browser.Reload", Symbol.Refresh, 3, Reload);
        var zoom = Add("Reset page zoom to 100%", "Browser.Zoom", Symbol.Zoom, 4, () => { requestedZoom = 1; RefreshZoom(); });
        zoom.Content = zoomLabel;
        bookmark = Add("Save bookmark", "Browser.Bookmark", Symbol.OutlineStar, 5, () => Run(ToggleBookmarkAsync));
        bookmarks = Add("Bookmarks", "Browser.Bookmarks", Symbol.Library, 6, () => Run(() => ShowLibraryAsync(true)));
        history = Add("History", "Browser.History", Symbol.Clock, 7, () => Run(() => ShowLibraryAsync(false)));
        external = Add("Open externally", "Browser.External", Symbol.Link, 8, () => Run(OpenExternallyAsync));
        if (IsProviderContent)
        {
            // Restricted documents have no navigation chrome, including during
            // environment/controller startup. The progress row is independent.
            header.Visibility = Visibility.Collapsed;
            bookmark.Visibility = bookmarks.Visibility = history.Visibility = Visibility.Collapsed;
        }
        Children.Add(header); SetRow(progress, 1); Children.Add(progress);
        web.DefaultBackgroundColor = Microsoft.UI.Colors.Transparent;
        viewport.Children.Add(web); viewport.Children.Add(welcome); viewport.Children.Add(cursor); SetRow(viewport, 2); Children.Add(viewport);
        SetRow(status, 3); Children.Add(status);
        AutomationProperties.SetLiveSetting(status, Microsoft.UI.Xaml.Automation.Peers.AutomationLiveSetting.Polite);
        UpdateChrome();
    }

    private Button Add(string label, string id, Symbol symbol, int column, Action command)
    {
        var button = new Button { Content = new SymbolIcon(symbol), MinWidth = 32, MinHeight = 32, Padding = new(8, 4, 8, 4),
            HorizontalAlignment = HorizontalAlignment.Stretch, Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent), BorderThickness = new(0) };
        chromeCommands[button] = command;
        button.UseSystemFocusVisuals = true;
        button.GotFocus += (_, _) =>
        {
            if (!input || HasDialog || XamlRoot is null || !ReferenceEquals(FocusManager.GetFocusedElement(XamlRoot), button)) return;
            CancelPointer(); toolbarFocused = true; rememberedToolbar = button; interacting = true; browsing = false;
            DrawCursor(); InteractionChanged?.Invoke();
        };
        button.Click += (_, _) => { if (input && !retired && !HasDialog) command(); };
        AutomationProperties.SetName(button, label); AutomationProperties.SetAutomationId(button, id);
        ToolTipService.SetToolTip(button, label); SetColumn(button, column); header.Children.Add(button); buttons.Add(button);
        return button;
    }
    private void Reload()
    {
        if (core is null || faulted) return;
        if (loading) core.Stop(); else core.Reload();
    }
    private void UpdateChrome()
    {
        header.Visibility = IsProviderContent ? Visibility.Collapsed : Visibility.Visible;
        back.IsEnabled = !faulted && core?.CanGoBack == true;
        forward.IsEnabled = !faulted && core?.CanGoForward == true;
        reload.IsEnabled = core is not null && !faulted;
        reload.Content = new SymbolIcon(loading ? Symbol.Cancel : Symbol.Refresh);
        var label = loading ? "Stop loading" : "Reload";
        AutomationProperties.SetName(reload, label); ToolTipService.SetToolTip(reload, label);
        progress.IsIndeterminate = (loading || core is null) && !faulted;
        progress.Visibility = progress.IsIndeterminate ? Visibility.Visible : Visibility.Collapsed;
        UpdateBookmark();
        if (external is not null) external.IsEnabled = core is not null && !faulted && WidgetProtocol.WebBrowserDocument.IsWebUrl(core.Source);
        InteractionChanged?.Invoke();
    }
    private void UpdateBookmark()
    {
        if (bookmark is null) return;
        bookmark.IsEnabled = library is not null && !faulted && core is not null && WidgetProtocol.WebBrowserDocument.IsWebUrl(core.Source);
        var saved = core is not null && library?.IsBookmarked(core.Source) == true;
        bookmark.Content = new SymbolIcon(saved ? Symbol.SolidStar : Symbol.OutlineStar);
        var label = saved ? "Remove bookmark" : "Save bookmark";
        AutomationProperties.SetName(bookmark, label); ToolTipService.SetToolTip(bookmark, label);
    }
    private void ShowStatus(string? value)
    {
        status.Text = value ?? "";
        status.Visibility = string.IsNullOrEmpty(value) ? Visibility.Collapsed : Visibility.Visible;
    }
    private void AttachTheme()
    {
        chromeTheme?.Dispose();
        chromeTheme = NativePopupTheme.Browser(this);
    }
    internal void ApplyTheme(Brush background, Brush text, Brush muted, Brush selected, Brush focus,
        double size, Windows.UI.Text.FontWeight weight, FontFamily? family, double radius)
    {
        header.Background = background; viewport.Background = background; welcome.Foreground = muted; welcome.FontSize = size;
        status.Foreground = text; status.FontSize = size; status.FontWeight = weight;
        address.Foreground = muted; address.FontSize = size; address.FontWeight = weight;
        zoomLabel.Foreground = muted; zoomLabel.FontSize = size; zoomLabel.FontWeight = weight;
        progress.Foreground = focus; cursor.Background = text; cursor.BorderBrush = background;
        foreach (var button in buttons)
        {
            button.Foreground = text; button.FontWeight = weight; button.CornerRadius = new(radius * .6);
            button.Resources["ButtonBackgroundPointerOver"] = selected;
            button.Resources["ButtonBackgroundPressed"] = selected;
            button.Resources["ButtonForegroundDisabled"] = muted;
            button.FocusVisualPrimaryBrush = focus;
            if (family is not null) button.FontFamily = family; else button.ClearValue(Control.FontFamilyProperty);
        }
        if (family is not null) { address.FontFamily = family; status.FontFamily = family; }
        else { address.ClearValue(TextBlock.FontFamilyProperty); status.ClearValue(TextBlock.FontFamilyProperty); }
    }
}
