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
        await TypedHandleAndPublicTestHostOwnSelection();
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

    private static async Task TypedHandleAndPublicTestHostOwnSelection()
    {
        var widget = new HandleWidget();
        await WidgetTestHost.InitializeAsync(widget);
        await WidgetTestHost.SetLifecycleStateAsync(
            widget, WidgetLifecycleState.Visible);
        var invalidations = 0;
        widget.Invalidated += (_, _) => invalidations++;
        var host = WidgetTestHost.CreatePinnedLayoutHost(
            widget, "layouts.handle", initialSequence: 7);
        var duplicateRejected = false;
        try
        {
            widget.CreatePinnedLayoutHandle(
                "compact", "Duplicate", Surface(360, 240));
        }
        catch (InvalidOperationException)
        {
            duplicateRejected = true;
        }
        True(duplicateRejected,
            "One widget registered duplicate typed pinned-layout identities.");

        var compactPresentation = host.CurrentSnapshot.PinnedLayouts.Single(
            layout => layout.Id == "compact");
        Equal("Compact", widget.Compact.Name);
        Equal<double?>(360D, widget.Compact.Surface.PreferredWidth);
        Equal("compact.root", compactPresentation.Root?.Id);
        Equal("compact.scope", compactPresentation.ActiveInputScopeId);
        Equal("compact.action", compactPresentation.InitialFocusId);
        True(!widget.Compact.IsSelected &&
             widget.Compact.SelectionCancellationToken.IsCancellationRequested,
            "An unselected handle exposed active demand.");

        True(await host.SelectAsync("compact"),
            "The public host did not select a current authored layout.");
        Equal("compact", host.SelectedLayoutId);
        True(widget.Compact.IsSelected && !widget.Details.IsSelected,
            "SDK selection was not visible before the author callback.");
        Equal(
            new SelectionObservation("compact", true, false),
            widget.SelectionObservations.Single());
        Equal(1, invalidations);
        var firstSelection = widget.Compact.SelectionCancellationToken;
        True(!firstSelection.IsCancellationRequested,
            "Current selection token was already canceled.");

        True(await host.SelectAsync("compact"),
            "An idempotent current selection was rejected.");
        Equal(1, widget.SelectionObservations.Count);
        Equal(1, invalidations);
        Equal(firstSelection, widget.Compact.SelectionCancellationToken);

        var staleSequence = host.CurrentSnapshot.Sequence;
        await host.ReplaceSnapshotAsync();
        var stale = await widget.OnControllerInputAsync(new ControllerInputEvent(
            ControllerButton.View,
            ControllerEventPhase.Pressed,
            ControllerInputContext.PinnedLayoutSelection,
            SnapshotSequence: staleSequence)
        {
            PinnedLayoutId = "details",
            IsPinnedLayoutSelected = true,
        });
        True(!stale && widget.Compact.IsSelected && !widget.Details.IsSelected,
            "A stale layout notification replaced current handle authority.");
        Equal(1, invalidations);

        True(await host.SelectAsync("host.full-widget"),
            "Full widget did not revoke package-authored demand.");
        True(firstSelection.IsCancellationRequested &&
             !widget.Compact.IsSelected && host.SelectedLayoutId is null,
            "Full widget retained package-authored selection authority.");
        Equal(2, invalidations);

        True(await host.RestoreAsync("compact"),
            "Persisted authored selection was not restored.");
        var restoredSelection = widget.Compact.SelectionCancellationToken;
        True(!restoredSelection.IsCancellationRequested &&
             !restoredSelection.Equals(firstSelection),
            "Restored selection did not receive a fresh scoped token.");
        Equal(3, invalidations);

        widget.IncludeCompact = false;
        await host.ReplaceSnapshotAsync();
        True(restoredSelection.IsCancellationRequested &&
             !widget.Compact.IsSelected && host.SelectedLayoutId is null,
            "Removing a selected layout did not revoke its scoped demand.");
        Equal(4, invalidations);
        True(!await host.SelectAsync("missing"),
            "The public host selected a layout absent from the current snapshot.");
        Equal(4, invalidations);

        widget.IncludeCompact = true;
        await host.ReplaceSnapshotAsync();
        True(await host.SelectAsync("legacy"),
            "A low-level layout could not coexist with typed handles.");
        True(!widget.Compact.IsSelected && !widget.Details.IsSelected &&
             widget.SelectionObservations[^1] ==
             new SelectionObservation("legacy", false, false),
            "Mixed low-level selection incorrectly acquired typed handle state.");
        True(await host.SelectAsync("details"),
            "The public host did not select the replacement projection.");
        var destroyingSelection = widget.Details.SelectionCancellationToken;
        True(await host.RouteActionAsync(
                ControllerButton.A, "details.action"),
            "Pinned projection action was not admitted through its exact root.");
        var action = await widget.Action.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Equal("details.play", action.ActionId);
        Equal("details.scope", action.InputScopeId);

        await WidgetTestHost.DestroyAsync(widget);
        True(destroyingSelection.IsCancellationRequested && !widget.Details.IsSelected,
            "Widget teardown retained pinned-layout demand authority.");
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

    private sealed class HandleWidget : Widget
    {
        internal HandleWidget()
        {
            Compact = CreatePinnedLayoutHandle(
                "compact", "Compact", Surface(360, 240),
                "compact.action", "compact.scope");
            Details = CreatePinnedLayoutHandle(
                "details", "Details", Surface(480, 300),
                "details.action", "details.scope");
        }

        internal PinnedLayoutHandle Compact { get; }
        internal PinnedLayoutHandle Details { get; }
        internal bool IncludeCompact { get; set; } = true;
        internal List<SelectionObservation> SelectionObservations { get; } = [];
        internal TaskCompletionSource<WidgetActionEvent> Action { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public override WidgetView Render()
        {
            var layouts = new List<PinnedPresentationLayout>();
            if (IncludeCompact)
                layouts.Add(Compact.Present(
                    UI.Stack("compact.root",
                            UI.Button("Play", "compact.play", "compact.action"))
                        .InputScope("compact.scope")));
            layouts.Add(Details.Present(
                UI.Stack("details.root",
                        UI.Button("Play", "details.play", "details.action"))
                    .InputScope("details.scope")));
            layouts.Add(WidgetView.PinnedLayout(
                "legacy", "Legacy", Surface(320, 220),
                UI.Stack("legacy.root",
                        UI.Button("Play", "legacy.play", "legacy.action"))
                    .InputScope("legacy.scope"),
                "legacy.action", "legacy.scope"));
            return new WidgetView(
                UI.Text("Full", "full"), ActiveInputScopeId: "full")
            {
                PinnedLayouts = layouts,
            };
        }

        public override ValueTask OnPinnedLayoutSelectionChangedAsync(
            string? layoutId,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            SelectionObservations.Add(new SelectionObservation(
                layoutId, Compact.IsSelected, Details.IsSelected));
            return ValueTask.CompletedTask;
        }

        public override ValueTask OnActionAsync(
            WidgetActionEvent action,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Action.TrySetResult(action);
            return ValueTask.CompletedTask;
        }
    }

    private sealed record SelectionObservation(
        string? LayoutId,
        bool CompactSelected,
        bool DetailsSelected);

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
