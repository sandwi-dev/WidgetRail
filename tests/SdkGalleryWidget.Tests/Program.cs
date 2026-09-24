using System.Buffers.Binary;
using WidgetRail.Samples.SdkGalleryWidget;
using System.Security.Cryptography;
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
    ("Background gallery proves package artwork fits focus ownership and fallback", BackgroundGallery),
    ("Top-level pages retain one root scope and exact header identities", TopLevelPagesShareRootScope),
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
    Assert.Equal(ProtocolConstants.ControllerGlyphVersion, overview.ProtocolVersion);
    Assert.Equal("gallery.refresh", overview.InitialFocusId);
    Assert.Equal(widget.Navigation.InputScopeId, overview.ActiveInputScopeId);
    Assert.Equal(WidgetSurfaceMode.Standard, overview.Surface!.Mode);
    Assert.Equal(760d, overview.Surface.PreferredWidth);
    Assert.Equal(600d, overview.Surface.PreferredHeight);
    Assert.Equal(320d, overview.Surface.MinimumWidth);
    Assert.Equal(280d, overview.Surface.MinimumHeight);
    Assert.ContainsClass(overview, "wrail-card");
    Assert.ContainsClass(overview, "wrail-alert");
    Assert.ContainsClass(overview, "wrail-empty-state");
    Assert.ContainsClass(overview, "wrail-icon-button");
    Assert.ContainsClass(overview, "wrail-badge");
    Assert.ContainsClass(overview, "wrail-navigation-shell");
    var originalIcon = Find(overview, "gallery.package-icon.original");
    Assert.Equal(WidgetGlyph.Settings, originalIcon.Glyph);
    Assert.Equal("gallery.mark", originalIcon.PackageIcon?.AssetId);
    Assert.Equal(WidgetPackageIconColorMode.OriginalColor,
        originalIcon.PackageIcon?.ColorMode);
    var tintedIcon = Find(overview, "gallery.package-icon.tinted");
    Assert.Equal("gallery.mark", tintedIcon.PackageIcon?.AssetId);
    Assert.Equal(WidgetPackageIconColorMode.ThemeTint,
        tintedIcon.PackageIcon?.ColorMode);
    Assert.True(tintedIcon.IsFocusable && tintedIcon.IsDisabled != true);
    var disabledIcon = Find(overview, "gallery.package-icon.disabled");
    Assert.Equal("gallery.mark", disabledIcon.PackageIcon?.AssetId);
    Assert.Equal(WidgetPackageIconColorMode.ThemeTint,
        disabledIcon.PackageIcon?.ColorMode);
    Assert.True(disabledIcon.IsFocusable && disabledIcon.IsDisabled == true);
    var compactNavigation = Find(overview, "gallery.shell.compact");
    var expandedNavigation = Find(overview, "gallery.shell.rail");
    Assert.Equal(ResponsiveVisibility.CompactOnly, compactNavigation.VisibleWhen);
    Assert.Equal(ResponsiveVisibility.ExpandedOnly, expandedNavigation.VisibleWhen);
    Assert.Equal(1, Nodes(overview.Root).Count(node => node.Id == "gallery.page-scroll"));
    Assert.Equal(7, compactNavigation.Children.Count);
    Assert.Equal(5, compactNavigation.Children.Count(node => node.ActionId is not null));
    Assert.Equal(5, expandedNavigation.Children.Count);
    var previousSection = compactNavigation.Children[0];
    var nextSection = compactNavigation.Children[^1];
    Assert.Equal("gallery.hint.section.previous", previousSection.Id);
    Assert.Equal("gallery.hint.section.next", nextSection.Id);
    Assert.Equal(ViewNodeKind.ControllerGlyph, previousSection.Kind);
    Assert.Equal(ViewNodeKind.ControllerGlyph, nextSection.Kind);
    Assert.True(previousSection.StyleClasses.SequenceEqual(
        ["wrail-controller-glyph", "gallery-section-bumper-key"]));
    Assert.True(nextSection.StyleClasses.SequenceEqual(
        ["wrail-controller-glyph", "gallery-section-bumper-key"]));
    Assert.True(previousSection.ActionId is null && previousSection.Focus is null &&
        previousSection.Shortcuts.Count == 0);
    Assert.True(nextSection.ActionId is null && nextSection.Focus is null &&
        nextSection.Shortcuts.Count == 0);
    Assert.Equal(ControllerPrompt.LeftBumper, previousSection.ControllerPrompt);
    Assert.Equal(ControllerPrompt.RightBumper, nextSection.ControllerPrompt);
    Assert.Equal("Previous section", previousSection.AccessibilityLabel);
    Assert.Equal("Next section", nextSection.AccessibilityLabel);
    var rootHints = Find(overview, "gallery.section.hints");
    Assert.Equal(1, rootHints.Children.Count);
    Assert.Equal("gallery.hint.section.actions", rootHints.Children[0].Id);
    Assert.Equal("gallery.tab.overview",
        compactNavigation.Children.Single(node => node.IsSelected == true).ActionId);
    Assert.Equal("gallery.tab.overview",
        expandedNavigation.Children.Single(node => node.IsSelected == true).ActionId);
    Assert.Equal(1, overview.QuickActions.Count);
    Assert.Equal(ControllerButton.X, overview.QuickActions[0].Button);
    Assert.Equal(null, overview.QuickActions[0].Capability);
    var hints = Find(overview, "gallery.overview.hints");
    Assert.Equal(4, hints.Children.Count);
    Assert.True(hints.Children.All(node => node.Kind == ViewNodeKind.Row &&
        node.ActionId is null && node.Shortcuts.Count == 0));
    Assert.Equal(ControllerPrompt.DPad, Find(overview, "gallery.hint.navigate.key").ControllerPrompt);
    Assert.Equal(ControllerPrompt.A, Find(overview, "gallery.hint.select.key").ControllerPrompt);
    Assert.Equal(ControllerPrompt.B, Find(overview, "gallery.hint.back.key").ControllerPrompt);
    Assert.Equal(ControllerPrompt.RightStickMove, Find(overview, "gallery.hint.scroll.key").ControllerPrompt);

    await Act(widget, "gallery.tab.controls");
    var controls = Snapshot(widget, 2);
    Assert.ContainsClass(controls, "wrail-settings-row");
    Assert.ContainsClass(controls, "wrail-switch");
    Assert.ContainsClass(controls, "wrail-scrubber");
    Assert.Equal(ViewNodeKind.Slider, Find(controls, "gallery.scrubber.slider").Kind);

    await Act(widget, "gallery.tab.tiles");
    var tiles = Snapshot(widget, 3);
    Assert.Equal(ProtocolConstants.ControllerGlyphVersion, tiles.ProtocolVersion);
    Assert.Equal(4, Nodes(tiles.Root).Count(node => node.Kind == ViewNodeKind.ActionSurface));
    Assert.Equal(2, Nodes(tiles.Root).Count(node =>
        node.StyleClasses.SequenceEqual(["wrail-action-surface", "wrail-tile"])));
    Assert.Equal(2, Nodes(tiles.Root).Count(node =>
        node.StyleClasses.SequenceEqual(["wrail-action-surface", "wrail-poster-tile"])));
    var tilesGrid = Find(tiles, "gallery.tiles.grid");
    Assert.Equal(ViewNodeKind.Grid, tilesGrid.Kind);
    Assert.Equal(1, tilesGrid.Children.Count(node => node.Id == "gallery.media"));
    Assert.Equal(1, tilesGrid.Children.Count(node => node.Id == "gallery.app"));
    Assert.Equal(1, tilesGrid.Children.Count(node => node.Id == "gallery.webp"));
    Assert.Equal(1, tilesGrid.Children.Count(node => node.Id == "gallery.poster"));
    var webP = Find(tiles, "gallery.webp");
    Assert.Equal(ActionSurfacePresentation.Poster, webP.ActionSurfacePresentation);
    Assert.True(webP.StyleClasses.SequenceEqual(
        ["wrail-action-surface", "wrail-poster-tile"]));
    Assert.Equal(ImageFit.Cover, Find(tiles, "gallery.webp.artwork").ImageFit);
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
    Assert.Equal("gallery.webp.presentation",
        Find(tiles, "gallery.webp").FocusPresentation?.Id);
    Assert.Equal("gallery.poster.presentation",
        Find(tiles, "gallery.poster").FocusPresentation?.Id);
    Assert.False(string.Equals(
        Find(tiles, "gallery.webp").FocusPresentation?.Id,
        Find(tiles, "gallery.poster").FocusPresentation?.Id,
        StringComparison.Ordinal));

    await Act(widget, "gallery.tab.utilities");
    var utilities = Snapshot(widget, 4);
    Assert.ContainsClass(utilities, "wrail-code-text");
    Assert.Equal("Presentational command", Find(utilities, "gallery.utilities.code-title").Text);
    Assert.Equal(
        "CodeText is nonfocusable and does not provide a clipboard action.",
        Find(utilities, "gallery.utilities.code-description").Text);
    Assert.Equal(ViewNodeKind.Text, Find(utilities, "gallery.utilities.code").Kind);
    Assert.Equal(null, Find(utilities, "gallery.utilities.code").ActionId);
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
    var comfortablePreview = Find(switched, "gallery.controls.density-preview");
    Assert.True(comfortablePreview.StyleClasses.Contains(
        "gallery-density-preview--comfortable", StringComparer.Ordinal));
    Assert.Equal(2, comfortablePreview.Children.Count(node =>
        node.StyleClasses.Contains("gallery-density-preview-row", StringComparer.Ordinal)));
    Assert.Equal(
        "Comfortable · 12 DIP row gap · 14 DIP panel padding",
        Find(switched, "gallery.controls.density-preview.title").Text);
    await widget.OnActionAsync(new WidgetActionEvent(
        "gallery.density.spacious", density.Id));
    var spacious = Snapshot(widget, 2);
    Assert.Equal("Spacious", Find(spacious, density.Id).AccessibilityValue);
    Assert.Equal(density.Id, Find(spacious, "gallery.controls.density").Id);
    Assert.True(Find(spacious, "gallery.controls.density-preview").StyleClasses.Contains(
        "gallery-density-preview--spacious", StringComparer.Ordinal));
    Assert.Equal("Spacious · 20 DIP row gap · 24 DIP panel padding",
        Find(spacious, "gallery.controls.density-preview.title").Text);
    Assert.True(Find(spacious, "gallery.controls.density-preview.row-one").StyleClasses
        .Contains("gallery-density-preview-row--spacious", StringComparer.Ordinal));
    Assert.Equal(3, Find(spacious, "gallery.controls.density-preview.row-one").Children.Count);
    Assert.Equal(3, Find(spacious, "gallery.controls.density-preview.row-two").Children.Count);

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
    var controlsBeforePicker = widget.RenderSnapshot("sdk-gallery.test", 1);
    var handled = await Press(
        widget, controlsBeforePicker, ControllerButton.A, "gallery.picker.open");
    Assert.True(handled);
    await WaitUntil(() => widget.Modal == GalleryModal.Picker);
    var picker = widget.RenderSnapshot("sdk-gallery.test", 2);
    Assert.Equal(GalleryModal.Picker, widget.Modal);
    Assert.True(controlsLifetime.IsCancellationRequested,
        "Opening a nested gallery route did not cancel parent route work.");
    Assert.Equal(widget.Navigation.InputScopeId, picker.ActiveInputScopeId);
    Assert.Equal("gallery.density.comfortable", picker.InitialFocusId);
    Assert.ContainsShortcut(Find(picker, "gallery.picker"), ControllerButton.B,
        widget.Navigation.BackActionId!);

    var pickerLifetime = widget.Navigation.RouteCancellationToken;
    handled = await widget.OnControllerInputAsync(new ControllerInputEvent(
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

    var controls = widget.RenderSnapshot("sdk-gallery.test", 3);
    const string sheetReturnFocus = "gallery.controls.compact.action";
    handled = await Press(widget, controls, ControllerButton.Y, sheetReturnFocus);
    Assert.True(handled);
    await WaitUntil(() => widget.Modal == GalleryModal.ActionSheet);
    var sheet = widget.RenderSnapshot("sdk-gallery.test", 4);
    Assert.Equal(widget.Navigation.InputScopeId, sheet.ActiveInputScopeId);
    Assert.ContainsShortcut(Find(sheet, "gallery.sheet"), ControllerButton.B,
        widget.Navigation.BackActionId!);
    Assert.Equal(ActionSheetItemToneClass("danger"),
        Find(sheet, "gallery.sheet.remove").StyleClasses.Single(value => value.EndsWith("danger", StringComparison.Ordinal)));

    handled = await Press(widget, sheet, ControllerButton.B, "gallery.sheet.pin");
    Assert.True(handled);
    await WaitUntil(() => widget.Modal == GalleryModal.None);
    Assert.Equal(GalleryModal.None, widget.Modal);
    var sheetReturn = widget.RenderSnapshot("sdk-gallery.test", 5);
    Assert.Equal(sheetReturnFocus, sheetReturn.InitialFocusId);
    Assert.Equal(0, ViewSnapshotValidator.Validate(sheetReturn).Count);

    var tilesSource = HeaderAction(
        sheetReturn, "gallery.shell.compact", "gallery.tab.tiles");
    handled = await Press(widget, sheetReturn, ControllerButton.A, tilesSource.Id);
    Assert.True(handled);
    await WaitUntil(() => widget.Page == GalleryPage.Tiles);
    var tiles = widget.RenderSnapshot("sdk-gallery.test", 6);
    Assert.True(tiles.InitialFocusId is null);
    Assert.True(tiles.FocusGroupEntryRequest is null);
    Assert.Equal(tilesSource.Id,
        HeaderAction(tiles, "gallery.shell.compact", "gallery.tab.tiles").Id);
    Assert.Equal(sheetReturn.ActiveInputScopeId, tiles.ActiveInputScopeId);
    Assert.Equal(0, ViewSnapshotValidator.Validate(tiles).Count);
    Assert.False(Nodes(tiles.Root).Any(node => node.Id == "gallery.sheet.open"),
        "A modal return-focus opener survived after its owning page left the tree.");
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
    Assert.Equal(ProtocolConstants.ControllerGlyphVersion, snapshot.ProtocolVersion);
    Assert.Equal(ViewNodeKind.BackgroundSurface, snapshot.Root.Kind);
    Assert.True(snapshot.Root.StyleClasses.SequenceEqual(
        ["wrail-background-surface", "gallery-root-background"]));
    Assert.Equal("gallery.root.content", snapshot.Root.Children.Single().Id);
    var nestedBackground = Find(snapshot, "gallery.tiles.surface");
    Assert.Equal(ViewNodeKind.BackgroundSurface, nestedBackground.Kind);
    Assert.True(nestedBackground.StyleClasses.SequenceEqual(
        ["wrail-background-surface", "gallery-nested-background"]));
    var artwork = await widget.OnResolveArtworkAsync(new WidgetArtworkHandle(artworkHandle!));
    Assert.True(artwork is not null);
    Assert.Equal(WidgetArtworkContentType.Png, artwork!.ContentType);
    Assert.True(artwork.Bytes.Span[..8].SequenceEqual(
        new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }));
    var webPHandle = Find(snapshot, "gallery.webp.artwork").ArtworkHandle;
    Assert.Equal("gallery.artwork.sample-webp-512-v1", webPHandle);
    Assert.False(string.Equals("gallery.artwork.sample-webp", webPHandle,
        StringComparison.Ordinal), "Changed WebP bytes reused the stale artwork handle.");
    var webP = await widget.OnResolveArtworkAsync(new WidgetArtworkHandle(webPHandle!));
    Assert.True(webP is not null);
    Assert.Equal(WidgetArtworkContentType.WebP, webP!.ContentType);
    Assert.True(webP.Bytes.Span[..12].SequenceEqual(
        new byte[] { 82, 73, 70, 70, 4, 147, 3, 0, 87, 69, 66, 80 }));
    Assert.Equal(234_252, webP.Bytes.Length);
    Assert.True(WebPDimensions(webP.Bytes.Span) == (512, 512),
        "The embedded WebP did not retain its decoded 512 by 512 canvas.");
    Assert.Equal(
        "F498C68D0ACD29152C084437C651D19F8E0CE376BE63C8042C62324EBFFEAB4E",
        Convert.ToHexString(SHA256.HashData(webP.Bytes.Span)));
}

static async Task BackgroundGallery()
{
    var widget = new SdkGalleryWidget();
    var initial = Snapshot(widget, 1);
    Assert.Equal(ViewNodeKind.BackgroundSurface, initial.Root.Kind);
    Assert.Equal(SdkGalleryWidget.DefaultBackgroundArtworkHandle, initial.Root.ArtworkHandle);
    Assert.Equal(ImageFit.Cover, initial.Root.ImageFit);
    Assert.Equal(true, initial.Root.UsesFocusedDescendantArtwork);
    Assert.True(initial.Root.StyleClasses.SequenceEqual(
        ["wrail-background-surface", "gallery-root-background"]));

    await widget.OnActionAsync(new WidgetActionEvent(
        "gallery.tab.backgrounds",
        Find(initial, "gallery.shell.compact").Children.Single(node =>
            node.ActionId == "gallery.tab.backgrounds").Id));
    var backgrounds = Snapshot(widget, 2);
    Assert.True(backgrounds.InitialFocusId is null);
    Assert.True(backgrounds.FocusGroupEntryRequest is null);
    Assert.Equal(0, ViewSnapshotValidator.Validate(backgrounds).Count);
    Assert.Equal(5, Nodes(backgrounds.Root).Count(
        node => node.Kind == ViewNodeKind.BackgroundSurface));
    Assert.Equal("gallery.backgrounds.warm",
        Find(backgrounds, "gallery.backgrounds.focus-row").InitialChildFocusId);
    var compactBackgrounds = Find(backgrounds, "gallery.shell.compact").Children
        .Single(node => node.ActionId == "gallery.tab.backgrounds");
    var railBackgrounds = Find(backgrounds, "gallery.shell.rail").Children
        .Single(node => node.ActionId == "gallery.tab.backgrounds");
    Assert.Equal("gallery.backgrounds.focus-row", compactBackgrounds.Focus?.Down);
    Assert.Equal("gallery.backgrounds.focus-row", railBackgrounds.Focus?.Right);
    Assert.Equal(SdkGalleryWidget.WarmBackgroundArtworkHandle,
        Find(backgrounds, "gallery.backgrounds.warm").FocusBackgroundArtworkHandle);
    Assert.Equal<string?>(null,
        Find(backgrounds, "gallery.backgrounds.retain").FocusBackgroundArtworkHandle);
    Assert.Equal(SdkGalleryWidget.CoolBackgroundArtworkHandle,
        Find(backgrounds, "gallery.backgrounds.cool").FocusBackgroundArtworkHandle);

    var cover = Find(backgrounds, "gallery.backgrounds.cover.surface");
    Assert.Equal(ImageFit.Cover, cover.ImageFit);
    Assert.Equal(SdkGalleryWidget.DefaultBackgroundArtworkHandle, cover.ArtworkHandle);
    Assert.True(cover.StyleClasses.Contains(
        "gallery-background-fit-surface", StringComparer.Ordinal));
    Assert.Equal(
        "Cover fills every edge and crops overflow when aspect ratios differ.",
        Find(backgrounds, "gallery.backgrounds.cover.description").Text);

    var contain = Find(backgrounds, "gallery.backgrounds.contain.surface");
    Assert.Equal(ImageFit.Contain, contain.ImageFit);
    Assert.Equal(SdkGalleryWidget.DefaultBackgroundArtworkHandle, contain.ArtworkHandle);
    Assert.Equal(
        "Contain preserves the whole image and may leave unused space.",
        Find(backgrounds, "gallery.backgrounds.contain.description").Text);
    Assert.Equal(true, contain.UsesFocusedDescendantArtwork);
    Assert.Equal(SdkGalleryWidget.WarmBackgroundArtworkHandle,
        Find(backgrounds, "gallery.backgrounds.contain.focus").FocusBackgroundArtworkHandle);
    Assert.True(contain.StyleClasses.SequenceEqual(
        ["wrail-background-surface", "gallery-background-demo-surface",
         "gallery-background-fit-surface",
         "gallery-background-demo-surface--contain"]));
    var fill = Find(backgrounds, "gallery.backgrounds.fill.surface");
    Assert.Equal(ImageFit.Fill, fill.ImageFit);
    Assert.Equal(SdkGalleryWidget.DefaultBackgroundArtworkHandle, fill.ArtworkHandle);
    Assert.True(fill.StyleClasses.Contains(
        "gallery-background-fit-surface", StringComparer.Ordinal));
    Assert.Equal(
        "Fill stretches the sealed default artwork to the bounded sample region.",
        Find(backgrounds, "gallery.backgrounds.fill.description").Text);
    Assert.Equal(ImageFit.Cover, Find(backgrounds, "gallery.backgrounds.missing.surface").ImageFit);
    Assert.Equal(SdkGalleryWidget.MissingBackgroundArtworkHandle,
        Find(backgrounds, "gallery.backgrounds.missing.surface").ArtworkHandle);
    Assert.Equal<WidgetEncodedArtwork?>(null, await widget.OnResolveArtworkAsync(
        new WidgetArtworkHandle(SdkGalleryWidget.MissingBackgroundArtworkHandle)));

    var hashes = new HashSet<string>(StringComparer.Ordinal);
    foreach (var (handle, length, hash) in new[]
    {
        (SdkGalleryWidget.DefaultBackgroundArtworkHandle, 2_241_830,
            "DBEE1BA7FFF3ABC765F684D3AC666E34DA5CC68575C19DBAF9B64A4CA8405297"),
        (SdkGalleryWidget.WarmBackgroundArtworkHandle, 2_295_973,
            "502D596BCBB590EAF24C7FC34ED81DE81B616E4F562F36118112E971BB38D247"),
        (SdkGalleryWidget.CoolBackgroundArtworkHandle, 2_417_019,
            "B317048E3A6A455350A1C3A19FDDFF13371CE8C6F112CDEA1B80BC2879341711"),
    })
    {
        var resolved = await widget.OnResolveArtworkAsync(new WidgetArtworkHandle(handle));
        Assert.True(resolved is not null);
        Assert.Equal(WidgetArtworkContentType.Png, resolved!.ContentType);
        Assert.Equal(length, resolved.Bytes.Length);
        Assert.True(length <= ProtocolConstants.MaximumEncodedArtworkBytes);
        var actualHash = Convert.ToHexString(SHA256.HashData(resolved.Bytes.Span));
        Assert.Equal(hash, actualHash);
        hashes.Add(actualHash);
    }
    Assert.Equal(3, hashes.Count);

    await widget.OnActionAsync(new WidgetActionEvent(
        "gallery.tab.overview",
        Find(backgrounds, "gallery.shell.compact").Children.Single(node =>
            node.ActionId == "gallery.tab.overview").Id));
    var departed = Snapshot(widget, 3);
    Assert.Equal(1, Nodes(departed.Root).Count(
        node => node.Kind == ViewNodeKind.BackgroundSurface));
    Assert.False(Nodes(departed.Root).Any(node =>
        node.Id.StartsWith("gallery.backgrounds.", StringComparison.Ordinal)),
        "A nested BackgroundSurface survived after its exact page left the tree.");
}

static async Task TopLevelPagesShareRootScope()
{
    foreach (var presentation in new[] { "gallery.shell.compact", "gallery.shell.rail" })
    {
        var widget = new SdkGalleryWidget();
        await WidgetTestHost.SetLifecycleStateAsync(
            widget, WidgetLifecycleState.Interactive);
        var overview = widget.RenderSnapshot("sdk-gallery.navigation", 1);
        var rootScope = overview.ActiveInputScopeId;
        var overviewRouteLifetime = widget.Navigation.RouteCancellationToken;
        var overviewRootLifetime = widget.Navigation.RootRouteCancellationToken;
        var controlsSource = HeaderAction(overview, presentation, "gallery.tab.controls");
        var handled = await Press(widget, overview, ControllerButton.A, controlsSource.Id);
        Assert.True(handled);
        await WaitUntil(() => widget.Page == GalleryPage.Controls);

        var controls = widget.RenderSnapshot("sdk-gallery.navigation", 2);
        Assert.Equal(rootScope, controls.ActiveInputScopeId);
        Assert.True(overviewRouteLifetime.IsCancellationRequested,
            "Changing roots did not cancel departed route work.");
        Assert.True(overviewRootLifetime.IsCancellationRequested,
            "Changing roots did not cancel departed root-page work.");
        Assert.True(controls.FocusGroupEntryRequest is null,
            "A on a persistent header requested remembered-group entry.");
        Assert.Equal(controlsSource.Id,
            HeaderAction(controls, presentation, "gallery.tab.controls").Id);
        var tilesSource = HeaderAction(controls, presentation, "gallery.tab.tiles");
        handled = await Press(widget, controls, ControllerButton.A, tilesSource.Id);
        Assert.True(handled);
        await WaitUntil(() => widget.Page == GalleryPage.Tiles);

        var tiles = widget.RenderSnapshot("sdk-gallery.navigation", 3);
        Assert.Equal(rootScope, tiles.ActiveInputScopeId);
        Assert.True(tiles.FocusGroupEntryRequest is null);
        Assert.Equal(tilesSource.Id,
            HeaderAction(tiles, presentation, "gallery.tab.tiles").Id);
        var controlsReturn = HeaderAction(tiles, presentation, "gallery.tab.controls");
        handled = await Press(widget, tiles, ControllerButton.A, controlsReturn.Id);
        Assert.True(handled);
        await WaitUntil(() => widget.Page == GalleryPage.Controls);

        var returned = widget.RenderSnapshot("sdk-gallery.navigation", 4);
        Assert.Equal(rootScope, returned.ActiveInputScopeId);
        Assert.True(returned.FocusGroupEntryRequest is null);
        Assert.Equal(controlsReturn.Id,
            HeaderAction(returned, presentation, "gallery.tab.controls").Id);
        Assert.Equal(controlsReturn.FocusPersistenceId,
            Find(returned, controlsReturn.Id).FocusPersistenceId);
        Assert.Equal("gallery.tab.controls",
            HeaderAction(returned, presentation, "gallery.tab.controls").ActionId);

        var repeatedSource = HeaderAction(
            returned, presentation, "gallery.tab.controls");
        var repeatedRouteLifetime = widget.Navigation.RouteCancellationToken;
        var repeatedRootLifetime = widget.Navigation.RootRouteCancellationToken;
        handled = await Press(widget, returned, ControllerButton.A, repeatedSource.Id);
        Assert.True(handled);
        var repeated = widget.RenderSnapshot("sdk-gallery.navigation", 5);
        Assert.Equal(rootScope, repeated.ActiveInputScopeId);
        Assert.False(repeatedRouteLifetime.IsCancellationRequested,
            "Selecting the current root canceled valid route work.");
        Assert.False(repeatedRootLifetime.IsCancellationRequested,
            "Selecting the current root canceled valid root-page work.");
        Assert.True(repeated.InitialFocusId is null);
        Assert.True(repeated.FocusGroupEntryRequest is null);
        Assert.Equal(repeatedSource.Id,
            HeaderAction(repeated, presentation, "gallery.tab.controls").Id);
        Assert.Equal(repeatedSource.FocusPersistenceId,
            HeaderAction(repeated, presentation, "gallery.tab.controls").FocusPersistenceId);
        Assert.Equal(0, ViewSnapshotValidator.Validate(repeated).Count);
        await WidgetTestHost.DestroyAsync(widget);
    }

    var bumperWidget = new SdkGalleryWidget();
    await WidgetTestHost.SetLifecycleStateAsync(
        bumperWidget, WidgetLifecycleState.Interactive);
    var overviewSnapshot = bumperWidget.RenderSnapshot("sdk-gallery.bumpers", 1);
    var overviewLifetime = bumperWidget.Navigation.RouteCancellationToken;
    var handledBumper = await Press(
        bumperWidget, overviewSnapshot, ControllerButton.LeftBumper, "gallery.refresh");
    Assert.True(handledBumper);
    await WaitUntil(() => bumperWidget.Page == GalleryPage.Utilities);
    var utilities = bumperWidget.RenderSnapshot("sdk-gallery.bumpers", 2);
    Assert.True(overviewLifetime.IsCancellationRequested);
    Assert.Equal(overviewSnapshot.ActiveInputScopeId, utilities.ActiveInputScopeId);
    Assert.True(utilities.InitialFocusId is null);
    Assert.Equal(1L, utilities.FocusGroupEntryRequest!.RequestId);
    Assert.Equal("gallery.utilities", utilities.FocusGroupEntryRequest.GroupId);

    var utilitiesLifetime = bumperWidget.Navigation.RouteCancellationToken;
    handledBumper = await Press(
        bumperWidget,
        utilities,
        ControllerButton.RightBumper,
        "gallery.utilities.toast-button");
    Assert.True(handledBumper);
    await WaitUntil(() => bumperWidget.Page == GalleryPage.Overview);
    var wrapped = bumperWidget.RenderSnapshot("sdk-gallery.bumpers", 3);
    Assert.True(utilitiesLifetime.IsCancellationRequested);
    Assert.Equal(2L, wrapped.FocusGroupEntryRequest!.RequestId);
    Assert.Equal("gallery.overview", wrapped.FocusGroupEntryRequest.GroupId);

    var rejected = await bumperWidget.OnControllerInputAsync(new ControllerInputEvent(
        ControllerButton.RightBumper,
        ControllerEventPhase.Pressed,
        ControllerInputContext.OpenWidget,
        FocusedElementId: "gallery.refresh",
        ActiveInputScopeId: "foreign.scope",
        SnapshotSequence: wrapped.Sequence));
    Assert.False(rejected);
    Assert.Equal(GalleryPage.Overview, bumperWidget.Page);
    await WidgetTestHost.DestroyAsync(bumperWidget);
}

static Task PackageContract()
{
    var manifest = ManifestJson.Deserialize(File.ReadAllBytes(
        Path.Combine(AppContext.BaseDirectory, "manifest.json")));
    Assert.Equal(0, WidgetManifestValidator.Validate(manifest).Count);
    Assert.Equal("widgetrail.samples.sdk-gallery", manifest.Id);
    Assert.Equal("widgetrail.samples", manifest.Publisher);
    Assert.Equal("0.1.19", manifest.Version);
    Assert.Equal("dotnet-worker", manifest.Entrypoint.Runtime);
    Assert.Equal("payload/SdkGalleryWidget.dll", manifest.Entrypoint.Assembly);
    Assert.Equal(typeof(SdkGalleryWidget).FullName, manifest.Entrypoint.Type);
    Assert.Equal(0, manifest.Permissions.Count);
    Assert.Equal(0, manifest.OptionalPermissions.Count);
    Assert.Equal("suspend-when-hidden", manifest.ResidencyPolicy!.Mode);
    Assert.Equal(WidgetGlyph.Settings, manifest.Presentation.Icon);
    Assert.Equal("gallery.mark", manifest.Presentation.PackageIcon?.AssetId);
    Assert.Equal(WidgetPackageIconColorMode.OriginalColor,
        manifest.Presentation.PackageIcon?.ColorMode);
    Assert.Equal("assets/icons/gallery-mark.svg",
        manifest.IconAssets["gallery.mark"].Path);

    var project = File.ReadAllText(Path.Combine(
        AppContext.BaseDirectory, "sample", "SdkGalleryWidget.csproj"));
    Assert.True(project.Contains("src\\WidgetSdk\\WidgetSdk.csproj", StringComparison.Ordinal));
    Assert.False(project.Contains("OverlayHost", StringComparison.OrdinalIgnoreCase));
    Assert.False(project.Contains("PlatformBroker", StringComparison.OrdinalIgnoreCase));
    Assert.False(project.Contains("WidgetRuntime", StringComparison.OrdinalIgnoreCase));
    var expectedResources = new[]
    {
        "WidgetRail.Samples.SdkGalleryWidget.Assets.background-default.png",
        "WidgetRail.Samples.SdkGalleryWidget.Assets.background-focus-cool.png",
        "WidgetRail.Samples.SdkGalleryWidget.Assets.background-focus-warm.png",
        "WidgetRail.Samples.SdkGalleryWidget.Assets.sample-webp.webp",
    };
    Assert.True(project.Contains("<EmbeddedResource Include=\"assets\\background-default.png\"",
        StringComparison.Ordinal));
    Assert.True(project.Contains("<EmbeddedResource Include=\"assets\\sample-webp.webp\"",
        StringComparison.Ordinal));
    foreach (var resource in expectedResources)
        Assert.True(project.Contains($"LogicalName=\"{resource}\"", StringComparison.Ordinal));
    Assert.True(typeof(SdkGalleryWidget).Assembly.GetManifestResourceNames()
        .Order(StringComparer.Ordinal).SequenceEqual(expectedResources));

    var packageScript = File.ReadAllText(Path.Combine(
        AppContext.BaseDirectory, "sample", "Build-CommunityPackage.ps1"));
    Assert.True(packageScript.Contains("$expectedFiles", StringComparison.Ordinal));
    Assert.True(packageScript.Contains("payload\\SdkGalleryWidget.dll", StringComparison.Ordinal));
    Assert.True(packageScript.Contains("assets\\icons\\gallery-mark.svg",
        StringComparison.Ordinal));
    Assert.False(packageScript.Contains("assets\\background-", StringComparison.Ordinal),
        "Embedded artwork must not be duplicated as loose package files.");
    Assert.True(packageScript.Contains("Assert-NoReparsePoint", StringComparison.Ordinal));
    Assert.True(packageScript.Contains("validate $stagingRoot", StringComparison.Ordinal));
    Assert.True(packageScript.Contains("pack $stagingRoot --output $packagePath", StringComparison.Ordinal));
    Assert.False(packageScript.Contains("Copy-Item -Path", StringComparison.OrdinalIgnoreCase),
        "Package staging must not copy wildcard paths.");
    var iconPath = Path.Combine(
        AppContext.BaseDirectory, "sample", "assets", "icons", "gallery-mark.svg");
    Assert.Equal(
        "3680A34A41F772B56ACD9C57D2A6A34169F8E432B79DD4F432DC8AE54E2ECA1F",
        Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(iconPath))));
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
    var bumperKey = compiled.Theme.Resolve(new WrssElement(
        "row", StyleClasses: new HashSet<string>(
            ["wrail-controller-glyph", "gallery-section-bumper-key"])))!;
    Assert.Equal("24px", bumperKey.Get("font-size")?.Text);
    Assert.Equal("#f7f7fa", bumperKey.Get("color")?.Text);
    Assert.Equal("0", bumperKey.Get("flex-shrink")?.Text);
    var grid = compiled.Theme.Resolve(new WrssElement(
        "grid", StyleClasses: new HashSet<string>(["wrail-responsive-grid"])))!;
    Assert.Equal("100%", grid.Get("width")?.Text);
    var compactDensity = compiled.Theme.Resolve(new WrssElement(
        "stack", StyleClasses: new HashSet<string>(
            ["gallery-density-preview", "gallery-density-preview--compact"])))!;
    var comfortableDensity = compiled.Theme.Resolve(new WrssElement(
        "stack", StyleClasses: new HashSet<string>(
            ["gallery-density-preview", "gallery-density-preview--comfortable"])))!;
    var spaciousDensity = compiled.Theme.Resolve(new WrssElement(
        "stack", StyleClasses: new HashSet<string>(
            ["gallery-density-preview", "gallery-density-preview--spacious"])))!;
    Assert.Equal("6px", compactDensity.Get("padding")?.Text);
    Assert.Equal("14px", comfortableDensity.Get("padding")?.Text);
    Assert.Equal("24px", spaciousDensity.Get("padding")?.Text);
    Assert.False(string.Equals(
        compactDensity.Get("gap")?.Text, spaciousDensity.Get("gap")?.Text,
        StringComparison.Ordinal));
    var compactDensityRow = compiled.Theme.Resolve(new WrssElement(
        "row", StyleClasses: new HashSet<string>(
            ["gallery-density-preview-row", "gallery-density-preview-row--compact"])))!;
    var spaciousDensityRow = compiled.Theme.Resolve(new WrssElement(
        "row", StyleClasses: new HashSet<string>(
            ["gallery-density-preview-row", "gallery-density-preview-row--spacious"])))!;
    Assert.Equal("4px", compactDensityRow.Get("gap")?.Text);
    Assert.Equal("20px", spaciousDensityRow.Get("gap")?.Text);
    var fitSurface = compiled.Theme.Resolve(new WrssElement(
        "background-surface", StyleClasses: new HashSet<string>(
            ["gallery-background-demo-surface", "gallery-background-fit-surface"])))!;
    Assert.Equal("150px", fitSurface.Get("height")?.Text);
    Assert.Equal("150px", fitSurface.Get("min-height")?.Text);
    Assert.Equal("150px", fitSurface.Get("max-height")?.Text);
    Assert.Equal("2px", fitSurface.Get("border-width")?.Text);
    Assert.False(source.Contains("wrail-navigation-shell", StringComparison.Ordinal),
        "The sample must inherit the platform navigation recipe instead of rebuilding it.");
    Assert.Equal("rgba(8, 12, 18, 0.68)", root.Get("background")?.Text);
    var background = compiled.Theme.Resolve(new WrssElement(
        "background-surface", StyleClasses: new HashSet<string>(
            ["wrail-background-surface", "gallery-root-background"])))!;
    Assert.Equal("rgba(8, 12, 18, 0.18)", background.Get("image-tint")?.Text);
    Assert.Equal("rgba(8, 12, 18, 0.30)", background.Get("scrim-color")?.Text);
    Assert.True(source.Contains("var(--text)", StringComparison.Ordinal));
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

static ValueTask<bool> Press(
    SdkGalleryWidget widget,
    ViewSnapshot snapshot,
    ControllerButton button,
    string focusedElementId) => widget.OnControllerInputAsync(new ControllerInputEvent(
        button,
        ControllerEventPhase.Pressed,
        ControllerInputContext.OpenWidget,
        FocusedElementId: focusedElementId,
        ActiveInputScopeId: snapshot.ActiveInputScopeId,
        SnapshotSequence: snapshot.Sequence));

static ViewNode HeaderAction(ViewSnapshot snapshot, string presentation, string actionId) =>
    Find(snapshot, presentation).Children.Single(node => node.ActionId == actionId);

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

static (int Width, int Height) WebPDimensions(ReadOnlySpan<byte> bytes)
{
    var offset = 12;
    while (bytes.Length - offset >= 8)
    {
        var chunkLength = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(
            bytes.Slice(offset + 4, 4)));
        var data = offset + 8;
        if (chunkLength > bytes.Length - data)
            throw new InvalidOperationException("The WebP chunk is truncated.");
        var chunk = bytes.Slice(offset, 4);
        if (chunk.SequenceEqual("VP8L"u8))
        {
            if (chunkLength < 5 || bytes[data] != 0x2f)
                throw new InvalidOperationException("The WebP lossless header is invalid.");
            var packed = BinaryPrimitives.ReadUInt32LittleEndian(bytes.Slice(data + 1, 4));
            return ((int)(packed & 0x3fff) + 1, (int)((packed >> 14) & 0x3fff) + 1);
        }
        var padded = chunkLength + (chunkLength & 1);
        offset = checked(data + padded);
    }
    throw new InvalidOperationException("The WebP has no supported image payload.");
}

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
