using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

internal static class ProtocolVersionRequirementsTests
{
    private const string InlinePng =
        "data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJ" +
        "AAAADUlEQVR42mP8z8BQDwAFgwJ/lK3Q7wAAAABJRU5ErkJggg==";

    public static Task SdkSnapshotsUseSharedMaximum()
    {
        var baseline = new WidgetView(UI.Text("Ready", "root"))
            .CreateSnapshot("requirements.baseline", 1);
        Equal(ProtocolConstants.BaselineVersion, baseline.ProtocolVersion);

        var combined = new WidgetView(
            UI.Stack("root", UI.Text("Ready", "status")),
            QuickActions:
            [
                new(
                    ControllerButton.X,
                    "refresh",
                    "Refresh",
                    new("network.status", "refresh")),
            ],
            Surface: new()
            {
                WidthMode = WidgetSurfaceAxisMode.Content,
                HeightMode = WidgetSurfaceAxisMode.FillAvailable,
            }).CreateSnapshot("requirements.maximum", 2);
        Equal(ProtocolConstants.SurfaceAxisSizingVersion, combined.ProtocolVersion);

        return Task.CompletedTask;
    }

    public static Task RawSnapshotsUseCompleteRequirementMatrix()
    {
        var cases = new RequirementCase[]
        {
            new("embedded media", "embedded-media-surface",
                ProtocolConstants.EmbeddedMediaSurfaceVersion,
                "$.embeddedMedia", snapshot => snapshot with
                {
                    EmbeddedMedia = new()
                    {
                        Id = "media",
                        AccessibleName = "Neutral media",
                        EntryAsset = "media/index.html",
                        Surface = new()
                        {
                            PreferredWidth = 760,
                            PreferredHeight = 425,
                            MinimumWidth = 320,
                            MinimumHeight = 180,
                        },
                        AspectRatio = 16.0 / 9.0,
                        Resources =
                        [
                            new() { Path = "media/index.html", ContentType = "text/html" },
                        ],
                    },
                }),
            new("media viewport", "media-viewport",
                ProtocolConstants.MediaViewportVersion,
                "$.root.children[0]", snapshot => snapshot with
                {
                    EmbeddedMedia = new()
                    {
                        Id = "media",
                        AccessibleName = "Neutral media",
                        EntryAsset = "media/index.html",
                        Surface = new()
                        {
                            PreferredWidth = 640,
                            PreferredHeight = 360,
                            MinimumWidth = 240,
                            MinimumHeight = 135,
                        },
                        AspectRatio = 16.0 / 9.0,
                        Resources =
                        [
                            new() { Path = "media/index.html", ContentType = "text/html" },
                        ],
                    },
                    Root = Root(new ViewNode
                    {
                        Id = "media.viewport",
                        Kind = ViewNodeKind.MediaViewport,
                        MediaSurfaceId = "media",
                        AccessibilityLabel = "Neutral media",
                    }),
                }),
            new("surface hints", "surface-hints", ProtocolConstants.SurfaceHintsVersion,
                "$.surface", snapshot => snapshot with { Surface = new() }),
            new("surface width policy", "surface-axis-sizing", ProtocolConstants.SurfaceAxisSizingVersion,
                "$.surface", snapshot => snapshot with
                {
                    Surface = new() { WidthMode = WidgetSurfaceAxisMode.Content },
                }),
            new("surface height policy", "surface-axis-sizing", ProtocolConstants.SurfaceAxisSizingVersion,
                "$.surface", snapshot => snapshot with
                {
                    Surface = new() { HeightMode = WidgetSurfaceAxisMode.FillAvailable },
                }),
            new("dashboard capability", "dashboard-capability-authority",
                ProtocolConstants.DashboardGestureAuthorityVersion,
                "$.quickActions[0].capability", snapshot => snapshot with
                {
                    QuickActions =
                    [
                        new(ControllerButton.X, "refresh", "Refresh",
                            new("network.status", "refresh")),
                    ],
                }),
            NodeCase("scroll", "scroll-container", ProtocolConstants.ScrollContainerVersion,
                Scroll(Text("item"))),
            NodeCase("pagination near-start", "scroll-pagination",
                ProtocolConstants.ScrollPaginationVersion,
                Scroll(Text("item")) with { ScrollNearStartActionId = "load.before" }),
            NodeCase("pagination near-end", "scroll-pagination",
                ProtocolConstants.ScrollPaginationVersion,
                Scroll(Text("item")) with { ScrollNearEndActionId = "load.after" }),
            NodeCase("pagination threshold", "scroll-pagination",
                ProtocolConstants.ScrollPaginationVersion,
                Scroll(Text("item")) with { ScrollPaginationThreshold = 2 }),
            NodeCase("slider", "slider", ProtocolConstants.SliderVersion, Slider()),
            NodeCase("slider interaction", "slider-interaction-mode",
                ProtocolConstants.SliderActivationVersion,
                Slider() with { SliderInteractionMode = SliderInteractionMode.ActivateToAdjust },
                "$.root.children[0].sliderInteractionMode"),
            NodeCase("loading", "loading-indicator", ProtocolConstants.LoadingIndicatorVersion,
                new()
                {
                    Id = "loading",
                    Kind = ViewNodeKind.LoadingIndicator,
                    AccessibilityLabel = "Loading",
                    IndicatorSize = LoadingIndicatorSize.Standard,
                }),
            NodeCase("inline PNG image", "inline-png-image",
                ProtocolConstants.InlinePngImageVersion,
                new()
                {
                    Id = "image",
                    Kind = ViewNodeKind.Image,
                    AccessibilityLabel = "Artwork",
                    ImageSource = InlinePng,
                    ImageFit = ImageFit.Contain,
                }, "$.root.children[0].imageSource"),
            NodeCase("inline PNG button", "inline-png-image",
                ProtocolConstants.InlinePngImageVersion,
                Button() with { ImageSource = InlinePng, ImageFit = ImageFit.Contain },
                "$.root.children[0].imageSource"),
            NodeCase("action surface", "action-surface", ProtocolConstants.ActionSurfaceVersion,
                new()
                {
                    Id = "surface",
                    Kind = ViewNodeKind.ActionSurface,
                    ActionId = "open",
                    AccessibilityLabel = "Open item",
                    ActionSurfaceOrientation = ActionSurfaceOrientation.Horizontal,
                    Children = [Text("surface-text")],
                }),
            NodeCase("responsive grid", "responsive-grid", ProtocolConstants.ResponsiveGridVersion,
                new()
                {
                    Id = "grid",
                    Kind = ViewNodeKind.Grid,
                    GridMinimumColumnWidth = 100,
                    Children = [Text("grid-text")],
                }),
            NodeCase("responsive visibility", "responsive-visibility",
                ProtocolConstants.ResponsiveVisibilityVersion,
                Text("conditional") with { VisibleWhen = ResponsiveVisibility.ExpandedOnly },
                "$.root.children[0].visibleWhen"),
            NodeCase("repeat one icon", "repeat-one-glyph",
                ProtocolConstants.RepeatOneGlyphVersion,
                new()
                {
                    Id = "repeat",
                    Kind = ViewNodeKind.Icon,
                    Glyph = WidgetGlyph.RepeatOne,
                    AccessibilityLabel = "Repeat one",
                }, "$.root.children[0].glyph"),
            NodeCase("repeat one button", "repeat-one-glyph",
                ProtocolConstants.RepeatOneGlyphVersion,
                Button() with { Glyph = WidgetGlyph.RepeatOne },
                "$.root.children[0].glyph"),
            NodeCase("focus persistence", "focus-persistence",
                ProtocolConstants.FocusPersistenceVersion,
                Button() with { FocusPersistenceId = "logical.primary" },
                "$.root.children[0].focusPersistenceId"),
            NodeCase("collection anchor", "cursor-collection-anchor",
                ProtocolConstants.CursorCollectionVersion,
                CursorCollection(), "$.root.children[0].collectionAnchorKey"),
            NodeCase("collection item", "cursor-collection-item",
                ProtocolConstants.CursorCollectionVersion,
                CursorCollection(), "$.root.children[0].children[0].collectionItemKey"),
            NodeCase("image artwork handle", "artwork-handle",
                ProtocolConstants.CursorCollectionVersion,
                new()
                {
                    Id = "artwork",
                    Kind = ViewNodeKind.Image,
                    ArtworkHandle = "artwork.1",
                    ImageFit = ImageFit.Cover,
                    AccessibilityLabel = "Artwork",
                }, "$.root.children[0].artworkHandle"),
            NodeCase("button artwork handle", "artwork-handle",
                ProtocolConstants.CursorCollectionVersion,
                Button() with { ArtworkHandle = "artwork.1", ImageFit = ImageFit.Contain },
                "$.root.children[0].artworkHandle"),
            NodeCase("text entry", "text-entry", ProtocolConstants.TextEntryVersion,
                new()
                {
                    Id = "entry",
                    Kind = ViewNodeKind.TextEntry,
                    ActionId = "entry.commit",
                    AccessibilityLabel = "Search",
                    TextEntryValue = "",
                    TextEntryPlaceholder = "Search",
                    TextEntryMaximumLength = 32,
                }),
            NodeCase("virtual collection window", "virtual-collection-window",
                ProtocolConstants.VirtualCollectionWindowVersion,
                CursorCollection() with
                {
                    VirtualCollectionWindow = new()
                    {
                        RequestGeneration = 1,
                        Change = VirtualCollectionWindowChange.Replace,
                        FirstItemIndex = 0,
                        TotalItemCount = 1,
                        HasBefore = false,
                        HasAfter = false,
                        EstimatedItemExtent = 44,
                    },
                }, "$.root.children[0].virtualCollectionWindow"),
        };

        foreach (var requirementCase in cases)
        {
            var snapshot = requirementCase.Create(BaselineSnapshot());
            var requirements = ProtocolVersionRequirements.Calculate(snapshot);
            Equal(requirementCase.Version, requirements.RequiredVersion,
                $"{requirementCase.Name} chose the wrong maximum version.");
            True(requirements.Requirements.Any(requirement =>
                    requirement.Feature == requirementCase.Feature &&
                    requirement.Version == requirementCase.Version &&
                    requirement.Path == requirementCase.Path),
                $"{requirementCase.Name} omitted exact requirement provenance.");

            var tooOld = snapshot with { ProtocolVersion = requirementCase.Version - 1 };
            var oldErrors = ViewSnapshotValidator.Validate(tooOld);
            True(oldErrors.Any(error =>
                    error.Code == "feature_requires_version" &&
                    error.Path == requirementCase.Path &&
                    error.Message.Contains(
                        $"version {requirementCase.Version} or later", StringComparison.Ordinal)),
                $"{requirementCase.Name} did not fail at its exact raw-snapshot path.");

            var admitted = snapshot with { ProtocolVersion = requirementCase.Version };
            True(!ViewSnapshotValidator.Validate(admitted).Any(error =>
                    error.Code == "feature_requires_version"),
                $"{requirementCase.Name} did not pass version admission at its requirement.");
        }

        var baselineRequirements = ProtocolVersionRequirements.Calculate(BaselineSnapshot());
        Equal(ProtocolConstants.BaselineVersion, baselineRequirements.RequiredVersion);
        Equal(0, baselineRequirements.Requirements.Count);

        VerifyEveryContainerPath();
        VerifyMaximumOfMany();
        return Task.CompletedTask;
    }

    private static void VerifyEveryContainerPath()
    {
        var nested = new ViewNode
        {
            Id = "row",
            Kind = ViewNodeKind.Row,
            Children =
            [
                Scroll(new ViewNode
                {
                    Id = "grid",
                    Kind = ViewNodeKind.Grid,
                    GridMinimumColumnWidth = 100,
                    Children =
                    [
                        new ViewNode
                        {
                            Id = "surface",
                            Kind = ViewNodeKind.ActionSurface,
                            ActionId = "open",
                            AccessibilityLabel = "Open",
                            ActionSurfaceOrientation = ActionSurfaceOrientation.Vertical,
                            Children =
                            [
                                new ViewNode
                                {
                                    Id = "nested-loading",
                                    Kind = ViewNodeKind.LoadingIndicator,
                                    AccessibilityLabel = "Loading",
                                    IndicatorSize = LoadingIndicatorSize.Compact,
                                    VisibleWhen = ResponsiveVisibility.ExpandedOnly,
                                },
                            ],
                        },
                    ],
                }),
            ],
        };
        var requirements = ProtocolVersionRequirements.Calculate(SnapshotWith(nested));
        True(requirements.Requirements.Any(requirement =>
                requirement.Feature == "loading-indicator" &&
                requirement.Path ==
                    "$.root.children[0].children[0].children[0].children[0].children[0]"),
            "The shared traversal skipped a legal nested container path.");
        Equal(ProtocolConstants.ResponsiveVisibilityVersion, requirements.RequiredVersion);
    }

    private static void VerifyMaximumOfMany()
    {
        var snapshot = BaselineSnapshot() with
        {
            Surface = new() { WidthMode = WidgetSurfaceAxisMode.Content },
            QuickActions =
            [
                new(ControllerButton.X, "refresh", "Refresh",
                    new("network.status", "refresh")),
            ],
            Root = Root(
                Slider() with { SliderInteractionMode = SliderInteractionMode.ActivateToAdjust },
                CursorCollection() with
                {
                    VirtualCollectionWindow = new()
                    {
                        RequestGeneration = 1,
                        Change = VirtualCollectionWindowChange.Replace,
                        FirstItemIndex = 0,
                        TotalItemCount = 1,
                        HasBefore = false,
                        HasAfter = false,
                        EstimatedItemExtent = 44,
                    },
                }),
        };
        var requirements = ProtocolVersionRequirements.Calculate(snapshot);
        Equal(ProtocolConstants.VirtualCollectionWindowVersion, requirements.RequiredVersion);
        True(requirements.Requirements.Select(requirement => requirement.Version).Distinct().Count() >= 6,
            "The representative combined tree did not retain all lower-version provenance.");
    }

    private static RequirementCase NodeCase(
        string name,
        string feature,
        int version,
        ViewNode node,
        string path = "$.root.children[0]") =>
        new(name, feature, version, path, snapshot => snapshot with { Root = Root(node) });

    private static ViewSnapshot BaselineSnapshot() => new()
    {
        ProtocolVersion = ProtocolConstants.BaselineVersion,
        Sequence = 1,
        WidgetInstanceId = "requirements.raw",
        ActiveInputScopeId = "root",
        Root = Root(Text("baseline")),
    };

    private static ViewSnapshot SnapshotWith(ViewNode child) =>
        BaselineSnapshot() with { Root = Root(child) };

    private static ViewNode Root(params ViewNode[] children) => new()
    {
        Id = "root",
        Kind = ViewNodeKind.Stack,
        Children = children,
    };

    private static ViewNode Text(string id) => new()
    {
        Id = id,
        Kind = ViewNodeKind.Text,
        Text = "Text",
    };

    private static ViewNode Button() => new()
    {
        Id = "button",
        Kind = ViewNodeKind.Button,
        Text = "Button",
        ActionId = "button.invoke",
        AccessibilityLabel = "Button",
    };

    private static ViewNode Slider() => new()
    {
        Id = "slider",
        Kind = ViewNodeKind.Slider,
        AccessibilityLabel = "Volume",
        AccessibilityValue = "50 percent",
        Minimum = 0,
        Maximum = 100,
        Value = 50,
        Step = 1,
        ValueChangedActionId = "volume.change",
    };

    private static ViewNode Scroll(params ViewNode[] children) => new()
    {
        Id = "scroll",
        Kind = ViewNodeKind.Scroll,
        ScrollAxis = ScrollAxis.Vertical,
        Children = children,
    };

    private static ViewNode CursorCollection() => Scroll(
        Text("item") with { CollectionItemKey = "item.1" }) with
    {
        CollectionAnchorKey = "item.1",
    };

    private static void True(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private static void Equal<T>(T expected, T actual, string? message = null)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new InvalidOperationException(
                message ?? $"Expected '{expected}', got '{actual}'.");
    }

    private sealed record RequirementCase(
        string Name,
        string Feature,
        int Version,
        string Path,
        Func<ViewSnapshot, ViewSnapshot> Create);
}
