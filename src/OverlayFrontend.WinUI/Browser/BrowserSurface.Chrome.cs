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
    private readonly StackPanel compactCommands = new() { Orientation = Orientation.Horizontal, Spacing = 4 };
    private readonly ScrollViewer compactCommandScroll = new()
    { HorizontalScrollMode = ScrollMode.Enabled, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
        VerticalScrollMode = ScrollMode.Disabled, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
        IsTabStop = false };
    private Button? closePopupButton;
    private Button? pinPopupButton;
    private bool compactHeader;

    private void CreateChrome()
    {
        RowDefinitions.Add(new() { Height = GridLength.Auto });
        RowDefinitions.Add(new() { Height = new(2) });
        RowDefinitions.Add(new() { Height = new(1, GridUnitType.Star) });
        RowDefinitions.Add(new() { Height = GridLength.Auto });
        for (var i = 0; i < (isPopup ? 12 : 10); ++i) header.ColumnDefinitions.Add(new() { Width = i == 2 ? new(1, GridUnitType.Star) : GridLength.Auto });
        back = Add("Previous page", "Browser.Back", Symbol.Back, 0, () => { if (core?.CanGoBack == true) core.GoBack(); });
        forward = Add("Forward", "Browser.Forward", Symbol.Forward, 1, () => { if (core?.CanGoForward == true) core.GoForward(); });
        addressButton = Add("Search Google or enter a URL", "Browser.Address", Symbol.Globe, 2, () => Run(EditAddressAsync));
        addressButton.HorizontalContentAlignment = HorizontalAlignment.Stretch;
        addressButton.Content = address; address.Text = document.Url == WidgetProtocol.WebBrowserDocument.StartPage ? "Search Google or enter a URL" : new Uri(document.Url).IdnHost;
        reload = Add("Reload", "Browser.Reload", Symbol.Refresh, 3, Reload);
        var zoom = Add("Reset page zoom to 100%", "Browser.Zoom", Symbol.Zoom, 4, () => { requestedZoom = 1; RefreshZoom(); });
        zoom.Content = zoomLabel;
        bookmark = Add("Save bookmark", "Browser.Bookmark", Symbol.OutlineStar, 5, () => Run(ToggleBookmarkAsync));
        bookmarks = Add("Bookmarks", "Browser.Bookmarks", Symbol.Library, 6, () => Run(() => ShowLibraryAsync(true)));
        history = Add("History", "Browser.History", Symbol.Clock, 7, () => Run(() => ShowLibraryAsync(false)));
        external = Add("Open externally", "Browser.External", Symbol.Link, 8, () => Run(OpenExternallyAsync));
        Add("Website permissions and data", "Browser.SiteControls", Symbol.Setting, 9, () => Run(ShowSiteControlsAsync));
        if (isPopup)
        {
            pinPopupButton = Add("Pin pop-out", "Browser.PinPopup", Symbol.Pin, 10, () => { if (RequestPopupPin is { } request) Run(() => request(this)); });
            closePopupButton = Add("Close pop-out", "Browser.ClosePopup", Symbol.Cancel, 11, () => CloseRequested?.Invoke());
        }
        header.SizeChanged += (_, args) => ArrangeChrome(args.NewSize.Width);
        if (IsProviderContent)
        {
            // Restricted documents have no navigation chrome, including during
            // environment/controller startup. The progress row is independent.
            header.Visibility = Visibility.Collapsed;
            bookmark.Visibility = bookmarks.Visibility = history.Visibility = Visibility.Collapsed;
        }
        SetRow(progress, 1); Children.Add(progress);
        web.DefaultBackgroundColor = Microsoft.UI.Colors.Transparent;
        viewport.Children.Add(web); viewport.Children.Add(welcome); viewport.Children.Add(cursor); SetRow(viewport, 2); Children.Add(viewport);
        // On-demand controls overlay the page instead of resizing the native
        // surface (and reflowing a webapp) each time LS is pressed.
        Children.Add(header);
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
            button.StartBringIntoView(new BringIntoViewOptions { AnimationDesired = false });
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
        if (sessionStopped)
        { if (ChangeRunning is { } change) Run(async () => { if (!await change(this, true)) ShowStatus("This webapp could not restart. Stop another background session and try again."); }); return; }
        if (loading) core.Stop(); else core.Reload();
    }
    private void UpdateChrome()
    {
        UpdateChromeVisibility();
        back.IsEnabled = !faulted && !sessionStopped && core?.CanGoBack == true;
        forward.IsEnabled = !faulted && !sessionStopped && core?.CanGoForward == true;
        reload.IsEnabled = core is not null && !faulted;
        reload.Content = new SymbolIcon(loading ? Symbol.Cancel : Symbol.Refresh);
        var label = sessionStopped ? "Restart webapp" : loading ? "Stop loading" : "Reload";
        AutomationProperties.SetName(reload, label); ToolTipService.SetToolTip(reload, label);
        progress.IsIndeterminate = (loading || core is null) && !faulted;
        progress.Visibility = progress.IsIndeterminate ? Visibility.Visible : Visibility.Collapsed;
        UpdateBookmark();
        if (external is not null) external.IsEnabled = core is not null && !faulted && WidgetProtocol.WebBrowserDocument.IsWebUrl(core.Source);
        InteractionChanged?.Invoke();
    }
    private void UpdateChromeVisibility()
    {
        if (pinPopupButton is { } pin)
        {
            pin.Visibility = RequestPopupPin is null ? Visibility.Collapsed : Visibility.Visible;
            var pinned = PopupIsPinned?.Invoke(this) == true;
            var label = pinned ? "Return pop-out to widget" : "Pin pop-out";
            AutomationProperties.SetName(pin, label); ToolTipService.SetToolTip(pin, label);
        }
        var onDemand = document.Options.Chrome == WidgetProtocol.BrowserChrome.OnDemand;
        SetRow(header, onDemand ? 2 : 0);
        header.VerticalAlignment = VerticalAlignment.Top;
        header.Visibility = !IsProviderContent && (!onDemand || toolbarFocused) ? Visibility.Visible : Visibility.Collapsed;
    }
    private void ArrangeChrome(double width)
    {
        if (width <= 0 || IsProviderContent) return;
        var compact = width < 640;
        if (compact == compactHeader) return;
        var focused = XamlRoot is null ? null : FocusManager.GetFocusedElement(XamlRoot) as Button;
        compactHeader = compact;
        header.Children.Clear(); compactCommands.Children.Clear(); compactCommandScroll.Content = null;
        header.ColumnDefinitions.Clear(); header.RowDefinitions.Clear();
        if (compact)
        {
            header.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
            header.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
            header.RowDefinitions.Add(new() { Height = GridLength.Auto });
            header.RowDefinitions.Add(new() { Height = GridLength.Auto });
            SetRow(addressButton, 0); SetColumn(addressButton, 0); header.Children.Add(addressButton);
            if (closePopupButton is { } close) { SetRow(close, 0); SetColumn(close, 1); header.Children.Add(close); }
            foreach (var button in buttons.Where(b => b != addressButton && b != closePopupButton)) compactCommands.Children.Add(button);
            compactCommandScroll.Content = compactCommands; SetRow(compactCommandScroll, 1); SetColumn(compactCommandScroll, 0);
            SetColumnSpan(compactCommandScroll, 2); header.Children.Add(compactCommandScroll);
        }
        else
        {
            for (var i = 0; i < buttons.Count; i++)
            {
                header.ColumnDefinitions.Add(new() { Width = i == 2 ? new(1, GridUnitType.Star) : GridLength.Auto });
                SetRow(buttons[i], 0); SetColumn(buttons[i], i); header.Children.Add(buttons[i]);
            }
        }
        if (focused is not null && buttons.Contains(focused)) focused.Focus(FocusState.Keyboard);
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
    internal Input.ControllerPointerAppearance PointerAppearance => new(
        ColorOf(cursor.Background, 0xFFFFFFFF), ColorOf(cursor.BorderBrush, 0xFF000000));
    private static uint ColorOf(Brush brush, uint fallback)
    {
        if (brush is not SolidColorBrush solid) return fallback;
        var color = solid.Color;
        return ((uint)color.A << 24) | ((uint)color.R << 16) | ((uint)color.G << 8) | color.B;
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
