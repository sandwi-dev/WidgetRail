using GameBarAlternative.WidgetProtocol;
using GameBarAlternative.WidgetSdk;

namespace GameBarAlternative.Samples.SdkGalleryWidget;

public enum GalleryPage { Overview, Controls, Tiles, Utilities }
public enum GalleryModal { None, Picker, ActionSheet }

/// <summary>
/// Copyable, capability-free reference for the public controller-first SDK.
/// It intentionally has no custom worker, broker calls, or host-only helpers.
/// </summary>
public sealed class SdkGalleryWidget : Widget
{
    private GalleryPage _page;
    private GalleryModal _modal;
    private bool _compactMode = true;
    private string _density = "Comfortable";
    private TimeSpan _position = TimeSpan.FromSeconds(74);
    private bool _showToast;
    private int _toastGeneration;
    private string? _restoreFocusId;

    public GalleryPage Page => _page;
    public GalleryModal Modal => _modal;
    public bool CompactMode => _compactMode;
    public string Density => _density;
    public TimeSpan Position => _position;
    public bool IsToastVisible => _showToast;

    public override WidgetView Render()
    {
        var root = UI.Stack("gallery.root",
            Header(),
            Tabs(),
            _modal == GalleryModal.None ? PageContent() : ModalContent())
            .InputScope("gallery.root")
            .Classes("gallery-root");

        if (_showToast)
        {
            root = root with
            {
                Children =
                [
                    .. root.Children,
                    UI.Toast(
                        "Action received",
                        "The widget updated local state without taking controller focus.",
                        ToastTone.Success,
                        "gallery.toast",
                        TimeSpan.FromSeconds(3)),
                ],
            };
        }

        return new WidgetView(
            root,
            InitialFocusId: InitialFocus(),
            QuickActions:
            [
                new WidgetQuickAction(
                    ControllerButton.X,
                    "gallery.toast.show",
                    "Show feedback"),
            ],
            ActiveInputScopeId: _modal switch
            {
                GalleryModal.Picker => "gallery.picker.scope",
                GalleryModal.ActionSheet => "gallery.sheet.scope",
                _ => "gallery.root",
            },
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

        switch (action.ActionId)
        {
            case "gallery.tab.overview": SetPage(GalleryPage.Overview); break;
            case "gallery.tab.controls": SetPage(GalleryPage.Controls); break;
            case "gallery.tab.tiles": SetPage(GalleryPage.Tiles); break;
            case "gallery.tab.utilities": SetPage(GalleryPage.Utilities); break;
            case "gallery.compact.toggle": _compactMode = !_compactMode; break;
            case "gallery.picker.open":
                _modal = GalleryModal.Picker;
                _restoreFocusId = "gallery.picker.open";
                break;
            case "gallery.sheet.open":
                _modal = GalleryModal.ActionSheet;
                _restoreFocusId = "gallery.sheet.open";
                break;
            case "gallery.modal.back": _modal = GalleryModal.None; break;
            case "gallery.density.compact": SelectDensity("Compact"); break;
            case "gallery.density.comfortable": SelectDensity("Comfortable"); break;
            case "gallery.density.spacious": SelectDensity("Spacious"); break;
            case "gallery.scrub":
                if (action.RequestedValue is { } requested && double.IsFinite(requested))
                    _position = TimeSpan.FromMilliseconds(Math.Clamp(requested, 0, 225_000));
                break;
            case "gallery.toast.show":
            case "gallery.alert.acknowledge":
            case "gallery.empty.populate":
            case "gallery.media.open":
            case "gallery.app.open":
            case "gallery.refresh":
            case "gallery.sheet.pin":
            case "gallery.sheet.share":
                ShowToast();
                return ValueTask.CompletedTask;
            case "gallery.sheet.remove":
                _modal = GalleryModal.None;
                ShowToast();
                return ValueTask.CompletedTask;
            default:
                return ValueTask.CompletedTask;
        }

        Invalidate();
        return ValueTask.CompletedTask;
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

    private RowElement Tabs() => UI.SegmentedTabs(
        "gallery.tabs",
        TabId(_page),
        new SegmentedTab("gallery.tab.overview", "Overview", "gallery.tab.overview"),
        new SegmentedTab("gallery.tab.controls", "Controls", "gallery.tab.controls"),
        new SegmentedTab("gallery.tab.tiles", "Tiles", "gallery.tab.tiles"),
        new SegmentedTab("gallery.tab.utilities", "Utilities", "gallery.tab.utilities"));

    private ScrollElement PageContent() => UI.VerticalScroll(
        "gallery.page-scroll",
        _page switch
        {
            GalleryPage.Overview => OverviewPage(),
            GalleryPage.Controls => ControlsPage(),
            GalleryPage.Tiles => TilesPage(),
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
        UI.ResponsiveGrid("gallery.tiles.grid", 250, 2,
            UI.MediaTile(
                "Night Drive",
                "Playing",
                "gallery.media.open",
                "gallery.media",
                subtitle: "Example Artist",
                metadata: "Controller UI Sessions",
                artwork: TileArtwork.FromGlyph(WidgetGlyph.Music, "Music artwork")),
            UI.AppTile(
                "Sample Game",
                "Ready",
                "gallery.app.open",
                "gallery.app",
                subtitle: "Recently used",
                metadata: "Community library",
                artwork: TileArtwork.FromGlyph(WidgetGlyph.Play, "Application icon"))))
        .AddClasses("gallery-page");

    private StackElement UtilitiesPage() => UI.Stack("gallery.utilities",
        UI.SectionHeader(
            "Feedback and diagnostics",
            "gallery.utilities.header",
            description: "Use semantic components instead of custom drawing or focus hacks."),
        UI.Card("gallery.utilities.code-card", CardVariant.Subtle,
            UI.Text("Copyable command", "gallery.utilities.code-title"),
            UI.CodeText(
                "dotnet run --project tools/GbarCli/GbarCli.csproj -- render <assembly> --type <widget-type>",
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

    private WidgetElement ModalContent() => _modal switch
    {
        GalleryModal.Picker => UI.Picker(
            "Choose density",
            "gallery.picker",
            "gallery.picker.scope",
            "gallery.modal.back",
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
            "gallery.sheet.scope",
            "gallery.modal.back",
            [
                new ActionSheetItem("gallery.sheet.pin", "Pin sample", "gallery.sheet.pin", WidgetGlyph.Check),
                new ActionSheetItem("gallery.sheet.share", "Share reference", "gallery.sheet.share", WidgetGlyph.Connection),
                new ActionSheetItem("gallery.sheet.remove", "Remove example", "gallery.sheet.remove",
                    WidgetGlyph.Warning, Tone: ActionSheetItemTone.Danger),
            ],
            "Nested surfaces own B without stealing it from other widget windows."),
        _ => throw new InvalidOperationException("No modal is active."),
    };

    private string InitialFocus() => _modal switch
    {
        GalleryModal.Picker => $"gallery.density.{_density.ToLowerInvariant()}",
        GalleryModal.ActionSheet => "gallery.sheet.pin",
        _ => _restoreFocusId ?? TabId(_page),
    };

    private void SetPage(GalleryPage page)
    {
        _page = page;
        _modal = GalleryModal.None;
        _restoreFocusId = null;
    }

    private void SelectDensity(string density)
    {
        _density = density;
        _modal = GalleryModal.None;
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
        _ => "gallery.tab.utilities",
    };
}
