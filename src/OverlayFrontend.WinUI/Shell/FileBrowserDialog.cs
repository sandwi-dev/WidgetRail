using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using WidgetRail.OverlayFrontend.WinUI.Presentation;
using WidgetRail.WidgetProtocol;
using Windows.System;

namespace WidgetRail.OverlayFrontend.WinUI.Shell;

internal sealed partial class FileBrowserDialog : FrameworkDialog
{
    private readonly FileBrowserRequest request;
    private readonly CancellationTokenSource lifetime = new();
    private CancellationTokenSource? reading;
    private readonly Grid body = new() { RowSpacing = 8, ColumnSpacing = 12 };
    private readonly UserControl editor = new() { IsTabStop = false };
    private readonly ColumnDefinition placesColumn = new() { Width = new(148) };
    private readonly ListView places = new() { IsItemClickEnabled = true, SelectionMode = ListViewSelectionMode.Single };
    private readonly ListView files = new()
    {
        IsItemClickEnabled = true,
        SelectionMode = ListViewSelectionMode.None,
        HorizontalContentAlignment = HorizontalAlignment.Stretch
    };
    private readonly TextBox location = new() { PlaceholderText = "Location", IsSpellCheckEnabled = false };
    private readonly BreadcrumbBar crumbs = new();
    private readonly Button editLocation = new() { Content = new SymbolIcon(Symbol.Edit), MinWidth = 36 };
    private readonly TextBox search = new() { PlaceholderText = "Search this folder", IsSpellCheckEnabled = false };
    private readonly ComboBox typeFilter = new() { MinWidth = 80, MaxWidth = 124, HorizontalAlignment = HorizontalAlignment.Right };
    private readonly Grid filterBar = new() { ColumnSpacing = 6 };
    private FileBrowserFilter filter;
    private FileBrowserFilter appliedFilter;
    private readonly TextBox filename = new() { PlaceholderText = "File name", IsSpellCheckEnabled = false };
    private readonly TextBlock notice = new() { TextWrapping = TextWrapping.Wrap, Visibility = Visibility.Collapsed };
    private readonly TextBlock empty = new()
    {
        TextWrapping = TextWrapping.Wrap,
        Visibility = Visibility.Collapsed,
        VerticalAlignment = VerticalAlignment.Center,
        HorizontalAlignment = HorizontalAlignment.Center,
        TextAlignment = TextAlignment.Center,
        MaxWidth = 320,
        Margin = new(16),
        Opacity = .85
    };
    private readonly ProgressRing loading = new() { Width = 22, Height = 22, IsActive = false, Visibility = Visibility.Collapsed };
    private readonly Button up = new();
    private readonly Button choosePlace = new() { Content = "Places" };
    private readonly Button review = new() { Content = "Selected files" };
    private readonly Button previous = new() { Content = new SymbolIcon(Symbol.Back), MinWidth = 36 };
    private readonly Button next = new() { Content = new SymbolIcon(Symbol.Forward), MinWidth = 36 };
    private readonly HyperlinkButton windows = new() { Content = "Windows picker…", HorizontalAlignment = HorizontalAlignment.Left };
    private readonly Button clear = new() { Content = "Clear selection" };
    private readonly Grid footer = new() { ColumnSpacing = 8, RowSpacing = 4 };
    private readonly Dictionary<string, string> picked = new(StringComparer.OrdinalIgnoreCase);
    private sealed record Visit(string Path, string Query, FileBrowserFilter Filter, FileBrowserItem? Cursor, string? Focus, double Offset, FileBrowserItem?[] PageHistory);
    private readonly List<Visit> history = [];
    private readonly Stack<FileBrowserItem?> pages = new();
    private readonly RightStickScroll scroll = new();
    private FileBrowserPage? page;
    private FileBrowserItem? cursor;
    private string? anchor, selectedPath;
    private Visit? beforeSearch;
    private string appliedQuery = "";
    private string? pendingSearch;
    private int filterCursor;
    private bool reviewing, showingPlaces, suppressText, closed, confirming, committing;
    private bool enteringLocation;
    private string[] crumbPaths = [];
    private string? replacePath;
    private (DateTime Timestamp, long Size)? replaceVersion;
    internal string[] Result { get; private set; } = [];
    internal bool UseWindows
    {
        get; private set;
    }
    internal WidgetTextEntryDialog? Keyboard
    {
        get; private set;
    }
    internal event Action? GuideChanged;
    internal Input.DialogControllerAdmission Admission { get; } = new();
    private ScrollViewer? fileScroll;
    private sealed record Place(string Name, string Path);

    internal FileBrowserDialog(FileBrowserRequest request)
    {
        this.request = request;
        up.Content = ShortcutLabel(ControllerPrompt.LeftBumper, "Parent");
        Title = request.Title;
        PrimaryButtonText = request.CommitLabel;
        CloseButtonText = "Cancel";
        DefaultButton = ContentDialogButton.None;
        IsPrimaryButtonEnabled = false;
        Resources["ContentDialogMaxWidth"] = 880d;
        Resources["ContentDialogPadding"] = new Thickness(20, 16, 20, 12);
        AutomationProperties.SetAutomationId(this, "Host.Files");
        foreach (var (element, id, label) in new (FrameworkElement, string, string)[]
        {
            (up,"Parent","Parent folder"), (places,"Places","Places"), (choosePlace,"ShowPlaces","Choose location"),
            (location,"Location","Location"), (crumbs,"Breadcrumb","Current folder"), (editLocation,"EditLocation","Enter or paste a location"),
            (search,"Search","Search this folder"), (typeFilter,"Type","Filter by file type"), (filename,"Name","File name"),
            (files,"List","Folder contents"), (review,"Selection","Review selected files"), (windows,"System","Browse in Windows"),
            (previous,"Previous","Previous files"), (next,"Next","Next files"), (clear,"Clear","Clear selection")
        })
        {
            AutomationProperties.SetAutomationId(element, "Host.Files." + id);
            AutomationProperties.SetName(element, label);
        }
        body.ColumnDefinitions.Add(placesColumn);
        body.ColumnDefinitions.Add(new()
        {
            Width = new(1, GridUnitType.Star)
        });
        for (var i = 0; i < 7; i++)
            body.RowDefinitions.Add(new()
            {
                Height = i == 2 ? new(1, GridUnitType.Star) : GridLength.Auto
            });
        var address = new Grid { ColumnSpacing = 8 };
        address.ColumnDefinitions.Add(new()
        {
            Width = GridLength.Auto
        });
        address.ColumnDefinitions.Add(new()
        {
            Width = GridLength.Auto
        });
        address.ColumnDefinitions.Add(new()
        {
            Width = new(1, GridUnitType.Star)
        });
        address.ColumnDefinitions.Add(new()
        {
            Width = GridLength.Auto
        });
        address.Children.Add(up);
        address.Children.Add(choosePlace);
        Grid.SetColumn(choosePlace, 1);
        address.Children.Add(crumbs);
        Grid.SetColumn(crumbs, 2);
        address.Children.Add(location);
        Grid.SetColumn(location, 2);
        address.Children.Add(editLocation);
        Grid.SetColumn(editLocation, 3);
        location.Visibility = Visibility.Collapsed;
        editLocation.Click += (_, _) => BeginLocationEdit();
        crumbs.ItemClicked += (_, args) => { if ((uint)args.Index < crumbPaths.Length) Go(crumbPaths[args.Index]); };
        filterBar.ColumnDefinitions.Add(new()
        {
            Width = new(1, GridUnitType.Star)
        });
        filterBar.ColumnDefinitions.Add(new()
        {
            Width = GridLength.Auto
        });
        filterBar.Children.Add(search);
        filterBar.Children.Add(typeFilter);
        Grid.SetColumn(typeFilter, 1);
        typeFilter.ItemsSource = new[] { "All files", "Images", "Videos", "Audio", "Documents" };
        typeFilter.SelectedIndex = 0;
        typeFilter.Visibility = request.Extension is null && request.Mode is FileBrowserMode.Open or FileBrowserMode.Multiple ? Visibility.Visible : Visibility.Collapsed;
        typeFilter.DropDownOpened += (_, _) => filterCursor = typeFilter.SelectedIndex;
        typeFilter.SelectionChanged += (_, _) =>
        {
            if (typeFilter.SelectedIndex < 0 || typeFilter.SelectedIndex == (int)filter)
                return;
            filter = (FileBrowserFilter)typeFilter.SelectedIndex;
            if (!suppressText && page is not null)
                _ = NavigateAsync(page.Directory, search.Text, null, null, false, false);
        };
        Put(address, 0, 0, 2);
        Put(filterBar, 1, 1);
        Put(places, 1, 0);
        Grid.SetRowSpan(places, 2);
        Put(files, 2, 1);
        Put(empty, 2, 1);
        Put(loading, 2, 1);
        Put(notice, 3, 0, 2);
        Put(filename, 4, 0, 2);
        AutomationProperties.SetLiveSetting(notice, Microsoft.UI.Xaml.Automation.Peers.AutomationLiveSetting.Polite);
        footer.ColumnDefinitions.Add(new()
        {
            Width = new(1, GridUnitType.Star)
        });
        footer.ColumnDefinitions.Add(new()
        {
            Width = GridLength.Auto
        });
        footer.RowDefinitions.Add(new()
        {
            Height = GridLength.Auto
        });
        footer.RowDefinitions.Add(new()
        {
            Height = GridLength.Auto
        });
        var tools = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        tools.Children.Add(review);
        tools.Children.Add(clear);
        tools.Children.Add(previous);
        tools.Children.Add(next);
        footer.Children.Add(tools);
        Put(footer, 5, 0, 2);
        BuildShortcutStrip();
        Put(shortcutStrip, 6, 0, 2);
        places.Footer = windows;
        editor.Content = body;
        Content = editor;
        filename.Visibility = request.Mode == FileBrowserMode.Save ? Visibility.Visible : Visibility.Collapsed;
        filename.Text = request.SuggestedName ?? "";
        // Construct content only for realized list items; recycled containers carry the current row.
        files.ContainerContentChanging += (_, args) =>
        {
            if (args.InRecycleQueue || args.Item is not FileBrowserRow row)
                return;
            var container = args.ItemContainer;
            AutomationProperties.SetName(container, row.AccessibleName);
            AutomationProperties.SetAutomationId(container, "Host.Files.Entry." + args.ItemIndex);
        };
        files.ItemTemplate = (DataTemplate)Application.Current.Resources["FileEntryTemplate"];
        if (request.Mode is FileBrowserMode.Open or FileBrowserMode.Save)
            files.SelectionMode = ListViewSelectionMode.Single;
        files.GotFocus += (_, _) => { if (FocusedRow() is { } focused) { selectedPath = focused.Entry.Path; UpdateActions(); } };
        files.ItemClick += (_, args) => { if (args.ClickedItem is FileBrowserRow row) Point(row); };
        files.DoubleTapped += (_, args) =>
        {
            if (RowAt(args.OriginalSource as DependencyObject) is { } row && (row.Entry.Folder || request.Mode != FileBrowserMode.Multiple))
            {
                args.Handled = true;
                Activate(row);
            }
        };
        places.ItemClick += (_, args) => { if (DialogChoiceNavigation.ClickedItem(places, args.ClickedItem)?.Tag is Place place) Go(place.Path); };
        up.Click += (_, _) => GoToParent();
        choosePlace.Click += (_, _) => { showingPlaces = !showingPlaces; LayoutPicker(); if (showingPlaces) places.Focus(FocusState.Keyboard); };
        review.Click += (_, _) => ToggleReview();
        clear.Click += (_, _) => { picked.Clear(); ToggleReview(); };
        previous.Click += (_, _) => Page(-1);
        next.Click += (_, _) => Page(1);
        windows.Click += (_, _) => { UseWindows = true; Hide(); };
        search.TextChanged += (_, _) => { if (!suppressText && !reviewing && page is not null) _ = SearchAsync(); };
        filename.TextChanged += (_, _) => { ResetConfirmation(); UpdateActions(); };
        location.KeyDown += (_, args) => { if (args.Key == VirtualKey.Enter) { args.Handled = true; Go(location.Text); } };
        PrimaryButtonClick += (sender, args) => { args.Cancel = true; _ = CommitAsync(); };
        AddHandler(PreviewKeyDownEvent, new KeyEventHandler(Keys), true);
        files.CharacterReceived += (_, args) =>
        {
            if (args.Character <= 32 || Down(VirtualKey.Control) || Down(VirtualKey.Menu) || reviewing)
                return;
            search.Focus(FocusState.Keyboard);
            search.Text += char.ConvertFromUtf32((int)args.Character);
            search.SelectionStart = search.Text.Length;
            args.Handled = true;
        };
        Input.GamepadKeyBoundary.ObserveDialog(this);
        Opened += (_, _) =>
        {
            WidgetControllerPrompts.Changed += RefreshShortcutGlyphs;
            RefreshShortcutGlyphs();
            XamlRoot.Changed += RootChanged;
            LayoutPicker();
            Admission.Reset();
            if (!initialized)
            {
                initialized = true;
                _ = LoadPlacesAsync();
                Go(request.StartFolder, false);
            }
        };
        Closed += (_, _) =>
        {
            WidgetControllerPrompts.Changed -= RefreshShortcutGlyphs;
            XamlRoot.Changed -= RootChanged;
            if (editField is null)
                Retire();
        };
        UpdateActions();
    }

    private void Put(FrameworkElement view, int row, int column, int span = 1)
    {
        body.Children.Add(view);
        Grid.SetRow(view, row);
        Grid.SetColumn(view, column);
        Grid.SetColumnSpan(view, span);
    }
    protected override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        if (DialogTemplatePart("CommandSpace") is Grid commands)
            commands.HorizontalAlignment = HorizontalAlignment.Right;
        if (DialogTemplatePart("PrimaryButton") is Button primary)
        {
            primary.MinWidth = 108;
            AutomationProperties.SetAutomationId(primary, "Host.Files.Commit");
        }
        if (DialogTemplatePart("CloseButton") is Button cancel)
        {
            cancel.MinWidth = 96;
            AutomationProperties.SetAutomationId(cancel, "Host.Files.Cancel");
        }
    }
    private void RootChanged(XamlRoot sender, XamlRootChangedEventArgs args) => LayoutPicker();
    private void LayoutPicker()
    {
        if (XamlRoot is null)
            return;
        var area = popupArea ?? XamlRoot.Size;
        body.Width = Math.Clamp(area.Width - 96, 180, 760);
        body.Height = Math.Clamp(area.Height - 232, 150, 460);
        var compact = body.Width < 540;
        LayoutShortcuts(compact);
        placesColumn.Width = new(compact ? 0 : 148);
        choosePlace.Visibility = compact ? Visibility.Visible : Visibility.Collapsed;
        places.Visibility = !compact || showingPlaces ? Visibility.Visible : Visibility.Collapsed;
        Grid.SetColumnSpan(places, compact ? 2 : 1);
        files.Visibility = compact && showingPlaces ? Visibility.Collapsed : Visibility.Visible;
        filterBar.Visibility = reviewing || compact && showingPlaces ? Visibility.Collapsed : Visibility.Visible;
        if (!compact)
            showingPlaces = false;
    }
    private void BeginLocationEdit()
    {
        enteringLocation = true;
        crumbs.Visibility = Visibility.Collapsed;
        location.Visibility = Visibility.Visible;
        location.Focus(FocusState.Keyboard);
        location.SelectAll();
    }
    private void ShowLocation(string directory)
    {
        enteringLocation = false;
        location.Text = directory;
        location.Visibility = Visibility.Collapsed;
        var chain = new Stack<DirectoryInfo>();
        for (var item = new DirectoryInfo(directory); item is not null; item = item.Parent)
            chain.Push(item);
        crumbPaths = chain.Select(item => item.FullName).ToArray();
        crumbs.ItemsSource = chain.Select(item => item.Name).ToArray();
        crumbs.Visibility = Visibility.Visible;
        MarkPlace(directory);
    }
    private void MarkPlace(string directory)
    {
        places.SelectedItem = places.Items.OfType<ListViewItem>().Where(item => item.Tag is Place place &&
            (string.Equals(directory, place.Path, StringComparison.OrdinalIgnoreCase) ||
             directory.StartsWith(Path.TrimEndingDirectorySeparator(place.Path).TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase)))
            .OrderByDescending(item => ((Place)item.Tag).Path.Length).FirstOrDefault();
    }

    private async Task LoadPlacesAsync()
    {
        var token = lifetime.Token;
        try
        {
            var result = await FileBrowserDirectory.InspectAsync(() =>
            {
                var paths = Windows.Storage.UserDataPaths.GetDefault();
                var known = new List<Place> { new("Downloads", paths.Downloads), new("Documents", paths.Documents),
                    new("Pictures", paths.Pictures), new("Videos", paths.Videos), new("Desktop", paths.Desktop) };
                known.AddRange(DriveInfo.GetDrives().Select(d => new Place(d.Name, d.RootDirectory.FullName)));
                return known.Where(p => !string.IsNullOrWhiteSpace(p.Path)).DistinctBy(p => p.Path, StringComparer.OrdinalIgnoreCase).ToList();
            }, token);
            if (!closed)
            {
                places.ItemsSource = result.Select(place =>
                {
                    var visual = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
                    var glyph = place.Name switch
                    {
                        "Downloads" => "\uE896",
                        "Documents" => "\uE8A5",
                        "Pictures" => "\uEB9F",
                        "Videos" => "\uE714",
                        "Desktop" => "\uE7F4",
                        _ => "\uEDA2"
                    };
                    visual.Children.Add(new FontIcon { Glyph = glyph, FontFamily = new("Segoe Fluent Icons"), FontSize = 16, Width = 20, Opacity = .8 });
                    visual.Children.Add(new TextBlock { Text = place.Name, FontWeight = Microsoft.UI.Text.FontWeights.Normal });
                    var item = new ListViewItem { Content = visual, Tag = place };
                    AutomationProperties.SetName(item, place.Name);
                    return item;
                }).ToArray();
                if (page is not null)
                    MarkPlace(page.Directory);
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception error) when (FileError(error)) { if (!closed) Message("Some locations are unavailable. Enter a location to continue."); }
    }

    private static bool FileError(Exception error) => error is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException or System.Security.SecurityException or System.Runtime.InteropServices.COMException;
    private void Message(string text)
    {
        notice.Text = text;
        notice.Visibility = text.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
    }
    private void ResetConfirmation()
    {
        confirming = false;
        replacePath = null;
        replaceVersion = null;
        PrimaryButtonText = request.CommitLabel;
    }
    private void SetSearch(string value)
    {
        suppressText = true;
        try
        {
            search.Text = value;
        }
        finally { suppressText = false; }
    }
    private void Go(string target, bool remember = true) => _ = NavigateAsync(target, "", null, null, remember, true);
    private async Task SearchAsync()
    {
        var query = search.Text;
        // TextChanged can arrive after a programmatic breadcrumb/Back update has completed.
        if (page is not { } current || committing || query == pendingSearch || pendingSearch is null && appliedQuery == query)
            return;
        pendingSearch = query;
        if (appliedQuery.Length == 0 && query.Length != 0 && beforeSearch is null)
            beforeSearch = new(current.Directory, "", appliedFilter, cursor, FocusedRow()?.Entry.Path ?? selectedPath,
                (fileScroll ??= FindViewport(files))?.VerticalOffset ?? 0, pages.Reverse().ToArray());
        var restore = query.Length == 0 ? beforeSearch : null;
        try
        {
            await NavigateAsync(current.Directory, query, restore?.Cursor, restore?.Focus, false, false,
                debounce: true, offset: restore?.Offset, returnTo: restore);
        }
        finally { if (pendingSearch == query) pendingSearch = null; }
    }

    private async Task NavigateAsync(string target, string query, FileBrowserItem? after, string? focus, bool remember, bool enterList,
        bool debounce = false, double? offset = null, Visit? returnTo = null, int pageStep = 0)
    {
        if (closed)
            return;
        reading?.Cancel();
        using var operation = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        reading = operation;
        var token = operation.Token;
        var focusCommit = false;
        ResetConfirmation();
        UpdateActions();
        try
        {
            if (debounce)
                await Task.Delay(140, token);
            _ = ShowSlowReadAsync(operation);
            var full = FileBrowserDirectory.FullPath(target);
            var requestedFilter = returnTo?.Filter ?? filter;
            if (enterList && after is null && await FileBrowserDirectory.InspectAsync(() => File.Exists(full), token))
            {
                if (!FileBrowserDirectory.MatchesType(full, request.Extension) || request.Mode == FileBrowserMode.Folder)
                    throw new ArgumentException("This file does not match the requested type.");
                focus = full;
                full = Path.GetDirectoryName(full)!;
                requestedFilter = FileBrowserFilter.All;
            }
            var result = await FileBrowserDirectory.ReadAsync(full, request, query, after, token, requestedFilter);
            if (focus is not null && result.HasMore && !result.Items.Any(row => row.Path.Equals(focus, StringComparison.OrdinalIgnoreCase)))
            {
                query = Path.GetFileName(focus);
                result = await FileBrowserDirectory.ReadAsync(full, request, query, null, token, requestedFilter);
            }
            token.ThrowIfCancellationRequested();
            if (remember && page is { } old && !old.Directory.Equals(full, StringComparison.OrdinalIgnoreCase))
            {
                if (history.Count == 48)
                    history.RemoveAt(0);
                var returnItem = old.Items.FirstOrDefault(item => item.Folder && string.Equals(item.Path, full, StringComparison.OrdinalIgnoreCase));
                history.Add(new(old.Directory, appliedQuery, appliedFilter, cursor, returnItem?.Path ?? FocusedRow()?.Entry.Path ?? selectedPath,
                    (fileScroll ??= FindViewport(files))?.VerticalOffset ?? 0, pages.Reverse().ToArray()));
            }
            if (page?.Directory != result.Directory || appliedQuery != query || appliedFilter != requestedFilter)
                pages.Clear();
            else if (pageStep > 0)
                pages.Push(cursor);
            else if (pageStep < 0)
                pages.Pop();
            if (page?.Directory != result.Directory || query.Length == 0)
                beforeSearch = null;
            if (returnTo is not null)
            {
                history.Remove(returnTo);
                pages.Clear();
                foreach (var marker in returnTo.PageHistory)
                    pages.Push(marker);
            }
            page = result;
            cursor = after;
            appliedQuery = query;
            appliedFilter = requestedFilter;
            selectedPath = focus;
            anchor = null;
            reviewing = false;
            showingPlaces = false;
            suppressText = true;
            try
            {
                typeFilter.SelectedIndex = (int)requestedFilter;
            }
            finally { suppressText = false; }
            ShowLocation(result.Directory);
            SetSearch(query);
            PresentRows(result.Items, focus, enterList && (!debounce || FocusedRow() is not null));
            if (offset is { } y && (fileScroll ??= FindViewport(files)) is { } viewport)
            {
                files.UpdateLayout();
                viewport.ChangeView(null, Math.Clamp(y, 0, viewport.ScrollableHeight), null, true);
            }
            Message("");
            empty.Text = query.Length != 0 || filter != FileBrowserFilter.All ? "No matching items in this folder." : request.Mode == FileBrowserMode.Folder
                ? $"No subfolders. Select “{request.CommitLabel}” to use this folder." :
                request.Extension is { } extension ? $"No {extension} files in this folder." : "This folder is empty.";
            empty.Visibility = result.Items.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
            focusCommit = request.Mode == FileBrowserMode.Folder && enterList && result.Items.Length == 0;
            LayoutPicker();
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception error) when (FileError(error))
        {
            if (!token.IsCancellationRequested)
            {
                Message(error is ArgumentException ? error.Message : error is UnauthorizedAccessException ? "Access denied. Choose another location." : "This location is unavailable.");
                if (page is { } prior)
                {
                    if (!enteringLocation)
                        location.Text = prior.Directory;
                    SetSearch(appliedQuery);
                }
            }
        }
        finally
        {
            operation.Cancel();
            if (ReferenceEquals(reading, operation))
            {
                reading = null;
                if (!closed)
                {
                    loading.IsActive = false;
                    loading.Visibility = Visibility.Collapsed;
                    UpdateActions();
                    if (focusCommit && IsPrimaryButtonEnabled)
                        (DialogTemplatePart("PrimaryButton") as Control)?.Focus(FocusState.Keyboard);
                }
            }
        }
    }

    private async Task ShowSlowReadAsync(CancellationTokenSource operation)
    {
        var token = operation.Token;
        try { await Task.Delay(170, token); }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { return; }
        if (!closed && ReferenceEquals(reading, operation))
        {
            empty.Visibility = Visibility.Collapsed;
            loading.IsActive = true;
            loading.Visibility = Visibility.Visible;
        }
    }
}
