using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

internal static class PinnedPresentationLayoutTests
{
    private const string Generation = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";

    internal static async Task Run()
    {
        ExistingWidgetViewApiRemainsAdditive();
        CatalogIsBoundedAndVersioned();
        ProjectionIsDeclarativeBoundedAndVersioned();
        await SelectionDemandIsExplicitAndRevocable();
        CatalogChangesRequireCheckpointFallback();
    }

    private static void ProjectionIsDeclarativeBoundedAndVersioned()
    {
        var view = new WidgetView(UI.Text("Full", "full"), ActiveInputScopeId: "full")
        {
            PinnedLayouts =
            [
                WidgetView.PinnedLayout(
                    "compact", "Compact", Surface(360, 240),
                    UI.Stack("compact.root",
                        UI.Button("Play", "play", "compact.play")),
                    initialFocusId: "compact.play"),
            ],
        };
        var snapshot = view.CreateSnapshot("layouts.projection", 2);
        Equal(ProtocolConstants.PinnedPresentationProjectionsVersion,
            snapshot.ProtocolVersion);
        Equal("compact.root", snapshot.PinnedLayouts[0].Root?.Id);
        Equal("compact.root", snapshot.PinnedLayouts[0].ActiveInputScopeId);
        Equal("compact.play", snapshot.PinnedLayouts[0].InitialFocusId);

        AssertError(Baseline() with
        {
            ProtocolVersion = ProtocolConstants.PinnedPresentationProjectionsVersion,
            PinnedLayouts =
            [
                Layout("compact", "Compact", 360, 240) with
                {
                    Root = new ViewNode
                    {
                        Id = "projection.root",
                        Kind = ViewNodeKind.Stack,
                        Children = Enumerable.Range(
                                0, ProtocolConstants.MaximumPinnedPresentationAggregateNodeCount)
                            .Select(index => new ViewNode
                            {
                                Id = $"projection.item.{index}",
                                Kind = ViewNodeKind.Text,
                            }).ToArray(),
                    },
                    ActiveInputScopeId = "projection.root",
                },
            ],
        }, "$.pinnedLayouts", "aggregate_tree_too_large");

        AssertError(Baseline() with
        {
            ProtocolVersion = ProtocolConstants.PinnedPresentationProjectionsVersion,
            PinnedLayouts =
            [
                Layout("compact", "Compact", 360, 240) with
                {
                    Root = new ViewNode
                    {
                        Id = "projection.root",
                        Kind = ViewNodeKind.Stack,
                        Children = Enumerable.Range(0, 65)
                            .Select(index => new ViewNode
                            {
                                Id = $"projection.text.{index}",
                                Kind = ViewNodeKind.Text,
                                Text = new string('T', ProtocolConstants.MaximumStringLength),
                            }).ToArray(),
                    },
                    ActiveInputScopeId = "projection.root",
                },
            ],
        }, "$.pinnedLayouts", "aggregate_strings_too_large");

        AssertError(Baseline() with
        {
            ProtocolVersion = ProtocolConstants.PinnedPresentationProjectionsVersion,
            PinnedLayouts =
            [
                Layout("compact", "Compact", 360, 240) with
                {
                    Root = new ViewNode
                    {
                        Id = "projection.root",
                        Kind = ViewNodeKind.Stack,
                        Children = Enumerable.Range(
                                0, ProtocolConstants.MaximumPinnedPresentationAggregateResourceCount + 1)
                            .Select(index => new ViewNode
                            {
                                Id = $"projection.image.{index}",
                                Kind = ViewNodeKind.Image,
                                ArtworkHandle = $"artwork-{index}",
                            }).ToArray(),
                    },
                    ActiveInputScopeId = "projection.root",
                },
            ],
        }, "$.pinnedLayouts", "aggregate_resources_too_large");
    }

    private static async Task SelectionDemandIsExplicitAndRevocable()
    {
        var widget = new SelectionWidget();
        var selected = new ControllerInputEvent(
            ControllerButton.View,
            ControllerEventPhase.Pressed,
            ControllerInputContext.PinnedLayoutSelection)
        {
            PinnedLayoutId = "compact",
            IsPinnedLayoutSelected = true,
        };
        True(await widget.OnControllerInputAsync(selected),
            "A valid selected-layout notification was not handled.");
        Equal("compact", widget.LayoutId);
        True(await widget.OnControllerInputAsync(selected with
        {
            PinnedLayoutId = null,
            IsPinnedLayoutSelected = false,
        }), "A selected-layout revocation was not handled.");
        Equal<string?>(null, widget.LayoutId);
    }

    private static void ExistingWidgetViewApiRemainsAdditive()
    {
        var root = UI.Text("Ready", "root");
        var legacy = new WidgetView(root, null, null, "root", null);
        var (deconstructedRoot, initialFocus, quickActions, activeScope, surface) = legacy;
        True(ReferenceEquals(root, deconstructedRoot) && initialFocus is null &&
             quickActions is null && activeScope == "root" && surface is null,
            "The accepted five-value WidgetView constructor/Deconstruct API changed.");

        var additive = legacy with
        {
            PinnedLayouts =
            [
                Layout("compact", "Compact", 360, 240),
            ],
        };
        var snapshot = additive.CreateSnapshot("layouts.additive", 1);
        Equal(ProtocolConstants.PinnedPresentationLayoutsVersion, snapshot.ProtocolVersion);
        var restored = SnapshotJson.Deserialize(SnapshotJson.Serialize(snapshot));
        Equal(1, restored.PinnedLayouts.Count);
        Equal("compact", restored.PinnedLayouts[0].Id);
        Equal("Compact", restored.PinnedLayouts[0].Name);
    }

    private static void CatalogIsBoundedAndVersioned()
    {
        var snapshot = Baseline() with { PinnedLayouts = [Layout("compact", "Compact", 360, 240)] };
        var requirements = ProtocolVersionRequirements.Calculate(snapshot);
        Equal(ProtocolConstants.PinnedPresentationLayoutsVersion, requirements.RequiredVersion);
        True(requirements.Requirements.Any(requirement =>
                requirement.Feature == "pinned-presentation-layouts" &&
                requirement.Path == "$.pinnedLayouts"),
            "Pinned layouts omitted exact protocol-v20 requirement provenance.");
        True(ViewSnapshotValidator.Validate(snapshot with
            { ProtocolVersion = ProtocolConstants.PinnedPresentationLayoutsVersion - 1 })
            .Any(error => error.Code == "feature_requires_version" &&
                          error.Path == "$.pinnedLayouts"),
            "A pre-v20 snapshot admitted a pinned layout catalog.");

        AssertError(Baseline() with
        {
            PinnedLayouts = Enumerable.Range(0, ProtocolConstants.MaximumPinnedPresentationLayoutCount + 1)
                .Select(index => Layout($"layout.{index}", "Layout", 360, 240)).ToArray(),
        }, "$.pinnedLayouts", "too_many");
        AssertError(Baseline() with
        {
            PinnedLayouts = [Layout("same", "First", 360, 240), Layout("same", "Second", 480, 270)],
        }, "$.pinnedLayouts[1].id", "duplicate_identifier");
        AssertError(Baseline() with
        {
            PinnedLayouts = [Layout("not valid", "Invalid identity", 360, 240)],
        }, "$.pinnedLayouts[0].id", "invalid_identifier");
        AssertError(Baseline() with
        {
            PinnedLayouts = [Layout("compact", new string('N',
                ProtocolConstants.MaximumPinnedPresentationLayoutNameLength + 1), 360, 240)],
        }, "$.pinnedLayouts[0].name", "too_long");
        AssertError(Baseline() with
        {
            PinnedLayouts =
            [
                new()
                {
                    Id = "compact",
                    Name = "Compact",
                    Surface = new() { PreferredWidth = 360 },
                },
            ],
        }, "$.pinnedLayouts[0].surface.preferredWidth", "incomplete_surface_size");
    }

    private static void CatalogChangesRequireCheckpointFallback()
    {
        var previous = Baseline() with { Sequence = 9, ProtocolVersion = 20 };
        var current = previous with
        {
            Sequence = 10,
            PinnedLayouts = [Layout("compact", "Compact", 360, 240)],
        };
        var publication = WidgetPresentationDiff.Create(
            previous, current, Generation, 9,
            PresentationUpdateCapabilities.Current,
            WidgetPresentationTransactionKind.IncrementalUpdate);
        True(!publication.IsUpdate && publication.Update is null &&
             publication.FallbackReason == "pinned_layout_catalog_changed",
            "A changed pinned layout catalog escaped complete-checkpoint fallback.");
    }

    private static ViewSnapshot Baseline() => new()
    {
        ProtocolVersion = 20,
        Sequence = 1,
        WidgetInstanceId = "layouts.contract",
        ActiveInputScopeId = "root",
        Root = new() { Id = "root", Kind = ViewNodeKind.Stack },
    };

    private static PinnedPresentationLayout Layout(
        string id, string name, double width, double height) => new()
    {
        Id = id,
        Name = name,
        Surface = new()
        {
            Mode = WidgetSurfaceMode.Compact,
            PreferredWidth = width,
            PreferredHeight = height,
            MinimumWidth = 240,
            MinimumHeight = 180,
        },
    };

    private static WidgetSurfaceHints Surface(double width, double height) => new()
    {
        Mode = WidgetSurfaceMode.Compact,
        PreferredWidth = width,
        PreferredHeight = height,
        MinimumWidth = 240,
        MinimumHeight = 180,
    };

    private sealed class SelectionWidget : Widget
    {
        internal string? LayoutId { get; private set; }
        public override WidgetView Render() => new(UI.Text("Ready", "root"));
        public override ValueTask OnPinnedLayoutSelectionChangedAsync(
            string? layoutId,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            LayoutId = layoutId;
            return ValueTask.CompletedTask;
        }
    }

    private static void AssertError(ViewSnapshot snapshot, string path, string code)
    {
        True(ViewSnapshotValidator.Validate(snapshot).Any(error =>
                error.Path == path && error.Code == code),
            $"Expected {code} at {path}.");
    }

    private static void True(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private static void Equal<T>(T expected, T actual)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new InvalidOperationException($"Expected '{expected}', got '{actual}'.");
    }
}
