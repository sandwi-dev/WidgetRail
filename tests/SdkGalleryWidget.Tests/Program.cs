using WidgetRail.Samples.SdkGalleryWidget;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;
using WidgetRail.WidgetStyling;

var tests = new (string Name, Func<Task> Run)[]
{
    ("Every gallery page publishes a valid responsive component tree", PageCoverage),
    ("Controls preserve stable state and requested scrub values", ControlState),
    ("Picker and action sheet own nested B scopes", NestedScopes),
    ("Toast feedback adds no focus or action target", ToastDoesNotTakeFocus),
    ("Trusted artwork preserves provider-neutral encoded bytes", TrustedArtwork),
    ("Manifest and project use the generic capability-free community path", PackageContract),
    ("Gallery WRSS is valid, responsive, and theme-token based", StyleContract),
};

var failures = new List<string>();
foreach (var (name, run) in tests)
{
    try
    {
        await run();
        Console.WriteLine($"PASS {name}");
    }
    catch (Exception exception)
    {
        var detail = exception is ProtocolValidationException protocol
            ? string.Join(Environment.NewLine,
                protocol.Errors.Select(error => $"{error.Path}: {error.Code}: {error.Message}"))
            : exception.ToString();
        failures.Add($"FAIL {name}: {detail}");
        Console.Error.WriteLine(failures[^1]);
    }
}

Console.WriteLine($"SDK Gallery widget tests: {tests.Length - failures.Count}/{tests.Length} passed.");
return failures.Count == 0 ? 0 : 1;

static async Task PageCoverage()
{
    var widget = new SdkGalleryWidget();
    var overview = Snapshot(widget, 1);
    Assert.Equal(ProtocolConstants.BackgroundSurfaceVersion, overview.ProtocolVersion);
    Assert.Equal("gallery.refresh", overview.InitialFocusId);
    Assert.Equal(widget.Navigation.InputScopeId, overview.ActiveInputScopeId);
    Assert.Equal(WidgetSurfaceMode.Standard, overview.Surface!.Mode);
    Assert.Equal(320d, overview.Surface.MinimumWidth);
    Assert.ContainsClass(overview, "wrail-card");
    Assert.ContainsClass(overview, "wrail-alert");
    Assert.ContainsClass(overview, "wrail-empty-state");
    Assert.ContainsClass(overview, "wrail-icon-button");
    Assert.ContainsClass(overview, "wrail-badge");
    Assert.ContainsClass(overview, "wrail-navigation-shell");
    var compactNavigation = Find(overview, "gallery.shell.compact");
    var expandedNavigation = Find(overview, "gallery.shell.rail");
    Assert.Equal(ResponsiveVisibility.CompactOnly, compactNavigation.VisibleWhen);
    Assert.Equal(ResponsiveVisibility.ExpandedOnly, expandedNavigation.VisibleWhen);
    Assert.Equal(1, Nodes(overview.Root).Count(node => node.Id == "gallery.page-scroll"));
    Assert.Equal(4, compactNavigation.Children.Count);
    Assert.Equal(4, expandedNavigation.Children.Count);
    Assert.Equal("gallery.tab.overview",
        compactNavigation.Children.Single(node => node.IsSelected == true).ActionId);
    Assert.Equal("gallery.tab.overview",
        expandedNavigation.Children.Single(node => node.IsSelected == true).ActionId);
    Assert.Equal(1, overview.QuickActions.Count);
    Assert.Equal(ControllerButton.X, overview.QuickActions[0].Button);
    Assert.Equal(null, overview.QuickActions[0].Capability);

    await Act(widget, "gallery.tab.controls");
    var controls = Snapshot(widget, 2);
    Assert.ContainsClass(controls, "wrail-settings-row");
    Assert.ContainsClass(controls, "wrail-switch");
    Assert.ContainsClass(controls, "wrail-scrubber");
    Assert.Equal(ViewNodeKind.Slider, Find(controls, "gallery.scrubber.slider").Kind);

    await Act(widget, "gallery.tab.tiles");
    var tiles = Snapshot(widget, 3);
    Assert.Equal(ProtocolConstants.FocusAssociatedPresentationVersion, tiles.ProtocolVersion);
    Assert.Equal(4, Nodes(tiles.Root).Count(node => node.Kind == ViewNodeKind.ActionSurface));
    Assert.Equal(3, Nodes(tiles.Root).Count(node =>
        node.StyleClasses.SequenceEqual(["wrail-action-surface", "wrail-tile"])));
    var tilesGrid = Find(tiles, "gallery.tiles.grid");
    Assert.Equal(ViewNodeKind.Grid, tilesGrid.Kind);
    Assert.Equal(1, tilesGrid.Children.Count(node => node.Id == "gallery.media"));
    Assert.Equal(1, tilesGrid.Children.Count(node => node.Id == "gallery.app"));
    Assert.Equal(1, tilesGrid.Children.Count(node => node.Id == "gallery.webp"));
    Assert.Equal(1, tilesGrid.Children.Count(node => node.Id == "gallery.poster"));
    var poster = Find(tiles, "gallery.poster");
    Assert.Equal(ActionSurfacePresentation.Poster, poster.ActionSurfacePresentation);
    Assert.True(poster.StyleClasses.SequenceEqual(
        ["wrail-action-surface", "wrail-poster-tile"]));
    Assert.Equal(ImageFit.Cover, Find(tiles, "gallery.poster.artwork").ImageFit);
    var focusPresentation = Find(tiles, "gallery.tiles.presentation");
    Assert.Equal(ViewNodeKind.FocusPresentationSurface, focusPresentation.Kind);
    Assert.Equal("gallery.tiles.presentation.default",
        focusPresentation.DefaultFocusPresentation?.Id);
    Assert.Equal("gallery.media.presentation",
        Find(tiles, "gallery.media").FocusPresentation?.Id);
    Assert.Equal("gallery.app.presentation",
        Find(tiles, "gallery.app").FocusPresentation?.Id);

    await Act(widget, "gallery.tab.utilities");
    var utilities = Snapshot(widget, 4);
    Assert.ContainsClass(utilities, "wrail-code-text");
    Assert.Equal(ViewNodeKind.LoadingIndicator, Find(utilities, "gallery.utilities.loading").Kind);
    Assert.Equal(LoadingIndicatorSize.Compact, Find(utilities, "gallery.utilities.loading").IndicatorSize);
}

static async Task ControlState()
{
    var widget = new SdkGalleryWidget();
    await Act(widget, "gallery.tab.controls");
    Assert.True(widget.CompactMode);
    await Act(widget, "gallery.compact.toggle");
    Assert.False(widget.CompactMode);
    var switched = Snapshot(widget, 1);
    Assert.Equal(null, Find(switched, "gallery.controls.switch").IsSelected);
    Assert.Equal("Off", Find(switched, "gallery.controls.compact.value").Text);
    var density = Find(switched, "gallery.controls.density");
    Assert.Equal(ViewNodeKind.Select, density.Kind);
    Assert.Equal(3, density.SelectOptions.Count);
    Assert.Equal("gallery.density.comfortable",
        density.SelectOptions.Single(option => option.IsSelected).ActionId);
    await widget.OnActionAsync(new WidgetActionEvent(
        "gallery.density.spacious", density.Id));
    Assert.Equal("Spacious", Find(Snapshot(widget, 2), density.Id).AccessibilityValue);

    await widget.OnActionAsync(new WidgetActionEvent(
        "gallery.scrub", "gallery.scrubber.slider", RequestedValue: 150_000));
    Assert.Equal(TimeSpan.FromSeconds(150), widget.Position);
    var scrubbed = Snapshot(widget, 2);
    Assert.Equal(150_000d, Find(scrubbed, "gallery.scrubber.slider").Value);
    Assert.Equal("2:30", Find(scrubbed, "gallery.scrubber.elapsed").Text);
}

static async Task NestedScopes()
{
    var widget = new SdkGalleryWidget();
    await WidgetTestHost.SetLifecycleStateAsync(widget, WidgetLifecycleState.Interactive);
    await Act(widget, "gallery.tab.controls");
    var controlsLifetime = widget.Navigation.RouteCancellationToken;
    await Act(widget, "gallery.picker.open");
    var picker = widget.RenderSnapshot("sdk-gallery.test", 1);
    Assert.Equal(GalleryModal.Picker, widget.Modal);
    Assert.True(controlsLifetime.IsCancellationRequested,
        "Opening a nested gallery route did not cancel parent route work.");
    Assert.Equal(widget.Navigation.InputScopeId, picker.ActiveInputScopeId);
    Assert.Equal("gallery.density.comfortable", picker.InitialFocusId);
    Assert.ContainsShortcut(Find(picker, "gallery.picker"), ControllerButton.B,
        widget.Navigation.BackActionId!);

    var pickerLifetime = widget.Navigation.RouteCancellationToken;
    var handled = await widget.OnControllerInputAsync(new ControllerInputEvent(
        ControllerButton.B,
        ControllerEventPhase.Pressed,
        ControllerInputContext.OpenWidget,
        FocusedElementId: "gallery.density.comfortable",
        ActiveInputScopeId: picker.ActiveInputScopeId,
        SnapshotSequence: picker.Sequence));
    Assert.True(handled);
    await WaitUntil(() => widget.Modal == GalleryModal.None);
    Assert.True(pickerLifetime.IsCancellationRequested,
        "Nested Back did not cancel picker route work.");
    Assert.Equal(GalleryModal.None, widget.Modal);
    Assert.Equal("gallery.picker.open", widget.Render().InitialFocusId);

    await Act(widget, "gallery.sheet.open");
    var sheet = widget.RenderSnapshot("sdk-gallery.test", 2);
    Assert.Equal(widget.Navigation.InputScopeId, sheet.ActiveInputScopeId);
    Assert.ContainsShortcut(Find(sheet, "gallery.sheet"), ControllerButton.B,
        widget.Navigation.BackActionId!);
    Assert.Equal(ActionSheetItemToneClass("danger"),
        Find(sheet, "gallery.sheet.remove").StyleClasses.Single(value => value.EndsWith("danger", StringComparison.Ordinal)));

    await widget.OnActionAsync(new WidgetActionEvent(
        widget.Navigation.BackActionId!,
        "gallery.sheet",
        ControllerButton.B,
        InputScopeId: sheet.ActiveInputScopeId));
    Assert.Equal(GalleryModal.None, widget.Modal);
    await WidgetTestHost.DestroyAsync(widget);
}

static async Task ToastDoesNotTakeFocus()
{
    var widget = new SdkGalleryWidget();
    await Act(widget, "gallery.toast.show");
    var snapshot = widget.RenderSnapshot("sdk-gallery.toast", 1);
    Assert.True(widget.IsToastVisible);
    var toast = Find(snapshot, "gallery.toast");
    Assert.Equal(ViewNodeKind.Row, toast.Kind);
    Assert.Equal(null, toast.ActionId);
    Assert.Equal(null, toast.Focus);
    Assert.Equal(0, toast.Shortcuts.Count);
    Assert.Equal("gallery.refresh", snapshot.InitialFocusId);
    await WidgetTestHost.DestroyAsync(widget);
}

static async Task TrustedArtwork()
{
    var widget = new SdkGalleryWidget();
    await Act(widget, "gallery.tab.tiles");
    var snapshot = Snapshot(widget, 1);
    var artworkHandle = Find(snapshot, "gallery.app.artwork").ArtworkHandle;
    Assert.True(artworkHandle is not null);
    Assert.Equal(ProtocolConstants.FocusAssociatedPresentationVersion, snapshot.ProtocolVersion);
    Assert.Equal(ViewNodeKind.BackgroundSurface, snapshot.Root.Kind);
    Assert.Equal("gallery.root.content", snapshot.Root.Children.Single().Id);
    Assert.Equal(ViewNodeKind.BackgroundSurface, Find(snapshot, "gallery.tiles.surface").Kind);
    var artwork = await widget.OnResolveArtworkAsync(new WidgetArtworkHandle(artworkHandle!));
    Assert.True(artwork is not null);
    Assert.Equal(WidgetArtworkContentType.Png, artwork!.ContentType);
    Assert.True(artwork.Bytes.Span[..8].SequenceEqual(
        new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }));
    var webPHandle = Find(snapshot, "gallery.webp.artwork").ArtworkHandle;
    Assert.Equal("gallery.artwork.sample-webp", webPHandle);
    var webP = await widget.OnResolveArtworkAsync(new WidgetArtworkHandle(webPHandle!));
    Assert.True(webP is not null);
    Assert.Equal(WidgetArtworkContentType.WebP, webP!.ContentType);
    Assert.True(webP.Bytes.Span[..12].SequenceEqual(
        new byte[] { 82, 73, 70, 70, 30, 0, 0, 0, 87, 69, 66, 80 }));
}

static Task PackageContract()
{
    var manifest = ManifestJson.Deserialize(File.ReadAllBytes(
        Path.Combine(AppContext.BaseDirectory, "manifest.json")));
    Assert.Equal(0, WidgetManifestValidator.Validate(manifest).Count);
    Assert.Equal("widgetrail.samples.sdk-gallery", manifest.Id);
    Assert.Equal("widgetrail.samples", manifest.Publisher);
    Assert.Equal("0.1.5", manifest.Version);
    Assert.Equal("dotnet-worker", manifest.Entrypoint.Runtime);
    Assert.Equal("payload/SdkGalleryWidget.dll", manifest.Entrypoint.Assembly);
    Assert.Equal(typeof(SdkGalleryWidget).FullName, manifest.Entrypoint.Type);
    Assert.Equal(0, manifest.Permissions.Count);
    Assert.Equal(0, manifest.OptionalPermissions.Count);
    Assert.Equal("suspend-when-hidden", manifest.ResidencyPolicy!.Mode);

    var project = File.ReadAllText(Path.Combine(
        AppContext.BaseDirectory, "sample", "SdkGalleryWidget.csproj"));
    Assert.True(project.Contains("src\\WidgetSdk\\WidgetSdk.csproj", StringComparison.Ordinal));
    Assert.False(project.Contains("OverlayHost", StringComparison.OrdinalIgnoreCase));
    Assert.False(project.Contains("PlatformBroker", StringComparison.OrdinalIgnoreCase));
    Assert.False(project.Contains("WidgetRuntime", StringComparison.OrdinalIgnoreCase));

    var packageScript = File.ReadAllText(Path.Combine(
        AppContext.BaseDirectory, "sample", "Build-CommunityPackage.ps1"));
    Assert.True(packageScript.Contains("$expectedFiles", StringComparison.Ordinal));
    Assert.True(packageScript.Contains("payload\\SdkGalleryWidget.dll", StringComparison.Ordinal));
    Assert.True(packageScript.Contains("Assert-NoReparsePoint", StringComparison.Ordinal));
    Assert.True(packageScript.Contains("validate $stagingRoot", StringComparison.Ordinal));
    Assert.True(packageScript.Contains("pack $stagingRoot --output $packagePath", StringComparison.Ordinal));
    Assert.False(packageScript.Contains("Copy-Item -Path", StringComparison.OrdinalIgnoreCase),
        "Package staging must not copy wildcard paths.");
    return Task.CompletedTask;
}

static Task StyleContract()
{
    var source = File.ReadAllText(Path.Combine(
        AppContext.BaseDirectory, "styles", "default.wrss"));
    var parsed = WrssParser.Parse(source, "styles/default.wrss");
    Assert.True(parsed.IsValid, string.Join(Environment.NewLine,
        parsed.Diagnostics.Select(item => item.Message)));
    var compiled = WrssThemeCompiler.Compile([parsed.Document]);
    Assert.True(compiled.IsValid, string.Join(Environment.NewLine,
        compiled.Diagnostics.Select(item => item.Message)));

    var root = compiled.Theme!.Resolve(new WrssElement(
        "stack", StyleClasses: new HashSet<string>(["gallery-root"])))!;
    Assert.Equal("100vw", root.Get("width")?.Text);
    Assert.Equal("100vh", root.Get("height")?.Text);
    Assert.Equal("clip", root.Get("overflow")?.Text);
    var grid = compiled.Theme.Resolve(new WrssElement(
        "grid", StyleClasses: new HashSet<string>(["wrail-responsive-grid"])))!;
    Assert.Equal("100%", grid.Get("width")?.Text);
    Assert.False(source.Contains("wrail-navigation-shell", StringComparison.Ordinal),
        "The sample must inherit the platform navigation recipe instead of rebuilding it.");
    Assert.True(source.Contains("var(--surface)", StringComparison.Ordinal));
    Assert.True(source.Contains(":pressed", StringComparison.Ordinal));
    Assert.False(source.Contains("font-family:", StringComparison.OrdinalIgnoreCase));
    Assert.False(source.Split('\n').Any(line =>
        line.TrimStart().StartsWith("position:", StringComparison.OrdinalIgnoreCase)));
    return Task.CompletedTask;
}

static ViewSnapshot Snapshot(SdkGalleryWidget widget, long sequence) =>
    widget.Render().CreateSnapshot("sdk-gallery.test", sequence);

static ValueTask Act(SdkGalleryWidget widget, string action) =>
    widget.OnActionAsync(new WidgetActionEvent(action, action));

static ViewNode Find(ViewSnapshot snapshot, string id) =>
    Nodes(snapshot.Root).Single(node => node.Id == id);

static IEnumerable<ViewNode> Nodes(ViewNode root)
{
    yield return root;
    foreach (var child in root.Children)
    foreach (var nested in Nodes(child))
        yield return nested;
}

static string ActionSheetItemToneClass(string tone) => $"wrail-action-sheet__item--{tone}";

static async Task WaitUntil(Func<bool> predicate)
{
    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
    while (!predicate()) await Task.Delay(10, timeout.Token);
}

static class Assert
{
    public static void True(bool condition, string message = "Expected true.")
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    public static void False(bool condition, string message = "Expected false.") =>
        True(!condition, message);

    public static void Equal<T>(T expected, T actual)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new InvalidOperationException($"Expected '{expected}', got '{actual}'.");
    }

    public static void ContainsClass(ViewSnapshot snapshot, string className)
    {
        if (!Flatten(snapshot.Root).Any(node => node.StyleClasses.Contains(className, StringComparer.Ordinal)))
            throw new InvalidOperationException($"Expected style class '{className}'.");
    }

    public static void ContainsShortcut(ViewNode node, ControllerButton button, string action)
    {
        if (!node.Shortcuts.Any(shortcut => shortcut.Button == button && shortcut.ActionId == action))
            throw new InvalidOperationException($"Expected {button} shortcut '{action}' on '{node.Id}'.");
    }

    private static IEnumerable<ViewNode> Flatten(ViewNode root)
    {
        yield return root;
        foreach (var child in root.Children)
        foreach (var nested in Flatten(child))
            yield return nested;
    }
}
