using System.Buffers.Binary;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

namespace WidgetRail.Samples.SdkGalleryWidget;

public enum GalleryPage { Overview, Controls, Tiles, Backgrounds, Utilities }
public enum GalleryModal { None, Picker, ActionSheet }
public enum GalleryRoute
{
    Overview,
    Controls,
    Tiles,
    Backgrounds,
    Utilities,
    Picker,
    ActionSheet,
}

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
    private const string SampleWebPArtworkHandle = "gallery.artwork.sample-webp-512-v1";
    private const string SampleWebPArtworkResource =
        "WidgetRail.Samples.SdkGalleryWidget.Assets.sample-webp.webp";
    private static readonly IReadOnlyDictionary<string, string> PackagedPngFiles =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [DefaultBackgroundArtworkHandle] =
                "WidgetRail.Samples.SdkGalleryWidget.Assets.background-default.png",
            [WarmBackgroundArtworkHandle] =
                "WidgetRail.Samples.SdkGalleryWidget.Assets.background-focus-warm.png",
            [CoolBackgroundArtworkHandle] =
                "WidgetRail.Samples.SdkGalleryWidget.Assets.background-focus-cool.png",
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
    private static readonly GalleryRoute[] RootRoutes =
    [
        GalleryRoute.Overview,
        GalleryRoute.Controls,
        GalleryRoute.Tiles,
        GalleryRoute.Backgrounds,
        GalleryRoute.Utilities,
    ];
    private readonly WidgetNavigator<GalleryRoute> _navigation;
    private bool _compactMode = true;
    private string _density = "Comfortable";
    private TimeSpan _position = TimeSpan.FromSeconds(74);
    private bool _showToast;
    private int _toastGeneration;

    public SdkGalleryWidget() => _navigation = CreateNavigatorWithOptions(
        Ids.Id("navigation"),
        GalleryRoute.Overview,
        new WidgetNavigatorOptions<GalleryRoute>
        {
            SharedRootScopeId = Ids.Id("root-scope"),
            RootRoutes = RootRoutes,
        });

    public GalleryPage Page => PageFor(_navigation.Value.RootRoute);
    public GalleryModal Modal => ModalFor(_navigation.Value.Route);
    public WidgetNavigationSnapshot<GalleryRoute> Navigation => _navigation.Value;
    public bool CompactMode => _compactMode;
    public string Density => _density;
    public TimeSpan Position => _position;
    public bool IsToastVisible => _showToast;

    public override WidgetView Render()
    {
        var navigation = _navigation.Value;
        var page = PageFor(navigation.RootRoute);
        WidgetElement body = navigation.Depth == 0
            ? RootNavigation(page)
            : ModalContent(navigation);
        var rootContent = UI.Stack("gallery.root.content",
            Header(),
            body,
            navigation.Depth == 0 ? RootNavigationHints() : UI.Spacer("gallery.modal.hints-spacer"))
            .Classes("gallery-root");

        if (navigation.Depth == 0)
        {
            rootContent = _navigation.Scope(navigation, rootContent)
                .Shortcut(ControllerButton.LeftBumper, "gallery.page.previous")
                .Shortcut(ControllerButton.RightBumper, "gallery.page.next")
                .Shortcut(ControllerButton.Y, "gallery.route.actions");
        }

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
                PreferredHeight = 600,
                MinimumWidth = 320,
                MinimumHeight = 280,
            })
        {
            FocusGroupEntryRequest = navigation.FocusGroupEntryRequest,
        };
    }

    public override ValueTask OnActionAsync(
        WidgetActionEvent action,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(action);
        cancellationToken.ThrowIfCancellationRequested();

        if (_navigation.TryHandleBack(action, action.FocusedElementId))
            return ValueTask.CompletedTask;

        switch (action.ActionId)
        {
            case "gallery.tab.overview":
                ActivatePage(GalleryRoute.Overview);
                return ValueTask.CompletedTask;
            case "gallery.tab.controls":
                ActivatePage(GalleryRoute.Controls);
                return ValueTask.CompletedTask;
            case "gallery.tab.tiles":
                ActivatePage(GalleryRoute.Tiles);
                return ValueTask.CompletedTask;
            case "gallery.tab.backgrounds":
                ActivatePage(GalleryRoute.Backgrounds);
                return ValueTask.CompletedTask;
            case "gallery.tab.utilities":
                ActivatePage(GalleryRoute.Utilities);
                return ValueTask.CompletedTask;
            case "gallery.page.previous":
                SwitchPage(-1);
                return ValueTask.CompletedTask;
            case "gallery.page.next":
                SwitchPage(1);
                return ValueTask.CompletedTask;
            case "gallery.route.actions":
                _navigation.PushFromAction(GalleryRoute.ActionSheet, action);
                return ValueTask.CompletedTask;
            case "gallery.compact.toggle": _compactMode = !_compactMode; break;
            case "gallery.picker.open":
                _navigation.PushFromAction(GalleryRoute.Picker, action);
                return ValueTask.CompletedTask;
            case "gallery.sheet.open":
                _navigation.PushFromAction(GalleryRoute.ActionSheet, action);
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
                BackToPage(action.SourceElementId);
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
        if (PackagedPngFiles.TryGetValue(handle.Value, out var resourceName))
            return ValueTask.FromResult(ResolvePackagedPng(resourceName));
        return ValueTask.FromResult<WidgetEncodedArtwork?>(
            handle.Value == SampleWebPArtworkHandle
                ? ResolvePackagedWebP(SampleWebPArtworkResource)
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

    private ScrollElement PageContent(GalleryPage page)
    {
        var content = (page switch
        {
            GalleryPage.Overview => OverviewPage(),
            GalleryPage.Controls => ControlsPage(),
            GalleryPage.Tiles => TilesPage(),
            GalleryPage.Backgrounds => BackgroundsPage(),
            _ => UtilitiesPage(),
        }).RememberChildFocus(PageInitialFocus(page));
        return UI.VerticalScroll("gallery.page-scroll", content)
            .Classes("gallery-page-scroll");
    }

    private StackElement RootNavigation(GalleryPage page) =>
        UI.NavigationShell(
            "gallery.shell",
            TabId(page),
            NavigationContentEntryFocus(page),
            PageContent(page),
            Destinations,
            expandedPane: null,
            expandedPaneEntryFocusId: null,
            compactLeadingAdornment: UI.Row(
                    "gallery.hint.section.previous",
                    UI.Text(
                            "LB",
                            "gallery.hint.section.previous.key",
                            "LB, Previous section")
                        .Classes("gallery-section-bumper-label"))
                .Classes("wrail-controller-hint__key", "gallery-section-bumper-key"),
            compactTrailingAdornment: UI.Row(
                    "gallery.hint.section.next",
                    UI.Text(
                            "RB",
                            "gallery.hint.section.next.key",
                            "RB, Next section")
                        .Classes("gallery-section-bumper-label"))
                .Classes("wrail-controller-hint__key", "gallery-section-bumper-key"));

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
            new ComponentAction("Populate example", "gallery.empty.populate", WidgetGlyph.Play)),
        UI.Row("gallery.overview.hints",
            UI.ControllerHint(ControllerButton.DPadRight, "Navigate", "gallery.hint.navigate"),
            UI.ControllerHint(ControllerButton.A, "Select", "gallery.hint.select"),
            UI.ControllerHint(ControllerButton.B, "Back", "gallery.hint.back"),
            UI.ControllerHint(ControllerButton.RightStick, "Scroll", "gallery.hint.scroll"))
            .Classes("gallery-controller-hints"))
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
        UI.Card("gallery.controls.density-preview", CardVariant.Transparent,
            UI.Text(DensitySummary(), "gallery.controls.density-preview.title"),
            UI.Text(
                "The same repeated content changes geometry without changing focus IDs.",
                "gallery.controls.density-preview.description"),
            UI.Row("gallery.controls.density-preview.row-one",
                UI.StatusBadge("Alpha", StatusTone.Info,
                    "gallery.controls.density-preview.alpha"),
                UI.StatusBadge("Beta", StatusTone.Success,
                    "gallery.controls.density-preview.beta"),
                UI.StatusBadge("Gamma", StatusTone.Warning,
                    "gallery.controls.density-preview.gamma"))
                .Classes("gallery-density-preview-row", DensityRowClass()),
            UI.Row("gallery.controls.density-preview.row-two",
                UI.StatusBadge("Delta", StatusTone.Info,
                    "gallery.controls.density-preview.delta"),
                UI.StatusBadge("Epsilon", StatusTone.Success,
                    "gallery.controls.density-preview.epsilon"),
                UI.StatusBadge("Zeta", StatusTone.Warning,
                    "gallery.controls.density-preview.zeta"))
                .Classes("gallery-density-preview-row", DensityRowClass()))
            .AddClasses("gallery-density-preview", DensityClass()),
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
                UI.PosterTile(
                    "WebP First Frame",
                    "Ready",
                    "gallery.app.open",
                    "gallery.webp",
                    subtitle: "Provider-neutral",
                    metadata: "Bounded encoded artwork",
                    artwork: TileArtwork.FromHandle(
                        new WidgetArtworkHandle(SampleWebPArtworkHandle),
                        "Provider-neutral encoded WebP artwork"))
                    .PresentOnFocus(UI.Stack("gallery.webp.presentation",
                        UI.Text("Embedded WebP", "gallery.webp.presentation.title"),
                        UI.Text("A visible 512 by 512 package resource with bounded encoded bytes.",
                            "gallery.webp.presentation.detail"))),
                UI.PosterTile(
                    "A deliberately longer poster title that demonstrates bounded two-line copy",
                    "Available",
                    "gallery.app.open",
                    "gallery.poster",
                    subtitle: "SDK Gallery",
                    metadata: "Provider-neutral poster",
                    artwork: TileArtwork.FromHandle(
                        new WidgetArtworkHandle(SampleArtworkHandle),
                        "Provider-neutral poster artwork"))
                    .PresentOnFocus(UI.Stack("gallery.poster.presentation",
                        UI.Text("PosterTile", "gallery.poster.presentation.title"),
                        UI.Text("Portrait artwork, bounded two-line copy, and one complete action target.",
                            "gallery.poster.presentation.detail")))),
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
            UI.Text("Presentational command", "gallery.utilities.code-title"),
            UI.Text(
                "CodeText is nonfocusable and does not provide a clipboard action.",
                "gallery.utilities.code-description"),
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
                .RememberChildFocus("gallery.backgrounds.warm")
                .Classes("gallery-background-focus-row"))
            .AddClasses("gallery-background-demo-card"),
        UI.BackgroundSurface(
                UI.Card("gallery.backgrounds.cover.card", CardVariant.Transparent,
                    UI.Text("Cover", "gallery.backgrounds.cover.title"),
                    UI.Text("Cover fills every edge and crops overflow when aspect ratios differ.",
                        "gallery.backgrounds.cover.description")),
                "gallery.backgrounds.cover.surface",
                BackgroundSurfaceArtwork.FromHandle(
                    new WidgetArtworkHandle(DefaultBackgroundArtworkHandle), ImageFit.Cover))
            .AddClasses("gallery-background-demo-surface", "gallery-background-fit-surface",
                "gallery-background-demo-surface--cover"),
        UI.BackgroundSurface(
                UI.Card("gallery.backgrounds.contain.card", CardVariant.Transparent,
                    UI.Text("Contain", "gallery.backgrounds.contain.title"),
                    UI.Text("Contain preserves the whole image and may leave unused space.",
                        "gallery.backgrounds.contain.description"),
                    UI.Button("Focus nested artwork", "gallery.toast.show", "gallery.backgrounds.contain.focus")
                        .Classes("gallery-background-button")
                        .FocusBackground(new WidgetArtworkHandle(WarmBackgroundArtworkHandle))),
                "gallery.backgrounds.contain.surface",
                BackgroundSurfaceArtwork.FromHandle(
                    new WidgetArtworkHandle(DefaultBackgroundArtworkHandle), ImageFit.Contain))
            .UseFocusedDescendantArtwork()
            .AddClasses("gallery-background-demo-surface", "gallery-background-fit-surface",
                "gallery-background-demo-surface--contain"),
        UI.BackgroundSurface(
                UI.Card("gallery.backgrounds.fill.card", CardVariant.Transparent,
                    UI.Text("Fill", "gallery.backgrounds.fill.title"),
                    UI.Text("Fill stretches the sealed default artwork to the bounded sample region.",
                        "gallery.backgrounds.fill.description")),
                "gallery.backgrounds.fill.surface",
                BackgroundSurfaceArtwork.FromHandle(
                    new WidgetArtworkHandle(DefaultBackgroundArtworkHandle), ImageFit.Fill))
            .AddClasses("gallery-background-demo-surface", "gallery-background-fit-surface",
                "gallery-background-demo-surface--fill"),
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
        _navigation.Scope(navigation, navigation.Route switch
        {
            GalleryRoute.Picker => UI.Picker(
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
            GalleryRoute.ActionSheet => UI.ActionSheet(
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

    private string? InitialFocus(WidgetNavigationSnapshot<GalleryRoute> navigation) =>
        navigation.Route switch
        {
            GalleryRoute.Picker => navigation.InitialFocusId ??
                $"gallery.density.{_density.ToLowerInvariant()}",
            GalleryRoute.ActionSheet => navigation.InitialFocusId ?? "gallery.sheet.pin",
            _ when navigation.Revision == 0 => PageInitialFocus(PageFor(navigation.RootRoute)),
            _ => navigation.InitialFocusId,
        };

    private static RowElement RootNavigationHints() => UI.Row(
        "gallery.section.hints",
        UI.ControllerHint(ControllerButton.Y, "Example actions", "gallery.hint.section.actions"))
        .Classes("gallery-controller-hints", "gallery-section-hints");

    private static string NavigationContentEntryFocus(GalleryPage page) =>
        page == GalleryPage.Backgrounds
            ? "gallery.backgrounds.focus-row"
            : PageInitialFocus(page);

    private static string PageInitialFocus(GalleryPage page) => page switch
    {
        GalleryPage.Overview => "gallery.refresh",
        GalleryPage.Controls => "gallery.controls.compact.action",
        GalleryPage.Tiles => "gallery.media",
        GalleryPage.Backgrounds => "gallery.backgrounds.warm",
        _ => "gallery.utilities.toast-button",
    };

    private void ActivatePage(GalleryRoute route)
    {
        _ = _navigation.NavigateRoot(route);
    }

    private void SwitchPage(int offset)
    {
        var current = Array.IndexOf(RootRoutes, _navigation.Value.RootRoute);
        if (current < 0)
            throw new InvalidOperationException("The current Gallery root route is not registered.");
        var destination = RootRoutes[(current + offset + RootRoutes.Length) % RootRoutes.Length];
        _ = _navigation.NavigateRoot(
            destination, PageFocusGroupId(PageFor(destination)));
    }

    private void SelectDensity(string density, string sourceFocusId)
    {
        _density = density;
        if (BackToPage(sourceFocusId) != WidgetNavigationResult.Changed)
            Invalidate();
    }

    private WidgetNavigationResult BackToPage(string? sourceFocusId)
        => _navigation.Back(sourceFocusId);

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

    private static GalleryPage PageFor(GalleryRoute route) => route switch
    {
        GalleryRoute.Overview => GalleryPage.Overview,
        GalleryRoute.Controls => GalleryPage.Controls,
        GalleryRoute.Tiles => GalleryPage.Tiles,
        GalleryRoute.Backgrounds => GalleryPage.Backgrounds,
        GalleryRoute.Utilities => GalleryPage.Utilities,
        _ => throw new InvalidOperationException("A nested Gallery route cannot be rendered as a root page."),
    };

    private static GalleryModal ModalFor(GalleryRoute route) => route switch
    {
        GalleryRoute.Picker => GalleryModal.Picker,
        GalleryRoute.ActionSheet => GalleryModal.ActionSheet,
        _ => GalleryModal.None,
    };

    private static string PageFocusGroupId(GalleryPage page) => page switch
    {
        GalleryPage.Overview => "gallery.overview",
        GalleryPage.Controls => "gallery.controls",
        GalleryPage.Tiles => "gallery.tiles",
        GalleryPage.Backgrounds => "gallery.backgrounds",
        _ => "gallery.utilities",
    };

    private string DensityClass() =>
        $"gallery-density-preview--{_density.ToLowerInvariant()}";

    private string DensityRowClass() =>
        $"gallery-density-preview-row--{_density.ToLowerInvariant()}";

    private string DensitySummary() => _density switch
    {
        "Compact" => "Compact · 4 DIP row gap · 6 DIP panel padding",
        "Spacious" => "Spacious · 20 DIP row gap · 24 DIP panel padding",
        _ => "Comfortable · 12 DIP row gap · 14 DIP panel padding",
    };

    private static WidgetEncodedArtwork? ResolvePackagedPng(string resourceName)
    {
        using var stream = typeof(SdkGalleryWidget).Assembly
            .GetManifestResourceStream(resourceName);
        if (stream is null || !stream.CanSeek ||
            stream.Length is <= 0 or > ProtocolConstants.MaximumEncodedArtworkBytes)
            return null;
        var bytes = new byte[checked((int)stream.Length)];
        stream.ReadExactly(bytes);
        return bytes.Length >= 24 && bytes.AsSpan(0, 8).SequenceEqual(
            new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 })
                ? new WidgetEncodedArtwork(WidgetArtworkContentType.Png, bytes)
                : null;
    }

    private static WidgetEncodedArtwork? ResolvePackagedWebP(string resourceName)
    {
        using var stream = typeof(SdkGalleryWidget).Assembly
            .GetManifestResourceStream(resourceName);
        if (stream is null || !stream.CanSeek ||
            stream.Length is < 20 or > ProtocolConstants.MaximumEncodedArtworkBytes)
            return null;
        var bytes = new byte[checked((int)stream.Length)];
        stream.ReadExactly(bytes);
        return bytes.AsSpan(0, 4).SequenceEqual("RIFF"u8) &&
               bytes.AsSpan(8, 4).SequenceEqual("WEBP"u8) &&
               BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(4, 4)) == bytes.Length - 8
            ? new WidgetEncodedArtwork(WidgetArtworkContentType.WebP, bytes)
            : null;
    }
}
