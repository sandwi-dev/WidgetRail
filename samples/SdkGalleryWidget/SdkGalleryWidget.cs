using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

namespace WidgetRail.Samples.SdkGalleryWidget;

public enum GalleryPage { Overview, Controls, Tiles, Backgrounds, Utilities }
public enum GalleryModal { None, Picker, ActionSheet }
public readonly record struct GalleryRoute(GalleryPage Page, GalleryModal Modal);

/// <summary>
/// Copyable, capability-free reference for the public controller-first SDK.
/// It intentionally has no custom worker, broker calls, or host-only helpers.
/// </summary>
public sealed class SdkGalleryWidget : Widget
{
    public const string DefaultBackgroundArtworkHandle = "gallery.artwork.background.default";
    public const string WarmBackgroundArtworkHandle = "gallery.artwork.background.warm";
    public const string CoolBackgroundArtworkHandle = "gallery.artwork.background.cool";
    public const string MissingBackgroundArtworkHandle = "gallery.artwork.background.missing";
    private const string SampleArtworkHandle = DefaultBackgroundArtworkHandle;
    private const string SampleWebPArtworkHandle = "gallery.artwork.sample-webp";
    private static readonly byte[] SampleWebPArtworkBytes = Convert.FromBase64String(
        "UklGRh4AAABXRUJQVlA4TBEAAAAvAQAAAAdQmWZ0qf+BiOh/AAA=");
    private static readonly IReadOnlyDictionary<string, string> PackagedPngFiles =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [DefaultBackgroundArtworkHandle] = "background-default.png",
            [WarmBackgroundArtworkHandle] = "background-focus-warm.png",
            [CoolBackgroundArtworkHandle] = "background-focus-cool.png",
        };
    private static readonly WidgetIdScope Ids = WidgetIds.Scope("gallery");
    private static readonly NavigationShellDestination[] Destinations =
    [
        new("gallery.tab.overview", "Overview", "gallery.tab.overview", WidgetGlyph.Play),
        new("gallery.tab.controls", "Controls", "gallery.tab.controls", WidgetGlyph.Settings),
        new("gallery.tab.tiles", "Tiles", "gallery.tab.tiles", WidgetGlyph.Connection),
        new("gallery.tab.backgrounds", "Backgrounds", "gallery.tab.backgrounds", WidgetGlyph.Music),
        new("gallery.tab.utilities", "Utilities", "gallery.tab.utilities", WidgetGlyph.Warning),
    ];
    private readonly WidgetNavigator<GalleryRoute> _navigation;
    private bool _compactMode = true;
    private string _density = "Comfortable";
    private TimeSpan _position = TimeSpan.FromSeconds(74);
    private bool _showToast;
    private int _toastGeneration;

    public SdkGalleryWidget() => _navigation = CreateNavigator(
        Ids.Id("navigation"),
        new GalleryRoute(GalleryPage.Overview, GalleryModal.None));

    public GalleryPage Page => _navigation.Value.Route.Page;
    public GalleryModal Modal => _navigation.Value.Route.Modal;
    public WidgetNavigationSnapshot<GalleryRoute> Navigation => _navigation.Value;
    public bool CompactMode => _compactMode;
    public string Density => _density;
    public TimeSpan Position => _position;
    public bool IsToastVisible => _showToast;

    public override WidgetView Render()
    {
        var navigation = _navigation.Value;
        WidgetElement body = navigation.Route.Modal == GalleryModal.None
            ? UI.NavigationShell(
                "gallery.shell",
                TabId(navigation.Route.Page),
                ContentEntryFocus(navigation),
                PageContent(navigation.Route.Page),
                Destinations)
            : ModalContent(navigation);
        var rootContent = UI.Stack("gallery.root.content",
            Header(),
            body)
            .Classes("gallery-root");

        if (navigation.Route.Modal == GalleryModal.None)
            rootContent = _navigation.Scope(navigation, rootContent);

        if (_showToast)
        {
            rootContent = rootContent with
            {
                Children =
                [
                    .. rootContent.Children,
                    UI.Toast(
                        "Action received",
                        "The widget updated local state without taking controller focus.",
                        ToastTone.Success,
                        "gallery.toast",
                        TimeSpan.FromSeconds(3)),
                ],
            };
        }

        var root = UI.BackgroundSurface(
                rootContent,
                "gallery.root",
                BackgroundSurfaceArtwork.FromHandle(
                    new WidgetArtworkHandle(DefaultBackgroundArtworkHandle), ImageFit.Cover))
            .UseFocusedDescendantArtwork()
            .AddClasses("gallery-root-background");

        return new WidgetView(
            root,
            InitialFocusId: InitialFocus(navigation),
            QuickActions:
            [
                new WidgetQuickAction(
                    ControllerButton.X,
                    "gallery.toast.show",
                    "Show feedback"),
            ],
            ActiveInputScopeId: navigation.InputScopeId,
            Surface: new WidgetSurfaceHints
            {
                Mode = WidgetSurfaceMode.Standard,
                PreferredWidth = 760,
                PreferredHeight = 540,
                MinimumWidth = 320,
                MinimumHeight = 280,
            });
    }

    public override ValueTask OnActionAsync(
        WidgetActionEvent action,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(action);
        cancellationToken.ThrowIfCancellationRequested();

        if (_navigation.TryHandleBack(action))
            return ValueTask.CompletedTask;

        switch (action.ActionId)
        {
            case "gallery.tab.overview":
                SetPage(GalleryPage.Overview, action.SourceElementId);
                return ValueTask.CompletedTask;
            case "gallery.tab.controls":
                SetPage(GalleryPage.Controls, action.SourceElementId);
                return ValueTask.CompletedTask;
            case "gallery.tab.tiles":
                SetPage(GalleryPage.Tiles, action.SourceElementId);
                return ValueTask.CompletedTask;
            case "gallery.tab.backgrounds":
                SetPage(GalleryPage.Backgrounds, action.SourceElementId);
                return ValueTask.CompletedTask;
            case "gallery.tab.utilities":
                SetPage(GalleryPage.Utilities, action.SourceElementId);
                return ValueTask.CompletedTask;
            case "gallery.compact.toggle": _compactMode = !_compactMode; break;
            case "gallery.picker.open":
                OpenModal(GalleryModal.Picker, action.SourceElementId);
                return ValueTask.CompletedTask;
            case "gallery.sheet.open":
                OpenModal(GalleryModal.ActionSheet, action.SourceElementId);
                return ValueTask.CompletedTask;
            case "gallery.density.compact":
                SelectDensity("Compact", action.SourceElementId);
                return ValueTask.CompletedTask;
            case "gallery.density.comfortable":
                SelectDensity("Comfortable", action.SourceElementId);
                return ValueTask.CompletedTask;
            case "gallery.density.spacious":
                SelectDensity("Spacious", action.SourceElementId);
                return ValueTask.CompletedTask;
            case "gallery.scrub":
                if (action.RequestedValue is { } requested && double.IsFinite(requested))
                    _position = TimeSpan.FromMilliseconds(Math.Clamp(requested, 0, 225_000));
                break;
            case "gallery.toast.show":
            case "gallery.alert.acknowledge":
            case "gallery.empty.populate":
            case "gallery.media.open":
            case "gallery.app.open":
            case "gallery.media.queue":
            case "gallery.media.remove":
            case "gallery.refresh":
            case "gallery.sheet.pin":
            case "gallery.sheet.share":
                ShowToast();
                return ValueTask.CompletedTask;
            case "gallery.sheet.remove":
                _navigation.Back(action.SourceElementId);
                ShowToast();
                return ValueTask.CompletedTask;
            default:
                return ValueTask.CompletedTask;
        }

        Invalidate();
        return ValueTask.CompletedTask;
    }

    public override ValueTask<WidgetEncodedArtwork?> OnResolveArtworkAsync(
        WidgetArtworkHandle handle,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (PackagedPngFiles.TryGetValue(handle.Value, out var fileName))
            return ValueTask.FromResult(ResolvePackagedPng(fileName));
        return ValueTask.FromResult<WidgetEncodedArtwork?>(
            handle.Value == SampleWebPArtworkHandle
                ? new WidgetEncodedArtwork(WidgetArtworkContentType.WebP, SampleWebPArtworkBytes)
                : null);
    }

    protected override ValueTask OnDeactivatedAsync(CancellationToken transitionToken)
    {
        if (_showToast)
        {
            _showToast = false;
            Interlocked.Increment(ref _toastGeneration);
            Invalidate();
        }
        return ValueTask.CompletedTask;
    }

    private StackElement Header() => UI.SectionHeader(
        "SDK Gallery",
        "gallery.header",
        eyebrow: "COMMUNITY REFERENCE",
        description: "Public components, real focus behavior, zero privileged APIs.",
        trailing: UI.StatusBadge("Public SDK", StatusTone.Success, "gallery.header.status"))
        .AddClasses("gallery-header");

    private ScrollElement PageContent(GalleryPage page) => UI.VerticalScroll(
        "gallery.page-scroll",
        page switch
        {
            GalleryPage.Overview => OverviewPage(),
            GalleryPage.Controls => ControlsPage(),
            GalleryPage.Tiles => TilesPage(),
            GalleryPage.Backgrounds => BackgroundsPage(),
            _ => UtilitiesPage(),
        }).Classes("gallery-page-scroll");

    private StackElement OverviewPage() => UI.Stack("gallery.overview",
        UI.SectionHeader(
            "Foundations",
            "gallery.overview.header",
            description: "Cards organize content; semantic tones add meaning without relying on color."),
        UI.ResponsiveGrid("gallery.overview.grid", 210, 2,
            UI.Card("gallery.overview.status-card", CardVariant.Subtle,
                UI.Text("Service state", "gallery.overview.status-title"),
                UI.StatusBadge("Ready", StatusTone.Success, "gallery.overview.ready"),
                UI.StatusBadge("Syncing", StatusTone.Info, "gallery.overview.syncing")),
            UI.Card("gallery.overview.actions-card", CardVariant.Subtle,
                UI.Text("Icon actions", "gallery.overview.actions-title"),
                UI.Row("gallery.overview.actions",
                    UI.IconButton(WidgetGlyph.Refresh, "gallery.refresh", "gallery.refresh",
                        "Refresh preview", IconButtonVariant.Quiet, IconButtonSize.Small),
                    UI.IconButton(WidgetGlyph.Play, "gallery.toast.show", "gallery.play",
                        "Play preview", IconButtonVariant.Primary, IconButtonSize.Medium),
                    UI.IconButton(WidgetGlyph.Warning, "gallery.alert.acknowledge", "gallery.warning",
                        "Acknowledge warning", IconButtonVariant.Danger, IconButtonSize.Small))
                    .Classes("gallery-overview-actions"))),
        UI.Alert(
            "Responsive by default",
            "Resize the overlay or increase text scaling; content reflows and the host owns scrolling.",
            AlertTone.Info,
            "gallery.overview.alert",
            new ComponentAction("Acknowledge", "gallery.alert.acknowledge", WidgetGlyph.Check)),
        UI.EmptyState(
            "Nothing selected",
            "Empty states keep one clear recovery action and no decorative focus stops.",
            "gallery.overview.empty",
            new ComponentAction("Populate example", "gallery.empty.populate", WidgetGlyph.Play)))
        .AddClasses("gallery-page");

    private StackElement ControlsPage() => UI.Stack("gallery.controls",
        UI.SectionHeader(
            "Controller-native controls",
            "gallery.controls.header",
            description: "Every composite publishes stable focus IDs and semantic interaction state."),
        UI.SettingsRow(
            "Compact presentation",
            new ComponentAction("Toggle", "gallery.compact.toggle", WidgetGlyph.Settings),
            "gallery.controls.compact",
            description: "One focus stop; supporting text remains presentational.",
            value: _compactMode ? "On" : "Off",
            status: "Local state",
            statusTone: StatusTone.Info,
            glyph: WidgetGlyph.Settings),
        UI.Switch("Compact controls", _compactMode, "gallery.compact.toggle", "gallery.controls.switch"),
        UI.Select(
            "Density",
            [
                new SelectOption(
                    "gallery.density.compact.option", "Compact",
                    "gallery.density.compact", _density == "Compact",
                    WidgetGlyph.Settings),
                new SelectOption(
                    "gallery.density.comfortable.option", "Comfortable",
                    "gallery.density.comfortable", _density == "Comfortable",
                    WidgetGlyph.Connection),
                new SelectOption(
                    "gallery.density.spacious.option", "Spacious",
                    "gallery.density.spacious", _density == "Spacious",
                    WidgetGlyph.Warning),
            ],
            "gallery.controls.density",
            "Presentation density"),
        UI.Row("gallery.controls.openers",
            UI.Button("Choose density", "gallery.picker.open", "gallery.picker.open")
                .Icon(WidgetGlyph.Settings).Classes("gallery-wide-action"),
            UI.Button("More actions", "gallery.sheet.open", "gallery.sheet.open")
                .Icon(WidgetGlyph.Connection).Classes("gallery-wide-action"))
            .Classes("gallery-controls-openers"),
        UI.Card("gallery.controls.scrubber-card", CardVariant.Subtle,
            UI.Text("Media scrubber", "gallery.controls.scrubber-title"),
            UI.Scrubber(
                _position,
                TimeSpan.FromSeconds(225),
                TimeSpan.FromSeconds(5),
                "gallery.scrub",
                "gallery.scrubber",
                "Preview position"))
            .AddClasses("gallery-controls-scrubber-card"))
        .AddClasses("gallery-page");

    private StackElement TilesPage() => UI.Stack("gallery.tiles",
        UI.SectionHeader(
            "Rich action surfaces",
            "gallery.tiles.header",
            description: "Each complete tile is one focus target; its descendants stay presentational."),
        UI.BackgroundSurface(
            UI.FocusPresentationSurface(
                UI.ResponsiveGrid("gallery.tiles.grid", 250, 2,
                UI.Tile(
                    "Night Drive",
                    "Playing",
                    "gallery.media.open",
                    "gallery.media",
                    subtitle: "Example Artist",
                    metadata: "Controller UI Sessions",
                    artwork: TileArtwork.FromGlyph(WidgetGlyph.Music, "Music artwork"))
                    .ContextAction("gallery.media.queue", "Add to queue")
                    .ContextAction(
                        "gallery.media.remove", "Remove from library",
                        WidgetContextActionStyle.Danger)
                    .PresentOnFocus(UI.Stack("gallery.media.presentation",
                        UI.Text("Night Drive", "gallery.media.presentation.title"),
                        UI.Text("Playing from Controller UI Sessions",
                            "gallery.media.presentation.detail"))),
                UI.Tile(
                    "Sample Game",
                    "Ready",
                    "gallery.app.open",
                    "gallery.app",
                    subtitle: "Recently used",
                    metadata: "Community library",
                    artwork: TileArtwork.FromHandle(
                        new WidgetArtworkHandle(SampleArtworkHandle),
                        "Provider-neutral encoded PNG artwork"))
                    .PresentOnFocus(UI.Stack("gallery.app.presentation",
                        UI.Text("Sample Game", "gallery.app.presentation.title"),
                        UI.Text("Ready from the community library",
                            "gallery.app.presentation.detail"))),
                UI.Tile(
                    "WebP First Frame",
                    "Ready",
                    "gallery.app.open",
                    "gallery.webp",
                    subtitle: "Provider-neutral",
                    metadata: "Bounded encoded artwork",
                    artwork: TileArtwork.FromHandle(
                        new WidgetArtworkHandle(SampleWebPArtworkHandle),
                        "Provider-neutral encoded WebP artwork")),
                UI.PosterTile(
                    "A deliberately longer poster title that demonstrates bounded two-line copy",
                    "Available",
                    "gallery.app.open",
                    "gallery.poster",
                    subtitle: "SDK Gallery",
                    metadata: "Provider-neutral poster",
                    artwork: TileArtwork.FromHandle(
                        new WidgetArtworkHandle(SampleArtworkHandle),
                        "Provider-neutral poster artwork"))),
                UI.Stack("gallery.tiles.presentation.default",
                    UI.Text("Choose a tile", "gallery.tiles.presentation.default.title"),
                    UI.Text("Focused details appear here without another widget render.",
                        "gallery.tiles.presentation.default.detail")),
                "gallery.tiles.presentation"),
            "gallery.tiles.surface")
            .Classes("gallery-nested-background"))
        .AddClasses("gallery-page");

    private StackElement UtilitiesPage() => UI.Stack("gallery.utilities",
        UI.SectionHeader(
            "Feedback and diagnostics",
            "gallery.utilities.header",
            description: "Use semantic components instead of custom drawing or focus hacks."),
        UI.Card("gallery.utilities.code-card", CardVariant.Subtle,
            UI.Text("Copyable command", "gallery.utilities.code-title"),
            UI.CodeText(
                "dotnet run --project tools/WrailCli/WrailCli.csproj -- render <assembly> --type <widget-type>",
                "gallery.utilities.code",
                "Example gallery render command"))
            .AddClasses("gallery-utilities-code-card"),
        UI.Card("gallery.utilities.loading-card", CardVariant.Transparent,
            UI.Row("gallery.utilities.loading-row",
                UI.LoadingIndicator("gallery.utilities.loading", "Loading indicator preview",
                    LoadingIndicatorSize.Compact).AddClasses("gallery-loading"),
                UI.Text("Indeterminate work", "gallery.utilities.loading-label")
                    .Classes("gallery-loading-label"))
                .Classes("gallery-utilities-loading-row")),
        UI.Button("Show toast", "gallery.toast.show", "gallery.utilities.toast-button")
            .Icon(WidgetGlyph.Check).Classes("gallery-utilities-toast-button"))
        .AddClasses("gallery-page");

    private StackElement BackgroundsPage() => UI.Stack("gallery.backgrounds",
        UI.SectionHeader(
            "Background surfaces",
            "gallery.backgrounds.header",
            description: "Sealed package artwork proves default, focus-driven, nested, fit, and fallback behavior."),
        UI.Card("gallery.backgrounds.focus-card", CardVariant.Subtle,
            UI.Text("Root focus replacement and retention", "gallery.backgrounds.focus-title"),
            UI.Text(
                "Move across the row: warm and cool replace the root artwork; Retain keeps the last accepted artwork.",
                "gallery.backgrounds.focus-description"),
            UI.Row("gallery.backgrounds.focus-row",
                UI.Button("Warm artwork", "gallery.toast.show", "gallery.backgrounds.warm")
                    .Classes("gallery-background-button")
                    .FocusBackground(new WidgetArtworkHandle(WarmBackgroundArtworkHandle)),
                UI.Button("Retain artwork", "gallery.toast.show", "gallery.backgrounds.retain")
                    .Classes("gallery-background-button"),
                UI.Button("Cool artwork", "gallery.toast.show", "gallery.backgrounds.cool")
                    .Classes("gallery-background-button")
                    .FocusBackground(new WidgetArtworkHandle(CoolBackgroundArtworkHandle)))
                .Classes("gallery-background-focus-row"))
            .AddClasses("gallery-background-demo-card"),
        UI.BackgroundSurface(
                UI.Card("gallery.backgrounds.contain.card", CardVariant.Transparent,
                    UI.Text("Nested Contain owner", "gallery.backgrounds.contain.title"),
                    UI.Text("This nested surface consumes its own focused artwork without replacing the root.",
                        "gallery.backgrounds.contain.description"),
                    UI.Button("Focus nested artwork", "gallery.toast.show", "gallery.backgrounds.contain.focus")
                        .Classes("gallery-background-button")
                        .FocusBackground(new WidgetArtworkHandle(WarmBackgroundArtworkHandle))),
                "gallery.backgrounds.contain.surface",
                BackgroundSurfaceArtwork.FromHandle(
                    new WidgetArtworkHandle(CoolBackgroundArtworkHandle), ImageFit.Contain))
            .UseFocusedDescendantArtwork()
            .AddClasses("gallery-background-demo-surface", "gallery-background-demo-surface--contain"),
        UI.BackgroundSurface(
                UI.Card("gallery.backgrounds.fill.card", CardVariant.Transparent,
                    UI.Text("Fill", "gallery.backgrounds.fill.title"),
                    UI.Text("Fill stretches the sealed warm artwork to the bounded sample region.",
                        "gallery.backgrounds.fill.description")),
                "gallery.backgrounds.fill.surface",
                BackgroundSurfaceArtwork.FromHandle(
                    new WidgetArtworkHandle(WarmBackgroundArtworkHandle), ImageFit.Fill))
            .AddClasses("gallery-background-demo-surface", "gallery-background-demo-surface--fill"),
        UI.BackgroundSurface(
                UI.Card("gallery.backgrounds.missing.card", CardVariant.Transparent,
                    UI.Text("Missing artwork fallback", "gallery.backgrounds.missing.title"),
                    UI.Text("An unresolved handle leaves the semantic foreground readable and actionable.",
                        "gallery.backgrounds.missing.description")),
                "gallery.backgrounds.missing.surface",
                BackgroundSurfaceArtwork.FromHandle(
                    new WidgetArtworkHandle(MissingBackgroundArtworkHandle), ImageFit.Cover))
            .AddClasses("gallery-background-demo-surface", "gallery-background-demo-surface--missing"))
        .AddClasses("gallery-page", "gallery-backgrounds-page");

    private StackElement ModalContent(WidgetNavigationSnapshot<GalleryRoute> navigation) =>
        _navigation.Scope(navigation, navigation.Route.Modal switch
        {
            GalleryModal.Picker => UI.Picker(
                "Choose density",
                "gallery.picker",
                navigation.InputScopeId,
                navigation.BackActionId!,
                [
                    new PickerOption("gallery.density.compact", "Compact", "gallery.density.compact",
                        IsSelected: _density == "Compact"),
                    new PickerOption("gallery.density.comfortable", "Comfortable", "gallery.density.comfortable",
                        IsSelected: _density == "Comfortable"),
                    new PickerOption("gallery.density.spacious", "Spacious", "gallery.density.spacious",
                        IsSelected: _density == "Spacious"),
                ],
                "B closes this nested scope and restores the main gallery."),
            GalleryModal.ActionSheet => UI.ActionSheet(
                "Example actions",
                "gallery.sheet",
                navigation.InputScopeId,
                navigation.BackActionId!,
                [
                    new ActionSheetItem("gallery.sheet.pin", "Pin sample", "gallery.sheet.pin", WidgetGlyph.Check),
                    new ActionSheetItem("gallery.sheet.share", "Share reference", "gallery.sheet.share", WidgetGlyph.Connection),
                    new ActionSheetItem("gallery.sheet.remove", "Remove example", "gallery.sheet.remove",
                        WidgetGlyph.Warning, Tone: ActionSheetItemTone.Danger),
                ],
                "Nested surfaces own B without stealing it from other widget windows."),
            _ => throw new InvalidOperationException("No modal is active."),
        });

    private string InitialFocus(WidgetNavigationSnapshot<GalleryRoute> navigation) =>
        navigation.InitialFocusId ?? ContentEntryFocus(navigation);

    private string ContentEntryFocus(WidgetNavigationSnapshot<GalleryRoute> navigation) =>
        navigation.Route.Modal switch
        {
            GalleryModal.Picker => $"gallery.density.{_density.ToLowerInvariant()}",
            GalleryModal.ActionSheet => "gallery.sheet.pin",
            _ => navigation.Route.Page switch
            {
                GalleryPage.Overview => "gallery.refresh",
                GalleryPage.Controls => "gallery.controls.compact.action",
                GalleryPage.Tiles => "gallery.media",
                GalleryPage.Backgrounds => "gallery.backgrounds.warm",
                _ => "gallery.utilities.toast-button",
            },
        };

    private void SetPage(GalleryPage page, string sourceFocusId) =>
        _navigation.Navigate(new GalleryRoute(page, GalleryModal.None), sourceFocusId);

    private void OpenModal(GalleryModal modal, string sourceFocusId) =>
        _navigation.Push(new GalleryRoute(Page, modal), sourceFocusId);

    private void SelectDensity(string density, string sourceFocusId)
    {
        _density = density;
        if (_navigation.Back(sourceFocusId) != WidgetNavigationResult.Changed)
            Invalidate();
    }

    private void ShowToast()
    {
        _showToast = true;
        var generation = Interlocked.Increment(ref _toastGeneration);
        Invalidate();
        _ = DismissToastAsync(generation, WidgetLifetimeToken);
    }

    private async Task DismissToastAsync(int generation, CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(3), cancellationToken).ConfigureAwait(false);
            if (generation != Volatile.Read(ref _toastGeneration)) return;
            _showToast = false;
            Invalidate();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
    }

    private static string TabId(GalleryPage page) => page switch
    {
        GalleryPage.Overview => "gallery.tab.overview",
        GalleryPage.Controls => "gallery.tab.controls",
        GalleryPage.Tiles => "gallery.tab.tiles",
        GalleryPage.Backgrounds => "gallery.tab.backgrounds",
        _ => "gallery.tab.utilities",
    };

    private static WidgetEncodedArtwork? ResolvePackagedPng(string fileName)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "assets", fileName);
        var file = new FileInfo(path);
        if (!file.Exists || file.Length is <= 0 or > ProtocolConstants.MaximumEncodedArtworkBytes)
            return null;
        var bytes = File.ReadAllBytes(path);
        return bytes.Length >= 24 && bytes.AsSpan(0, 8).SequenceEqual(
            new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 })
                ? new WidgetEncodedArtwork(WidgetArtworkContentType.Png, bytes)
                : null;
    }
}
