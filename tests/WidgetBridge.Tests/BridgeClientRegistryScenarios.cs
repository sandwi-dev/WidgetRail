using System.Threading.Channels;
using WidgetRail.WidgetBridge;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetRuntime;
using WidgetRail.WidgetSdk;
using WidgetRail.PlatformBroker;
using WidgetRail.PlatformDiagnostics;

internal static class BridgeClientRegistryScenarios
{
    internal static async Task VisibleRegistrationPublishesInvalidationExactlyOnce()
    {
        var configured = Widget("notification-registry", worker: 'r', catalog: 'r');
        await using var fixture = new RegistryFixture(Catalog(configured));

        await fixture.SetLifecycleAsync(
            configured.Id, WidgetLifecycleState.Visible);
        var current = fixture.Clients.Single();
        current.RaiseInvalidated(17);
        await fixture.Registry.DrainNotificationsAsync(configured.Id);

        RegistryAssert.Equal(1, fixture.Invalidations.Count);
        RegistryAssert.Equal(configured.Id, fixture.Invalidations[0].WidgetId);
        RegistryAssert.Equal(17L, fixture.Invalidations[0].Revision);
    }

    internal static async Task PinnedLayoutSelectionIsGenerationBound()
    {
        var configured = Widget("pinned-selection", worker: 'p', catalog: 'p');
        await using var fixture = new RegistryFixture(Catalog(configured));
        await fixture.SetLifecycleAsync(configured.Id, WidgetLifecycleState.Visible);
        var client = fixture.Clients.Single();
        var generation = configured.PublicDescriptor().RuntimeGeneration;
        var input = new ControllerInputEvent(
            ControllerButton.View,
            ControllerEventPhase.Pressed,
            ControllerInputContext.PinnedLayoutSelection)
        {
            PinnedLayoutId = "compact",
            IsPinnedLayoutSelected = true,
        };

        using (var publication = await fixture.Registry.SendControllerInputAsync(
                   configured.Id, input, generation,
                   CancellationToken.None, CancellationToken.None))
            RegistryAssert.True(publication.Value);
        RegistryAssert.Equal(1, client.ControllerInputs.Count);
        RegistryAssert.Equal("compact", client.ControllerInputs[0].PinnedLayoutId);

        await RegistryAssert.ThrowsAsync<BridgeProtocolException>(() =>
            fixture.Registry.SendControllerInputAsync(
                configured.Id, input, new string('f', 32),
                CancellationToken.None, CancellationToken.None));
        RegistryAssert.Equal(1, client.ControllerInputs.Count);
    }

    internal static async Task FullWidgetPinningRequiresManifestAuthority()
    {
        var configured = Widget("custom-pinning", worker: 'c', catalog: 'c') with { PinningSupported = true };
        await using var fixture = new RegistryFixture(Catalog(configured));
        await fixture.SetLifecycleAsync(configured.Id, WidgetLifecycleState.Interactive);
        var snapshot = (await fixture.GetSnapshotAsync(configured.Id)).Snapshot;
        RegistryAssert.True(configured.PublicDescriptor().PinningSupported);
        RegistryAssert.True(!configured.PublicDescriptor().FullWidgetPinningSupported);
        foreach (var context in new[] { ControllerInputContext.PinnedSurface, ControllerInputContext.PinnedLayoutSelection })
        {
            var input = new ControllerInputEvent(ControllerButton.A, ControllerEventPhase.Pressed,
                context, snapshot.InitialFocusId, Sequence: 1,
                ActiveInputScopeId: snapshot.ActiveInputScopeId, SnapshotSequence: snapshot.Sequence)
            { PinnedLayoutId = "host.full-widget", IsPinnedLayoutSelected = true };
            await RegistryAssert.ThrowsAsync<BridgeStalePinnedInputAuthorityException>(() =>
                fixture.Registry.SendControllerInputAsync(configured.Id, input,
                    configured.PublicDescriptor().RuntimeGeneration, CancellationToken.None, CancellationToken.None));
        }
        RegistryAssert.Equal(0, fixture.Clients.Single().ControllerInputs.Count);
    }

    internal static async Task PinnedSurfaceInputRequiresExactAuthority()
    {
        var configured = Widget("pinned-input", worker: 'i', catalog: 'i') with
        { PinningSupported = true, FullWidgetPinningSupported = true };
        var selectedActionId = "compact-quality.high";
        var selectedOptionDisabled = false;
        var sliderMinimum = 0d;
        var sliderMaximum = 100d;
        var sliderStep = 5d;
        await using var fixture = new RegistryFixture(
            Catalog(configured),
            configure: (_, client) => client.SnapshotFactory = sequence => new ViewSnapshot
            {
                ProtocolVersion = ProtocolConstants.PinnedPresentationProjectionsVersion,
                Sequence = sequence,
                WidgetInstanceId = configured.InstanceId,
                ActiveInputScopeId = "full.root",
                InitialFocusId = "full.play",
                Root = new ViewNode
                {
                    Id = "full.root",
                    Kind = ViewNodeKind.Stack,
                    InputScopeId = "full.root",
                    Children =
                    [
                        new ViewNode { Id = "full.play", Kind = ViewNodeKind.Button,
                            ActionId = "full-play" },
                    ],
                },
                PinnedLayouts =
                [
                    new PinnedPresentationLayout
                    {
                        Id = "compact",
                        Name = "Compact",
                        Surface = new WidgetSurfaceHints
                        {
                            PreferredWidth = 360,
                            PreferredHeight = 240,
                            MinimumWidth = 240,
                            MinimumHeight = 180,
                        },
                        Root = new ViewNode
                        {
                            Id = "compact.root",
                            Kind = ViewNodeKind.Stack,
                            InputScopeId = "compact.root",
                            Children =
                            [
                                new ViewNode { Id = "compact.play", Kind = ViewNodeKind.Button,
                                    ActionId = "compact-play" },
                                new ViewNode { Id = "compact.label", Kind = ViewNodeKind.Text },
                                new ViewNode
                                {
                                    Id = "compact.mute",
                                    Kind = ViewNodeKind.Slider,
                                    Minimum = 0,
                                    Maximum = 100,
                                    Value = 20,
                                    Step = 5,
                                },
                                new ViewNode
                                {
                                    Id = "compact.seek",
                                    Kind = ViewNodeKind.Slider,
                                    ValueChangedActionId = "compact-seek.changed",
                                    Minimum = sliderMinimum,
                                    Maximum = sliderMaximum,
                                    Value = 40,
                                    Step = sliderStep,
                                    SliderInteractionMode = SliderInteractionMode.ActivateToAdjust,
                                },
                                new ViewNode
                                {
                                    Id = "compact.quality",
                                    Kind = ViewNodeKind.Select,
                                    Text = "Quality",
                                    AccessibilityValue = "High",
                                    SelectOptions =
                                    [
                                        new WidgetSelectOption(
                                            "auto", "Automatic", "compact-quality.auto"),
                                        new WidgetSelectOption(
                                            "high", "High", selectedActionId,
                                            IsSelected: true,
                                            IsDisabled: selectedOptionDisabled),
                                    ],
                                },
                            ],
                        },
                        ActiveInputScopeId = "compact.root",
                        InitialFocusId = "compact.play",
                    },
                ],
            });
        await fixture.SetLifecycleAsync(configured.Id, WidgetLifecycleState.Interactive);
        var client = fixture.Clients.Single();
        var snapshot = (await fixture.GetSnapshotAsync(configured.Id)).Snapshot;
        var generation = configured.PublicDescriptor().RuntimeGeneration;
        var input = new ControllerInputEvent(
            ControllerButton.A,
            ControllerEventPhase.Pressed,
            ControllerInputContext.PinnedSurface,
            FocusedElementId: "compact.play",
            Sequence: 1,
            ActiveInputScopeId: "compact.root",
            SnapshotSequence: snapshot.Sequence)
        {
            PinnedLayoutId = "compact",
        };

        var wire = BridgeJson.ToElement(new BridgeControllerInputRequest(
            configured.Id, input, generation));
        RegistryAssert.Equal("pinnedSurface",
            wire.GetProperty("input").GetProperty("context").GetString());
        RegistryAssert.Equal("compact",
            wire.GetProperty("input").GetProperty("pinnedLayoutId").GetString());
        var roundTrip = BridgeJson.FromElement<BridgeControllerInputRequest>(wire);
        RegistryAssert.Equal(ControllerInputContext.PinnedSurface, roundTrip.Input.Context);

        using (var publication = await fixture.Registry.SendControllerInputAsync(
                   configured.Id, input, generation,
                   CancellationToken.None, CancellationToken.None))
            RegistryAssert.True(publication.Value);
        using (var publication = await fixture.Registry.SendControllerInputAsync(
                   configured.Id, input with
                   {
                       FocusedElementId = "full.play",
                       ActiveInputScopeId = "full.root",
                       PinnedLayoutId = "host.full-widget",
                   }, generation, CancellationToken.None, CancellationToken.None))
            RegistryAssert.True(publication.Value);
        RegistryAssert.Equal(2, client.ControllerInputs.Count);

        var sliderInput = new ControllerInputEvent(
            ControllerButton.DPadRight,
            ControllerEventPhase.Pressed,
            ControllerInputContext.PinnedSurface,
            FocusedElementId: "compact.seek",
            Sequence: 2,
            ActiveInputScopeId: "compact.root",
            SnapshotSequence: snapshot.Sequence,
            RequestedValue: 45)
        {
            PinnedLayoutId = "compact",
        };
        var successor = (await fixture.GetSnapshotAsync(configured.Id)).Snapshot;
        using (var publication = await fixture.Registry.SendControllerInputAsync(
                   configured.Id, sliderInput, generation,
                   CancellationToken.None, CancellationToken.None,
                   expectedActionId: "compact-seek.changed"))
            RegistryAssert.True(publication.Value);
        RegistryAssert.Equal(3, client.ControllerInputs.Count);

        var sliderAuthority = successor;
        foreach (var mutate in new Action[]
        {
            () => sliderMinimum = 5,
            () => sliderMaximum = 95,
            () => sliderStep = 10,
        })
        {
            mutate();
            var changedSlider = (await fixture.GetSnapshotAsync(configured.Id)).Snapshot;
            await RegistryAssert.ThrowsAsync<BridgeStalePinnedInputAuthorityException>(() =>
                fixture.Registry.SendControllerInputAsync(
                    configured.Id, sliderInput with
                    {
                        SnapshotSequence = sliderAuthority.Sequence,
                        Sequence = 20,
                    }, generation, CancellationToken.None, CancellationToken.None,
                    expectedActionId: "compact-seek.changed"));
            sliderAuthority = changedSlider;
        }
        sliderMinimum = 0;
        sliderMaximum = 100;
        sliderStep = 5;
        sliderAuthority = (await fixture.GetSnapshotAsync(configured.Id)).Snapshot;

        var selectInput = new ControllerInputEvent(
            ControllerButton.A,
            ControllerEventPhase.Pressed,
            ControllerInputContext.PinnedSurface,
            FocusedElementId: "compact.quality",
            Sequence: 3,
            ActiveInputScopeId: "compact.root",
            SnapshotSequence: sliderAuthority.Sequence)
        {
            PinnedLayoutId = "compact",
        };
        var selectSuccessor = (await fixture.GetSnapshotAsync(configured.Id)).Snapshot;
        using (var publication = await fixture.Registry.SendControllerInputAsync(
                   configured.Id, selectInput, generation,
                   CancellationToken.None, CancellationToken.None,
                   expectedSelectOptionActionId: selectedActionId))
            RegistryAssert.True(publication.Value);
        RegistryAssert.Equal(1, client.ActionEvents.Count);
        RegistryAssert.Equal(selectedActionId, client.ActionEvents[0].ActionId);
        RegistryAssert.Equal("compact.quality", client.ActionEvents[0].SourceElementId);
        RegistryAssert.Equal(3L, client.ActionEvents[0].Sequence);

        selectedOptionDisabled = true;
        var disabledSuccessor = (await fixture.GetSnapshotAsync(configured.Id)).Snapshot;
        await RegistryAssert.ThrowsAsync<BridgeStalePinnedInputAuthorityException>(() =>
            fixture.Registry.SendControllerInputAsync(
                configured.Id, selectInput with
                {
                    SnapshotSequence = selectSuccessor.Sequence,
                    Sequence = 4,
                }, generation, CancellationToken.None, CancellationToken.None,
                expectedSelectOptionActionId: selectedActionId));
        RegistryAssert.Equal(1, client.ActionEvents.Count);
        RegistryAssert.Equal(ControllerButton.DPadRight, client.ControllerInputs[2].Button);
        RegistryAssert.Equal(45d, client.ControllerInputs[2].RequestedValue);
        RegistryAssert.Equal(successor.Sequence, client.ControllerInputs[2].SnapshotSequence);

        await RegistryAssert.ThrowsAsync<BridgeStalePinnedInputAuthorityException>(() =>
            fixture.Registry.SendControllerInputAsync(
                configured.Id, sliderInput, generation,
                CancellationToken.None, CancellationToken.None,
                expectedActionId: "wrong-seek.changed"));
        RegistryAssert.Equal(3, client.ControllerInputs.Count);

        foreach (var staleGeneration in new (ControllerInputEvent Input, string? Generation)[]
        {
            (input, null),
            (input, new string('f', 32)),
        })
        {
            if (staleGeneration.Generation is null)
                await RegistryAssert.ThrowsAsync<BridgeProtocolException>(() =>
                    fixture.Registry.SendControllerInputAsync(
                        configured.Id, staleGeneration.Input, staleGeneration.Generation,
                        CancellationToken.None, CancellationToken.None));
            else
                await RegistryAssert.ThrowsAsync<BridgeStalePinnedInputAuthorityException>(() =>
                    fixture.Registry.SendControllerInputAsync(
                        configured.Id, staleGeneration.Input, staleGeneration.Generation,
                        CancellationToken.None, CancellationToken.None));
        }
        foreach (var staleAuthority in new ControllerInputEvent[]
        {
            input with { SnapshotSequence = disabledSuccessor.Sequence + 1 },
            input with { PinnedLayoutId = "retired" },
            input with { ActiveInputScopeId = "full.root" },
            input with { FocusedElementId = "full.play" },
        })
        {
            await RegistryAssert.ThrowsAsync<BridgeStalePinnedInputAuthorityException>(() =>
                fixture.Registry.SendControllerInputAsync(
                    configured.Id, staleAuthority, generation,
                    CancellationToken.None, CancellationToken.None));
        }
        RegistryAssert.Equal(3, client.ControllerInputs.Count);

        // A button the admitted projection binds nothing to is an ordinary
        // not-handled outcome. It must not be reported as stale authority and
        // must not reach the worker.
        foreach (var unbound in new ControllerInputEvent[]
        {
            // B has no shortcut on the focused node or any ancestor in scope.
            input with { Button = ControllerButton.B },
            // A on a node in scope that carries no action ID.
            input with { FocusedElementId = "compact.label" },
            // A slider D-pad step on a node with no valueChanged action.
            sliderInput with { FocusedElementId = "compact.mute" },
        })
        {
            using var publication = await fixture.Registry.SendControllerInputAsync(
                configured.Id, unbound, generation,
                CancellationToken.None, CancellationToken.None);
            RegistryAssert.False(publication.Value);
        }
        RegistryAssert.Equal(3, client.ControllerInputs.Count);

        // An expected action ID is the host asserting one exact binding, so a
        // projection that binds nothing remains an authority failure.
        await RegistryAssert.ThrowsAsync<BridgeStalePinnedInputAuthorityException>(() =>
            fixture.Registry.SendControllerInputAsync(
                configured.Id, sliderInput with { FocusedElementId = "compact.mute" },
                generation, CancellationToken.None, CancellationToken.None,
                expectedActionId: "compact-seek.changed"));
        RegistryAssert.Equal(3, client.ControllerInputs.Count);
    }

    internal static async Task PinnedShortcutAvailabilityBelongsToDeclaringOwner()
    {
        var configured = Widget("pinned-shortcut-owner", worker: 'h', catalog: 'h');
        var ownerDisabled = false;
        var ownerBusy = false;
        var shortcutActionId = "owner.action";
        var shortcutRepeatPolicy = ControllerActionRepeatPolicy.WhileHeld;
        await using var fixture = new RegistryFixture(
            Catalog(configured),
            configure: (_, client) => client.SnapshotFactory = sequence => new ViewSnapshot
            {
                ProtocolVersion = ProtocolConstants.HeldButtonActionRepeatVersion,
                Sequence = sequence,
                WidgetInstanceId = configured.InstanceId,
                ActiveInputScopeId = "root",
                InitialFocusId = "initial",
                Root = ShortcutRoot(),
                PinnedLayouts =
                [
                    new PinnedPresentationLayout
                    {
                        Id = "compact",
                        Name = "Compact",
                        Surface = new WidgetSurfaceHints
                        {
                            PreferredWidth = 360,
                            PreferredHeight = 240,
                            MinimumWidth = 240,
                            MinimumHeight = 180,
                        },
                        Root = ShortcutRoot(),
                        ActiveInputScopeId = "root",
                        InitialFocusId = "initial",
                    },
                ],
            });
        await fixture.SetLifecycleAsync(configured.Id, WidgetLifecycleState.Interactive);
        var generation = configured.PublicDescriptor().RuntimeGeneration;
        var snapshot = (await fixture.GetSnapshotAsync(configured.Id)).Snapshot;
        var input = new ControllerInputEvent(
            ControllerButton.X,
            ControllerEventPhase.Repeated,
            ControllerInputContext.PinnedSurface,
            FocusedElementId: "focused",
            Sequence: 1,
            ActiveInputScopeId: "root",
            SnapshotSequence: snapshot.Sequence)
        {
            PinnedLayoutId = "compact",
        };

        using (var publication = await fixture.Registry.SendControllerInputAsync(
                   configured.Id, input, generation,
                   CancellationToken.None, CancellationToken.None,
                   expectedActionId: "owner.action"))
            RegistryAssert.True(publication.Value);
        var client = fixture.Clients.Single();
        RegistryAssert.Equal(1, client.ControllerInputs.Count);

        shortcutActionId = "owner.retargeted";
        _ = await fixture.GetSnapshotAsync(configured.Id);
        await RegistryAssert.ThrowsAsync<BridgeStalePinnedInputAuthorityException>(() =>
            fixture.Registry.SendControllerInputAsync(
                configured.Id, input with { Sequence = 2 }, generation,
                CancellationToken.None, CancellationToken.None,
                expectedActionId: "owner.action"));

        shortcutActionId = "owner.action";
        var restored = (await fixture.GetSnapshotAsync(configured.Id)).Snapshot;
        shortcutRepeatPolicy = ControllerActionRepeatPolicy.None;
        _ = await fixture.GetSnapshotAsync(configured.Id);
        await RegistryAssert.ThrowsAsync<BridgeStalePinnedInputAuthorityException>(() =>
            fixture.Registry.SendControllerInputAsync(
                configured.Id, input with
                {
                    SnapshotSequence = restored.Sequence,
                    Sequence = 3,
                }, generation, CancellationToken.None, CancellationToken.None,
                expectedActionId: "owner.action"));
        shortcutRepeatPolicy = ControllerActionRepeatPolicy.WhileHeld;
        _ = await fixture.GetSnapshotAsync(configured.Id);

        ownerDisabled = true;
        var disabled = (await fixture.GetSnapshotAsync(configured.Id)).Snapshot;
        await RegistryAssert.ThrowsAsync<BridgeStalePinnedInputAuthorityException>(() =>
            fixture.Registry.SendControllerInputAsync(
                configured.Id, input with
                {
                    SnapshotSequence = disabled.Sequence,
                    Sequence = 2,
                }, generation, CancellationToken.None, CancellationToken.None,
                expectedActionId: "owner.action"));

        ownerDisabled = false;
        ownerBusy = true;
        var busy = (await fixture.GetSnapshotAsync(configured.Id)).Snapshot;
        await RegistryAssert.ThrowsAsync<BridgeStalePinnedInputAuthorityException>(() =>
            fixture.Registry.SendControllerInputAsync(
                configured.Id, input with
                {
                    SnapshotSequence = busy.Sequence,
                    Sequence = 3,
                }, generation, CancellationToken.None, CancellationToken.None,
                expectedActionId: "owner.action"));
        RegistryAssert.Equal(1, client.ControllerInputs.Count);

        ViewNode ShortcutRoot() => new()
        {
            Id = "root",
            Kind = ViewNodeKind.Stack,
            InputScopeId = "root",
            // A root fallback proves the nearer unavailable declaration blocks
            // fallback instead of lending the same button to another owner.
            Shortcuts =
            [
                new ControllerShortcut(
                    ControllerButton.X, "root.action",
                    ControllerEventPhase.Pressed,
                    ControllerActionRepeatPolicy.WhileHeld),
            ],
            Children =
            [
                new ViewNode
                {
                    Id = "initial",
                    Kind = ViewNodeKind.Button,
                    ActionId = "initial.action",
                },
                new ViewNode
                {
                    Id = "owner",
                    Kind = ViewNodeKind.Stack,
                    IsDisabled = ownerDisabled,
                    IsBusy = ownerBusy,
                    Shortcuts =
                    [
                        new ControllerShortcut(
                            ControllerButton.X, shortcutActionId,
                            ControllerEventPhase.Pressed,
                            shortcutRepeatPolicy),
                    ],
                    Children =
                    [
                        // Focused availability is deliberately unrelated to
                        // the ancestor-owned shortcut.
                        new ViewNode
                        {
                            Id = "focused",
                            Kind = ViewNodeKind.Button,
                            IsDisabled = true,
                        },
                    ],
                },
            ],
        };
    }

    internal static async Task OpenWidgetInputRetainsCompatibleCommittedAuthority()
    {
        var configured = Widget("open-input-refresh", worker: 'o', catalog: 'o');
        var actionId = "open.activate";
        var scopeId = "open.root";
        var includeFocusedNode = true;
        var focusedDisabled = false;
        await using var fixture = new RegistryFixture(
            Catalog(configured),
            configure: (_, client) => client.SnapshotFactory = sequence => new ViewSnapshot
            {
                ProtocolVersion = ProtocolConstants.CurrentVersion,
                Sequence = sequence,
                WidgetInstanceId = configured.InstanceId,
                ActiveInputScopeId = scopeId,
                InitialFocusId = includeFocusedNode ? "open.action" : null,
                Root = new ViewNode
                {
                    Id = scopeId,
                    Kind = ViewNodeKind.Stack,
                    InputScopeId = scopeId,
                    Children = includeFocusedNode
                        ?
                        [
                            new ViewNode
                            {
                                Id = "open.action",
                                Kind = ViewNodeKind.Button,
                                ActionId = actionId,
                                IsDisabled = focusedDisabled,
                            },
                            new ViewNode { Id = "open.raw", Kind = ViewNodeKind.Text },
                        ]
                        : [],
                },
            });
        await fixture.SetLifecycleAsync(configured.Id, WidgetLifecycleState.Interactive);
        var generation = configured.PublicDescriptor().RuntimeGeneration;
        var origin = (await fixture.GetSnapshotAsync(configured.Id)).Snapshot;
        var compatible = (await fixture.GetSnapshotAsync(configured.Id)).Snapshot;
        var input = new ControllerInputEvent(
            ControllerButton.A,
            ControllerEventPhase.Pressed,
            ControllerInputContext.OpenWidget,
            FocusedElementId: "open.action",
            Sequence: 1,
            ActiveInputScopeId: "open.root",
            SnapshotSequence: origin.Sequence);

        using (var publication = await fixture.Registry.SendControllerInputAsync(
                   configured.Id, input, generation,
                   CancellationToken.None, CancellationToken.None,
                   expectedActionId: "open.activate"))
            RegistryAssert.True(publication.Value);
        var client = fixture.Clients.Single();
        RegistryAssert.Equal(1, client.ControllerInputs.Count);
        RegistryAssert.Equal(compatible.Sequence,
            client.ControllerInputs[0].SnapshotSequence);

        var latest = compatible;
        for (var index = 0; index < 17; index++)
            latest = (await fixture.GetSnapshotAsync(configured.Id)).Snapshot;
        await RegistryAssert.ThrowsAsync<BridgeStaleControllerInputAuthorityException>(() =>
            fixture.Registry.SendControllerInputAsync(
                configured.Id, input with
                {
                    SnapshotSequence = compatible.Sequence,
                    Sequence = 2,
                }, generation, CancellationToken.None, CancellationToken.None,
                expectedActionId: "open.activate"));

        // A custom widget override remains eligible without a declarative
        // binding only while its exact snapshot/focus/scope authority is still
        // current. Private handler semantics cannot be compared across views.
        using (var publication = await fixture.Registry.SendControllerInputAsync(
                   configured.Id, input with
                   {
                       Button = ControllerButton.B,
                       FocusedElementId = "open.raw",
                       SnapshotSequence = latest.Sequence,
                       Sequence = 3,
                   }, generation, CancellationToken.None, CancellationToken.None))
            RegistryAssert.True(publication.Value);
        RegistryAssert.Equal(2, client.ControllerInputs.Count);
        RegistryAssert.Equal(latest.Sequence,
            client.ControllerInputs[1].SnapshotSequence);
        var rawSuccessor = (await fixture.GetSnapshotAsync(configured.Id)).Snapshot;
        client.RevalidatedHandled = false;
        using (var publication = await fixture.Registry.SendControllerInputAsync(
                   configured.Id, input with
                   {
                       Button = ControllerButton.B,
                       FocusedElementId = "open.raw",
                       SnapshotSequence = latest.Sequence,
                       Sequence = 4,
                   }, generation, CancellationToken.None, CancellationToken.None))
            RegistryAssert.True(!publication.Value);
        RegistryAssert.Equal(3, client.ControllerInputs.Count);
        RegistryAssert.Equal(rawSuccessor.Sequence, client.ControllerInputs[2].SnapshotSequence);
        RegistryAssert.Equal(4L, client.ControllerInputs[2].Sequence);

        // A worker with private raw semantics refuses before invocation. The
        // bridge must neither retry it nor downgrade it to ordinary unhandled.
        client.RevalidatedHandled = null;
        await RegistryAssert.ThrowsAsync<BridgeStaleControllerInputAuthorityException>(() =>
            fixture.Registry.SendControllerInputAsync(configured.Id, input with
            {
                Button = ControllerButton.B,
                FocusedElementId = "open.raw",
                SnapshotSequence = latest.Sequence,
                Sequence = 9,
            }, generation, CancellationToken.None, CancellationToken.None));
        RegistryAssert.Equal(3, client.ControllerInputs.Count);
        RegistryAssert.Equal(3, client.RevalidatedAttempts);
        client.RevalidatedHandled = true;

        actionId = "open.retargeted";
        _ = await fixture.GetSnapshotAsync(configured.Id);
        await RegistryAssert.ThrowsAsync<BridgeStaleControllerInputAuthorityException>(() =>
            fixture.Registry.SendControllerInputAsync(
                configured.Id, input with
                {
                    SnapshotSequence = rawSuccessor.Sequence,
                    Sequence = 5,
                }, generation, CancellationToken.None, CancellationToken.None,
                expectedActionId: "open.activate"));

        actionId = "open.activate";
        var restored = (await fixture.GetSnapshotAsync(configured.Id)).Snapshot;
        focusedDisabled = true;
        var disabled = (await fixture.GetSnapshotAsync(configured.Id)).Snapshot;
        await RegistryAssert.ThrowsAsync<BridgeStaleControllerInputAuthorityException>(() =>
            fixture.Registry.SendControllerInputAsync(
                configured.Id, input with
                {
                    SnapshotSequence = restored.Sequence,
                    Sequence = 6,
                }, generation, CancellationToken.None, CancellationToken.None,
                expectedActionId: "open.activate"));

        focusedDisabled = false;
        var enabled = (await fixture.GetSnapshotAsync(configured.Id)).Snapshot;
        includeFocusedNode = false;
        _ = await fixture.GetSnapshotAsync(configured.Id);
        await RegistryAssert.ThrowsAsync<BridgeStaleControllerInputAuthorityException>(() =>
            fixture.Registry.SendControllerInputAsync(
                configured.Id, input with
                {
                    SnapshotSequence = enabled.Sequence,
                    Sequence = 7,
                }, generation, CancellationToken.None, CancellationToken.None,
                expectedActionId: "open.activate"));

        includeFocusedNode = true;
        var oldScope = (await fixture.GetSnapshotAsync(configured.Id)).Snapshot;
        scopeId = "open.replaced-root";
        _ = await fixture.GetSnapshotAsync(configured.Id);
        await RegistryAssert.ThrowsAsync<BridgeStaleControllerInputAuthorityException>(() =>
            fixture.Registry.SendControllerInputAsync(
                configured.Id, input with
                {
                    SnapshotSequence = oldScope.Sequence,
                    Sequence = 8,
                }, generation, CancellationToken.None, CancellationToken.None));
        RegistryAssert.Equal(3, client.ControllerInputs.Count);
    }

    internal static async Task InputAdmissionSerializesWithSnapshotPublication()
    {
        var configured = Widget("input-race", worker: 'r', catalog: 'r');
        await using var fixture = new RegistryFixture(Catalog(configured), configure: (_, client) =>
            client.SnapshotFactory = sequence => new ViewSnapshot
            {
                ProtocolVersion = ProtocolConstants.CurrentVersion, Sequence = sequence,
                WidgetInstanceId = configured.InstanceId, ActiveInputScopeId = "root",
                Root = new ViewNode { Id = "root", Kind = ViewNodeKind.Stack,
                    Children = [new ViewNode { Id = "focus", Kind = ViewNodeKind.Button, ActionId = "activate" }] },
            });
        await fixture.SetLifecycleAsync(configured.Id, WidgetLifecycleState.Interactive);
        var origin = (await fixture.GetSnapshotAsync(configured.Id)).Snapshot;
        var client = fixture.Clients.Single();
        client.RevalidatedHandled = false;
        client.BlockSnapshots = true;
        var publishing = fixture.GetSnapshotAsync(configured.Id);
        await client.SnapshotEntered.WaitAsync(TimeSpan.FromSeconds(2));
        var input = new ControllerInputEvent(ControllerButton.B, ControllerEventPhase.Pressed,
            ControllerInputContext.OpenWidget, "focus", Sequence: 401,
            ActiveInputScopeId: "root", SnapshotSequence: origin.Sequence);
        var pending = fixture.Registry.SendControllerInputAsync(configured.Id, input,
            configured.PublicDescriptor().RuntimeGeneration, CancellationToken.None, CancellationToken.None);
        RegistryAssert.True(!pending.IsCompleted);
        RegistryAssert.Equal(0, client.ControllerInputs.Count);
        client.ReleaseSnapshot();
        var latest = (await publishing).Snapshot;
        using (var result = await pending) RegistryAssert.True(!result.Value);
        RegistryAssert.Equal(1, client.ControllerInputs.Count);
        RegistryAssert.Equal(401L, client.ControllerInputs[0].Sequence);
        RegistryAssert.Equal(latest.Sequence, client.ControllerInputs[0].SnapshotSequence);
        RegistryAssert.Equal(1, client.RevalidatedAttempts);
        client.BlockSnapshots = false;
        // Continuous visual updates must not turn into a retry loop or make
        // valid unbound buttons disappear. Each event is delivered once.
        foreach (var button in new[] { ControllerButton.B, ControllerButton.X, ControllerButton.Y, ControllerButton.Menu })
        {
            for (var index = 0; index < 25; index++)
            {
                origin = (await fixture.GetSnapshotAsync(configured.Id)).Snapshot;
                latest = (await fixture.GetSnapshotAsync(configured.Id)).Snapshot;
                input = input with { Button = button, Sequence = input.Sequence + 1,
                    SnapshotSequence = origin.Sequence };
                using var result = await fixture.Registry.SendControllerInputAsync(configured.Id, input,
                    configured.PublicDescriptor().RuntimeGeneration, CancellationToken.None, CancellationToken.None);
                RegistryAssert.True(!result.Value);
                RegistryAssert.Equal(latest.Sequence, client.ControllerInputs[^1].SnapshotSequence);
            }
        }
        RegistryAssert.Equal(101, client.ControllerInputs.Count);
        RegistryAssert.Equal(101, client.ControllerInputs.Select(value => value.Sequence).Distinct().Count());
        RegistryAssert.Equal(101, client.RevalidatedAttempts);
        var failure = WidgetBridgeServer.CreateRequestFailure(
            new BridgeStaleControllerInputAuthorityException("stale"));
        RegistryAssert.Equal("stale_controller_input_authority", failure.Code);
    }

    internal static async Task EmbeddedMediaRequiresExactPublicationAuthority()
    {
        var configured = Widget("embedded-media-authority", worker: 'm', catalog: 'm');
        var includePendingCommand = true;
        long pendingCommandSequence = 3;
        var entryAsset = "media/index.html";
        await using var fixture = new RegistryFixture(
            Catalog(configured),
            configure: (_, client) => client.SnapshotFactory = sequence => new ViewSnapshot
            {
                ProtocolVersion = ProtocolConstants.EmbeddedMediaPlaybackPreferencesVersion,
                Sequence = sequence,
                WidgetInstanceId = configured.InstanceId,
                ActiveInputScopeId = "root",
                Root = new ViewNode { Id = "root", Kind = ViewNodeKind.Stack },
                EmbeddedMediaSession = new EmbeddedMediaSession
                {
                    Id = "primary-media",
                    AccessibleName = "Neutral media",
                    EntryAsset = entryAsset,
                    Surface = new WidgetSurfaceHints
                    {
                        PreferredWidth = 760,
                        PreferredHeight = 425,
                        MinimumWidth = 320,
                        MinimumHeight = 180,
                    },
                    AspectRatio = 16.0 / 9.0,
                    PendingCommand = includePendingCommand &&
                        (sequence & uint.MaxValue) >= 3
                        ? new EmbeddedMediaPlaybackCommand
                        {
                            Sequence = pendingCommandSequence,
                            Kind = EmbeddedMediaPlaybackCommandKind.SetMuted,
                            MediaKey = "aurora-track",
                            Muted = true,
                        }
                        : null,
                    Resources =
                    [
                        new EmbeddedMediaResource
                        {
                            Path = entryAsset,
                            ContentType = "text/html",
                        },
                    ],
                },
            });
        await fixture.SetLifecycleAsync(configured.Id, WidgetLifecycleState.Visible);
        var snapshot = await fixture.GetSnapshotAsync(configured.Id);
        var initialSnapshotSequence = snapshot.Snapshot.Sequence;
        var descriptor = configured.PublicDescriptor();
        var initialExact = new BridgeEmbeddedMediaRequest(
            configured.Id,
            snapshot.Snapshot.WidgetInstanceId,
            descriptor.RuntimeGeneration,
            descriptor.PresentationGeneration,
            snapshot.Snapshot.Sequence,
            snapshot.Snapshot.EmbeddedMediaSession!.Id);

        using (var admitted = fixture.Registry.AdmitEmbeddedMedia(initialExact))
        {
            RegistryAssert.Equal(configured.Id, admitted.Value.Configured.Id);
            RegistryAssert.Equal(snapshot.Snapshot.Sequence, admitted.Value.Snapshot.Sequence);
        }

        var initialObservation = new EmbeddedMediaPlaybackEvent
        {
            SessionId = initialExact.SessionId,
            Sequence = 1,
            CommandSequence = 0,
            MediaKey = "aurora-track",
            State = EmbeddedMediaPlaybackState.Ready,
            PositionSeconds = 0,
            DurationSeconds = 60,
            Volume = 0.8,
            PlaybackRate = 1,
            Muted = false,
            Loop = false,
        };
        await fixture.Registry.PublishEmbeddedMediaPlaybackEventAsync(
            new BridgeEmbeddedMediaPlaybackEventRequest(
                initialExact.WidgetId,
                initialExact.InstanceId,
                initialExact.RuntimeGeneration,
                initialExact.PresentationGeneration,
                initialExact.Sequence,
                initialObservation),
            CancellationToken.None);

        var stateSnapshot = await fixture.GetSnapshotAsync(configured.Id);
        var commandSnapshot = await fixture.GetSnapshotAsync(configured.Id);
        RegistryAssert.True(stateSnapshot.Snapshot.Sequence > initialSnapshotSequence);
        RegistryAssert.True(
            commandSnapshot.Snapshot.Sequence > stateSnapshot.Snapshot.Sequence);
        RegistryAssert.Equal(
            stateSnapshot.Snapshot.Sequence - initialSnapshotSequence,
            commandSnapshot.Snapshot.Sequence - stateSnapshot.Snapshot.Sequence);
        var exact = initialExact with { Sequence = commandSnapshot.Snapshot.Sequence };

        var playbackEvent = new EmbeddedMediaPlaybackEvent
        {
            SessionId = exact.SessionId,
            Sequence = 7,
            CommandSequence = 3,
            MediaKey = "aurora-track",
            State = EmbeddedMediaPlaybackState.Paused,
            PositionSeconds = 12,
            DurationSeconds = 60,
            Volume = 0.8,
            PlaybackRate = 1,
            Muted = true,
            Loop = false,
        };
        var eventRequest = new BridgeEmbeddedMediaPlaybackEventRequest(
            exact.WidgetId,
            exact.InstanceId,
            exact.RuntimeGeneration,
            exact.PresentationGeneration,
            exact.Sequence,
            playbackEvent);
        await RegistryAssert.ThrowsAsync<BridgeProtocolException>(() =>
            fixture.Registry.PublishEmbeddedMediaPlaybackEventAsync(
                eventRequest with
                {
                    Event = playbackEvent with { MediaKey = "cedar-track" },
                },
                CancellationToken.None));
        await RegistryAssert.ThrowsAsync<BridgeProtocolException>(() =>
            fixture.Registry.PublishEmbeddedMediaPlaybackEventAsync(
                eventRequest with
                {
                    Event = playbackEvent with { CommandSequence = 4 },
                },
                CancellationToken.None));
        await RegistryAssert.ThrowsAsync<BridgeProtocolException>(() =>
            fixture.Registry.PublishEmbeddedMediaPlaybackEventAsync(
                eventRequest with
                {
                    Event = playbackEvent with { Muted = false },
                },
                CancellationToken.None));
        var compatibleSuccessor = await fixture.GetSnapshotAsync(configured.Id);
        RegistryAssert.True(
            compatibleSuccessor.Snapshot.Sequence > commandSnapshot.Snapshot.Sequence);
        RegistryAssert.Equal(
            playbackEvent.CommandSequence,
            compatibleSuccessor.Snapshot.EmbeddedMediaSession!.PendingCommand!.Sequence);
        await fixture.Registry.PublishEmbeddedMediaPlaybackEventAsync(
            eventRequest, CancellationToken.None);
        var client = fixture.Clients.Single();
        RegistryAssert.Equal(2, client.EmbeddedMediaPlaybackEvents.Count);
        RegistryAssert.Equal(initialObservation, client.EmbeddedMediaPlaybackEvents[0]);
        RegistryAssert.Equal(playbackEvent, client.EmbeddedMediaPlaybackEvents[1]);
        await RegistryAssert.ThrowsAsync<BridgeProtocolException>(() =>
            fixture.Registry.PublishEmbeddedMediaPlaybackEventAsync(
                eventRequest, CancellationToken.None));

        includePendingCommand = false;
        var retired = await fixture.GetSnapshotAsync(configured.Id);
        RegistryAssert.Equal<EmbeddedMediaPlaybackCommand?>(
            null, retired.Snapshot.EmbeddedMediaSession!.PendingCommand);
        await RegistryAssert.ThrowsAsync<BridgeProtocolException>(() =>
            fixture.Registry.PublishEmbeddedMediaPlaybackEventAsync(
                eventRequest, CancellationToken.None));

        includePendingCommand = true;
        pendingCommandSequence = 4;
        var nextCommand = await fixture.GetSnapshotAsync(configured.Id);
        var nextOriginSequence = nextCommand.Snapshot.Sequence;
        var nextPlaybackEvent = playbackEvent with
        {
            Sequence = 8,
            CommandSequence = pendingCommandSequence,
        };
        var nextRequest = eventRequest with
        {
            Sequence = nextOriginSequence,
            Event = nextPlaybackEvent,
        };
        var nextCompatible = await fixture.GetSnapshotAsync(configured.Id);
        RegistryAssert.True(nextCompatible.Snapshot.Sequence > nextOriginSequence);
        RegistryAssert.Equal(
            pendingCommandSequence,
            nextCompatible.Snapshot.EmbeddedMediaSession!.PendingCommand!.Sequence);

        entryAsset = "media/replacement.html";
        var wrongResource = await fixture.GetSnapshotAsync(configured.Id);
        await RegistryAssert.ThrowsAsync<BridgeProtocolException>(() =>
            fixture.Registry.PublishEmbeddedMediaPlaybackEventAsync(
                nextRequest with { Sequence = wrongResource.Snapshot.Sequence },
                CancellationToken.None));
        entryAsset = "media/index.html";
        var exactCurrent = await fixture.GetSnapshotAsync(configured.Id);
        await RegistryAssert.ThrowsAsync<BridgeProtocolException>(() =>
            fixture.Registry.PublishEmbeddedMediaPlaybackEventAsync(
                nextRequest with { InstanceId = "wrong-instance" },
                CancellationToken.None));
        await RegistryAssert.ThrowsAsync<BridgeProtocolException>(() =>
            fixture.Registry.PublishEmbeddedMediaPlaybackEventAsync(
                nextRequest with
                {
                    Sequence = exactCurrent.Snapshot.Sequence,
                    Event = nextPlaybackEvent with { MediaKey = "cedar-track" },
                },
                CancellationToken.None));
        await fixture.Registry.PublishEmbeddedMediaPlaybackEventAsync(
            nextRequest with { Sequence = exactCurrent.Snapshot.Sequence },
            CancellationToken.None);
        RegistryAssert.Equal(3, client.EmbeddedMediaPlaybackEvents.Count);
        RegistryAssert.Equal(nextPlaybackEvent, client.EmbeddedMediaPlaybackEvents[2]);
        await RegistryAssert.ThrowsAsync<BridgeProtocolException>(() =>
            fixture.Registry.PublishEmbeddedMediaPlaybackEventAsync(
                nextRequest with { Sequence = exactCurrent.Snapshot.Sequence },
                CancellationToken.None));

        await RegistryAssert.ThrowsAsync<BridgeProtocolException>(() => Task.Run(() =>
        {
            using var _ = fixture.Registry.AdmitEmbeddedMedia(
                exact with { Sequence = exact.Sequence + 1 });
        }));
        await RegistryAssert.ThrowsAsync<BridgeProtocolException>(() => Task.Run(() =>
        {
            using var _ = fixture.Registry.AdmitEmbeddedMedia(
                exact with { RuntimeGeneration = new string('f', 32) });
        }));
        await RegistryAssert.ThrowsAsync<BridgeProtocolException>(() =>
            fixture.Registry.PublishEmbeddedMediaPlaybackEventAsync(
                eventRequest with { Sequence = eventRequest.Sequence + 1 },
                CancellationToken.None));
        await RegistryAssert.ThrowsAsync<BridgeProtocolException>(() =>
            fixture.Registry.PublishEmbeddedMediaPlaybackEventAsync(
                eventRequest with
                {
                    Event = playbackEvent with { MediaKey = "invalid key" },
                },
                CancellationToken.None));
        RegistryAssert.Equal(3, client.EmbeddedMediaPlaybackEvents.Count);
    }

    internal static async Task CatalogReplacementAndRemovalOwnGenerations()
    {
        var initial = Widget("alpha", worker: 'a', catalog: 'a');
        await using var fixture = new RegistryFixture(Catalog(initial));

        await fixture.SetLifecycleAsync(initial.Id, WidgetLifecycleState.Visible);
        var first = fixture.Clients.Single();
        first.RaiseInvalidated(7);
        first.RaiseActionFailed("old-action");
        first.RaiseFailure();
        await fixture.Registry.DrainNotificationsAsync(initial.Id);
        RegistryAssert.Equal(1, fixture.Invalidations.Count);
        RegistryAssert.Equal(1, fixture.ActionFailures.Count);
        RegistryAssert.Equal(1, fixture.Failures.Count);
        RegistryAssert.Equal(1, fixture.Registry.ResidencyBudget.ApplicationWorkers);

        var presentationOnly = initial with
        {
            Name = "Renamed alpha",
            CatalogFingerprint = Fingerprint('b'),
        };
        RegistryAssert.True(fixture.Registry.ApplyCatalog(Catalog(presentationOnly), revision: 1));
        RegistryAssert.Equal(1, fixture.Clients.Count);
        var compatible = fixture.Registry.DiagnosticsSnapshot();
        RegistryAssert.Equal("Renamed alpha", compatible.Workers.Single().Name);
        RegistryAssert.Equal(1L, compatible.CatalogRevision);

        var replacement = presentationOnly with
        {
            WorkerFingerprint = Fingerprint('c'),
            CatalogFingerprint = Fingerprint('c'),
        };
        RegistryAssert.True(fixture.Registry.ApplyCatalog(Catalog(replacement), revision: 2));
        await first.Disposed.WaitAsync(TimeSpan.FromSeconds(2));
        RegistryAssert.Equal(1, first.DisposeCount);
        RegistryAssert.Equal(0, fixture.Registry.ResidencyBudget.ApplicationWorkers);

        first.RaiseInvalidated(8);
        first.RaiseActionFailed("stale-action");
        first.RaiseFailure();
        RegistryAssert.Equal(1, fixture.Invalidations.Count);
        RegistryAssert.Equal(1, fixture.ActionFailures.Count);
        RegistryAssert.Equal(1, fixture.Failures.Count);

        await fixture.SetLifecycleAsync(replacement.Id, WidgetLifecycleState.Interactive);
        var second = fixture.Clients[1];
        second.RaiseInvalidated(9);
        await fixture.Registry.DrainNotificationsAsync(replacement.Id);
        RegistryAssert.Equal(2, fixture.Invalidations.Count);
        RegistryAssert.Equal(Fingerprint('c')[..32].ToLowerInvariant(),
            fixture.Registry.CatalogSnapshot().Catalog.Widgets.Single().RuntimeGeneration);

        RegistryAssert.True(fixture.Registry.ApplyCatalog(Catalog(), revision: 3));
        await second.Disposed.WaitAsync(TimeSpan.FromSeconds(2));
        RegistryAssert.Equal(1, second.DisposeCount);
        RegistryAssert.Equal(0, fixture.Registry.RunningWorkerCount);
        RegistryAssert.Equal(0, fixture.Registry.ResidencyBudget.ApplicationWorkers);
        second.RaiseInvalidated(10);
        RegistryAssert.Equal(2, fixture.Invalidations.Count);
    }

    internal static async Task IdleUnloadCancellationAndReplacementAreOwned()
    {
        var delay = new ManualRegistryDelay();
        var initial = Widget(
            "idle",
            worker: 'd',
            catalog: 'd',
            residency: new WidgetResidencyPolicy
            {
                Mode = WidgetResidencyPolicies.UnloadAfterIdle,
                IdleSeconds = WidgetResidencyPolicies.MinimumIdleSeconds,
            });
        await using var fixture = new RegistryFixture(Catalog(initial), delay: delay.InvokeAsync);

        await fixture.SetLifecycleAsync(initial.Id, WidgetLifecycleState.Visible);
        _ = await fixture.GetSnapshotAsync(initial.Id);
        await fixture.SetLifecycleAsync(initial.Id, WidgetLifecycleState.Background);
        var cancelledByVisibility = await delay.NextAsync();

        await fixture.SetLifecycleAsync(initial.Id, WidgetLifecycleState.Visible);
        await cancelledByVisibility.CancellationObserved.Task.WaitAsync(TimeSpan.FromSeconds(2));
        cancelledByVisibility.Release();

        await fixture.SetLifecycleAsync(initial.Id, WidgetLifecycleState.Background);
        var cancelledByReplacement = await delay.NextAsync();
        var replacement = initial with
        {
            WorkerFingerprint = Fingerprint('e'),
            CatalogFingerprint = Fingerprint('e'),
        };
        RegistryAssert.True(fixture.Registry.ApplyCatalog(Catalog(replacement), revision: 1));
        await cancelledByReplacement.CancellationObserved.Task.WaitAsync(TimeSpan.FromSeconds(2));
        RegistryAssert.True(!fixture.Clients[0].Disposed.IsCompleted,
            "Retirement completed before its tracked idle-unload task drained.");

        cancelledByReplacement.Release();
        await fixture.Clients[0].Disposed.WaitAsync(TimeSpan.FromSeconds(2));
        RegistryAssert.Equal(0, fixture.Clients[0].UnloadCount);
        RegistryAssert.Equal(1, fixture.Clients[0].DisposeCount);
        RegistryAssert.Equal(0, fixture.Registry.ResidencyBudget.ApplicationWorkers);
    }

    internal static async Task FreshGenericWorkersRequireTypedRecoveryFromRetainedBases()
    {
        var delay = new ManualRegistryDelay();
        var gamesApps = Widget(
            "games-apps-restart",
            worker: 'g',
            catalog: 'g',
            residency: new WidgetResidencyPolicy
            {
                Mode = WidgetResidencyPolicies.UnloadAfterIdle,
                IdleSeconds = WidgetResidencyPolicies.MinimumIdleSeconds,
            });
        var networkControls = Widget(
            "network-controls-restart",
            worker: 'n',
            catalog: 'n',
            residency: new WidgetResidencyPolicy
            {
                Mode = WidgetResidencyPolicies.SuspendWhenHidden,
            });
        await using var fixture = new RegistryFixture(
            Catalog(gamesApps, networkControls),
            configure: (_, client) => client.RebaseRecoverySequence = true,
            delay: delay.InvokeAsync);

        var retained = new Dictionary<string, ViewSnapshot>(StringComparer.Ordinal);
        foreach (var configured in new[] { gamesApps, networkControls })
        {
            await fixture.SetLifecycleAsync(configured.Id, WidgetLifecycleState.Visible);
            retained[configured.Id] = await fixture.GetPresentationAsync(
                configured.Id,
                WidgetPresentationTransactionKind.OrdinaryCheckpoint,
                baseSequence: 0,
                recoveryOriginSequence: 0);
            await fixture.SetLifecycleAsync(configured.Id, WidgetLifecycleState.Background);
        }

        var idleClient = fixture.Clients.Single(client => client.WidgetId == gamesApps.Id);
        var suspendedClient = fixture.Clients.Single(
            client => client.WidgetId == networkControls.Id);
        var idleDelay = await delay.NextAsync();
        idleDelay.Release();
        await idleClient.Unloaded.WaitAsync(TimeSpan.FromSeconds(2));
        suspendedClient.StopForTest();

        foreach (var configured in new[] { gamesApps, networkControls })
        {
            var cached = await fixture.GetPresentationAsync(
                configured.Id,
                WidgetPresentationTransactionKind.IncrementalUpdate,
                retained[configured.Id].Sequence,
                recoveryOriginSequence: 0);
            RegistryAssert.Equal(retained[configured.Id].Sequence, cached.Sequence);
        }

        foreach (var configured in new[] { gamesApps, networkControls })
        {
            var client = fixture.Clients.Single(item => item.WidgetId == configured.Id);
            await fixture.SetLifecycleAsync(configured.Id, WidgetLifecycleState.Visible);
            RegistryAssert.Equal(2, client.Starts);
            _ = await RegistryAssert.ThrowsAsync<BridgeStalePresentationBaseException>(
                () => fixture.GetPresentationAsync(
                    configured.Id,
                    WidgetPresentationTransactionKind.IncrementalUpdate,
                    retained[configured.Id].Sequence,
                    recoveryOriginSequence: 0));
            RegistryAssert.Equal(1, client.PresentationRequests.Count);

            var recovered = await fixture.GetPresentationAsync(
                configured.Id,
                WidgetPresentationTransactionKind.RecoveryCheckpoint,
                baseSequence: 0,
                recoveryOriginSequence: retained[configured.Id].Sequence);
            RegistryAssert.Equal(
                retained[configured.Id].Sequence + 1,
                recovered.Sequence);
            RegistryAssert.Equal(2, client.Starts);
            RegistryAssert.Equal(2, client.PresentationRequests.Count);
            RegistryAssert.Equal(
                WidgetPresentationTransactionKind.RecoveryCheckpoint,
                client.PresentationRequests[^1].TransactionKind);
            RegistryAssert.Equal(retained[configured.Id].Sequence,
                client.PresentationRequests[^1].RecoveryOriginSequence);
        }
    }

    internal static async Task WidgetRuntimeFailuresAreTypedAndRegistrationLocal()
    {
        var alpha = Widget("alpha-runtime-failure", worker: 'a', catalog: 'a');
        var beta = Widget("beta-runtime-neighbor", worker: 'b', catalog: 'b');
        await using var fixture = new RegistryFixture(Catalog(alpha, beta));

        await fixture.SetLifecycleAsync(alpha.Id, WidgetLifecycleState.Visible);
        await fixture.SetLifecycleAsync(beta.Id, WidgetLifecycleState.Visible);
        var alphaClient = fixture.Clients.Single(client => client.WidgetId == alpha.Id);
        var betaClient = fixture.Clients.Single(client => client.WidgetId == beta.Id);

        var failures = new (Exception Failure, string Code)[]
        {
            (new WidgetProcessException("synthetic worker failure"), "worker-runtime-failed"),
            (new WidgetProcessException(
                MessageTypes.Render,
                "worker_request_failed",
                "provider-token=DO_NOT_SURFACE; path=C:\\private\\widget.json"),
                "worker-runtime-failed"),
            (new WidgetProcessAdmissionException("synthetic admission failure"),
                "worker-admission-failed"),
            (new WidgetProtocolViolationException("synthetic protocol failure"),
                "worker-protocol-failed"),
            (new TimeoutException("synthetic request timeout"), "worker-request-timeout"),
        };
        foreach (var (failure, code) in failures)
        {
            alphaClient.SnapshotFailure = failure;
            var typed = await RegistryAssert.ThrowsAsync<BridgeWidgetRequestException>(
                () => fixture.GetSnapshotAsync(alpha.Id));
            RegistryAssert.Equal(alpha.Id, typed.WidgetId);
            RegistryAssert.Equal(code, typed.FailureCode);
            RegistryAssert.True(ReferenceEquals(failure, typed.InnerException));
            if (failure is WidgetProcessException { RequestType: not null } processFailure)
            {
                RegistryAssert.Equal(MessageTypes.Render, typed.RequestType);
                RegistryAssert.Equal("worker_request_failed", typed.WorkerErrorCode);
                RegistryAssert.Equal(
                    "Widget 'alpha-runtime-failure' runtime request failed " +
                    "(worker-runtime-failed).",
                    typed.Message);
                RegistryAssert.True(!typed.Message.Contains(
                    processFailure.WorkerDiagnosticMessage!, StringComparison.Ordinal));
                RegistryAssert.True(!typed.Message.Contains("DO_NOT_SURFACE", StringComparison.Ordinal));

                var response = WidgetBridgeServer.CreateRequestFailure(typed);
                RegistryAssert.Equal("request_failed", response.Code);
                RegistryAssert.Equal(typed.Message, response.Message);
                RegistryAssert.True(!response.Message.Contains(
                    processFailure.WorkerDiagnosticMessage!, StringComparison.Ordinal));
                RegistryAssert.True(!response.Message.Contains(
                    "DO_NOT_SURFACE", StringComparison.Ordinal));

                var diagnostics = new List<BridgeWidgetRequestDiagnostic>();
                WidgetBridgeServer.ReportWidgetRequestFailure(diagnostics.Add, typed);
                RegistryAssert.Equal(1, diagnostics.Count);
                RegistryAssert.Equal(alpha.Id, diagnostics[0].WidgetId);
                RegistryAssert.Equal(MessageTypes.Render, diagnostics[0].RequestType);
                RegistryAssert.Equal("worker_request_failed", diagnostics[0].WorkerErrorCode);
            }
            alphaClient.SnapshotFailure = null;

            _ = await fixture.GetSnapshotAsync(beta.Id);
            RegistryAssert.True(alphaClient.IsRunning,
                "A typed widget failure retired its registration.");
            RegistryAssert.True(betaClient.IsRunning,
                "A neighbor registration ended after another widget failed.");
            RegistryAssert.Equal(2, fixture.Registry.RunningWorkerCount);
        }
    }

    internal static async Task RestartRestoresLifecycleAndResetsGeneration()
    {
        var configured = Widget("restart", worker: 'f', catalog: 'f');
        await using var fixture = new RegistryFixture(Catalog(configured));
        await fixture.SetLifecycleAsync(configured.Id, WidgetLifecycleState.Visible);
        var old = fixture.Clients.Single();
        var oldSnapshot = await fixture.GetSnapshotAsync(configured.Id);

        var restored = await fixture.RestartAsync(configured.Id);
        RegistryAssert.Equal(WidgetLifecycleState.Visible, restored);
        await old.Disposed.WaitAsync(TimeSpan.FromSeconds(2));
        RegistryAssert.Equal(1, old.DisposeCount);
        RegistryAssert.Equal(2, fixture.Clients.Count);
        var current = fixture.Clients[1];
        RegistryAssert.SequenceEqual([WidgetLifecycleState.Visible], current.LifecycleStates);
        RegistryAssert.Equal(1, fixture.Registry.RunningWorkerCount);
        RegistryAssert.Equal(1, fixture.Registry.ResidencyBudget.ApplicationWorkers);

        var currentSnapshot = await fixture.GetSnapshotAsync(configured.Id);
        RegistryAssert.True(oldSnapshot.Snapshot.Sequence != currentSnapshot.Snapshot.Sequence,
            "Restart reused the retired generation's cached snapshot.");
        old.RaiseInvalidated(90);
        RegistryAssert.Equal(0, fixture.Invalidations.Count);
    }

    internal static async Task ManagedReplacementRetiresBeforeMutation()
    {
        var selected = Widget("selected", worker: 'a', catalog: 'a');
        var neighbor = Widget("neighbor", worker: 'b', catalog: 'b');
        await using var fixture = new RegistryFixture(Catalog(selected, neighbor));
        await fixture.SetLifecycleAsync(selected.Id, WidgetLifecycleState.Visible);
        await fixture.SetLifecycleAsync(neighbor.Id, WidgetLifecycleState.Visible);
        var oldSelected = fixture.Clients[0];
        var neighborClient = fixture.Clients[1];
        var operationEntered = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var operationRelease = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        using var callerCancellation = new CancellationTokenSource();
        var replace = fixture.Registry.ReplaceAsync(
            selected.Id,
            async (configured, cancellationToken) =>
            {
                RegistryAssert.Equal(selected.Id, configured.Id);
                RegistryAssert.True(oldSelected.Disposed.IsCompleted,
                    "Host mutation began before the old generation retired.");
                operationEntered.TrySetResult();
                await operationRelease.Task.WaitAsync(cancellationToken);
                return "mutated";
            },
            callerCancellation.Token);
        await operationEntered.Task.WaitAsync(TimeSpan.FromSeconds(2));
        callerCancellation.Cancel();
        operationRelease.TrySetResult();
        var replacement = await replace.WaitAsync(TimeSpan.FromSeconds(2));
        using (replacement.Publication)
        {
            RegistryAssert.Equal("mutated", replacement.Result);
            RegistryAssert.Equal(WidgetLifecycleState.Visible, replacement.Publication.Value);
        }
        RegistryAssert.Equal(3, fixture.Clients.Count);
        RegistryAssert.Equal(1, oldSelected.DisposeCount);
        RegistryAssert.Equal(0, neighborClient.DisposeCount);
        RegistryAssert.SequenceEqual(
            [WidgetLifecycleState.Visible], fixture.Clients[2].LifecycleStates);
    }

    internal static async Task LocalDataManagementIsExactAndDocumentBlind()
    {
        var selected = Widget("local-selected", worker: 'c', catalog: 'c');
        var neighbor = Widget("local-neighbor", worker: 'd', catalog: 'd');
        await using var fixture = new RegistryFixture(Catalog(selected, neighbor));
        await fixture.SetLifecycleAsync(selected.Id, WidgetLifecycleState.Visible);
        await fixture.SetLifecycleAsync(neighbor.Id, WidgetLifecycleState.Visible);
        var backend = new RegistryPrivateStateBackend(selected, neighbor);
        var service = new BridgeWidgetLocalDataService(
            fixture.Registry, backend, appLibrary: backend);

        var inspection = await service.InspectAsync(selected.Id, CancellationToken.None);
        RegistryAssert.True(inspection.Exists && inspection.ConfirmationToken is not null,
            "Exact selected state was not projected as a document-blind token.");
        var result = await service.ClearAsync(
            selected.Id, inspection.ConfirmationToken!, CancellationToken.None);
        RegistryAssert.Equal(PlatformWidgetLocalDataClearStatus.Cleared, result.Status);
        RegistryAssert.True(!backend.Exists(selected.PackageId),
            "Selected state survived a successful exact clear.");
        RegistryAssert.True(!backend.RegistrationExists(selected.PackageId),
            "Selected portable registrations survived a successful exact clear.");
        RegistryAssert.True(backend.Exists(neighbor.PackageId),
            "Neighbor state changed during selected clear.");
        RegistryAssert.True(backend.RegistrationExists(neighbor.PackageId),
            "Neighbor portable registrations changed during selected clear.");
        RegistryAssert.Equal(1, fixture.Clients[0].DisposeCount);
        RegistryAssert.Equal(0, fixture.Clients[1].DisposeCount);
        RegistryAssert.Equal(3, fixture.Clients.Count);

        var staleInspection = await service.InspectAsync(neighbor.Id, CancellationToken.None);
        backend.Advance(neighbor.PackageId);
        var stale = await service.ClearAsync(
            neighbor.Id, staleInspection.ConfirmationToken!, CancellationToken.None);
        RegistryAssert.Equal(PlatformWidgetLocalDataClearStatus.Stale, stale.Status);
        RegistryAssert.True(backend.Exists(neighbor.PackageId),
            "A stale confirmation cleared current neighbor state.");
    }

    internal static async Task CatalogRetirementCancelsRegistrationLease()
    {
        var configured = Widget("registration-lease", worker: 'r', catalog: 'r') with
        {
            DeclaredCapabilities =
            [
                PlatformCapabilities.AppRunningReadV1,
                PlatformCapabilities.AppRunningRegisterV1,
            ],
        };
        var root = Path.Combine(
            Path.GetTempPath(), "WidgetRail-W193-Bridge-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var identity = new BrokerWidgetIdentity(
                configured.PackageId, configured.PublisherId, configured.InstanceId);
            var consent = new ConsentStore(root);
            await consent.SetDecisionAsync(
                identity, PlatformCapabilities.AppRunningReadV1, ConsentDecision.Grant);
            await consent.SetDecisionAsync(
                identity, PlatformCapabilities.AppRunningRegisterV1, ConsentDecision.Grant);
            var registration = new BlockingRegistrationBackend();
            var simulator = new SimulatedPlatformBrokerBackend();
            await using var composite = new CompositePlatformBrokerBackend(
                simulator, simulator, appLibrary: registration);
            BrokerWidgetProcessCompanion? companion = null;
            await using var fixture = new RegistryFixture(
                Catalog(configured),
                configure: (_, client) =>
                {
                    companion = new BrokerWidgetProcessCompanion(
                        configured.PackageId,
                        configured.PublisherId,
                        configured.InstanceId,
                        configured.DeclaredCapabilities,
                        consent,
                        composite,
                        new WidgetProcessCompanionContext(
                            WidgetWorkerIsolationPolicy.HostTrustedJobOnly, null, null));
                    client.OnDisposeAsync = companion.DisposeAsync;
                });
            await fixture.SetLifecycleAsync(
                configured.Id, WidgetLifecycleState.Interactive);
            await companion!.SetLifecycleStateAsync(WidgetLifecycleState.Interactive);
            var arguments = companion.WorkerArguments.ToArray();
            string Argument(string name)
            {
                var index = Array.IndexOf(arguments, name);
                RegistryAssert.True(index >= 0 && index + 1 < arguments.Length,
                    $"Broker companion omitted {name}.");
                return arguments[index + 1];
            }

            using var serverCancellation = new CancellationTokenSource();
            var server = companion.RunAsync(serverCancellation.Token);
            await using var brokerClient = new BrokerPipeClient(
                Argument("--broker-pipe"), identity, Argument("--broker-nonce"));
            await brokerClient.ConnectAsync();
            var observed = await brokerClient.RequestAsync(
                PlatformCapabilities.AppRunningReadV1,
                PlatformCapabilities.AppRunningList,
                new { });
            RegistryAssert.True(observed.Succeeded,
                "Running observation did not reach the real broker request owner.");
            var item = observed.Payload!.Value.GetProperty("items")[0];
            var request = brokerClient.RequestAsync(
                PlatformCapabilities.AppRunningRegisterV1,
                PlatformCapabilities.AppRunningRegister,
                new
                {
                    savedId = item.GetProperty("savedId").GetString(),
                    revision = observed.Payload.Value.GetProperty("revision").GetString(),
                });
            await registration.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));
            RegistryAssert.True(
                fixture.Registry.ApplyCatalog(Catalog(), revision: 1),
                "Catalog removal did not retire the registration owner.");
            await fixture.Clients.Single().Disposed.WaitAsync(TimeSpan.FromSeconds(2));
            await registration.Cancelled.Task.WaitAsync(TimeSpan.FromSeconds(2));
            try { _ = await request.WaitAsync(TimeSpan.FromSeconds(2)); }
            catch (Exception exception) when (exception is BrokerException or
                OperationCanceledException or EndOfStreamException or IOException) { }
            RegistryAssert.Equal(0, registration.Committed);
            serverCancellation.Cancel();
            try { await server.WaitAsync(TimeSpan.FromSeconds(2)); }
            catch (Exception exception) when (exception is OperationCanceledException or
                EndOfStreamException or IOException or ObjectDisposedException) { }
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); }
            catch (Exception exception) when (exception is IOException or
                UnauthorizedAccessException) { }
        }
    }

    internal static async Task RestartReservationAndRestoreFailureAreClosed()
    {
        var configured = Widget("restart-race", worker: '6', catalog: '6');
        await using (var fixture = new RegistryFixture(
            Catalog(configured),
            configure: (_, client) =>
            {
                if (client.ClientGeneration == 1) client.BlockDispose = true;
            }))
        {
            await fixture.SetLifecycleAsync(configured.Id, WidgetLifecycleState.Visible);
            var first = fixture.Clients.Single();
            var restart = fixture.Registry.RestartAsync(configured.Id, CancellationToken.None);
            await first.DisposeEntered.WaitAsync(TimeSpan.FromSeconds(2));
            var concurrentSnapshot = fixture.Registry.GetSnapshotAsync(
                configured.Id, CancellationToken.None, CancellationToken.None);
            RegistryAssert.Equal(1, fixture.Clients.Count);
            RegistryAssert.True(!restart.IsCompleted && !concurrentSnapshot.IsCompleted,
                "A concurrent operation created a competing generation during restart retirement.");

            first.ReleaseDispose();
            using var restartPublication = await restart.WaitAsync(TimeSpan.FromSeconds(2));
            using var snapshotPublication = await concurrentSnapshot.WaitAsync(TimeSpan.FromSeconds(2));
            RegistryAssert.Equal(2, fixture.Clients.Count);
            RegistryAssert.Equal(WidgetLifecycleState.Visible, restartPublication.Value);
            RegistryAssert.Equal(2L, snapshotPublication.Value.Snapshot.Sequence >> 32);
            RegistryAssert.Equal(1, first.DisposeCount);
        }

        await using var failedRestore = new RegistryFixture(
            Catalog(configured),
            configure: (_, client) =>
            {
                if (client.ClientGeneration == 2) client.FailLifecycleTransitions = 1;
            });
        await failedRestore.SetLifecycleAsync(configured.Id, WidgetLifecycleState.Interactive);
        await RegistryAssert.ThrowsAsync<InvalidOperationException>(() =>
            failedRestore.RestartAsync(configured.Id));
        RegistryAssert.Equal(2, failedRestore.Clients.Count);
        RegistryAssert.Equal(1, failedRestore.Clients[0].DisposeCount);
        RegistryAssert.Equal(1, failedRestore.Clients[1].DisposeCount);
        RegistryAssert.Equal(0, failedRestore.Registry.RunningWorkerCount);
        RegistryAssert.Equal(0, failedRestore.Registry.ResidencyBudget.ApplicationWorkers);

        await failedRestore.SetLifecycleAsync(configured.Id, WidgetLifecycleState.Visible);
        RegistryAssert.Equal(3, failedRestore.Clients.Count);
        RegistryAssert.Equal(1, failedRestore.Registry.RunningWorkerCount);
        RegistryAssert.Equal(1, failedRestore.Registry.ResidencyBudget.ApplicationWorkers);
    }

    internal static async Task ActivationInvalidationsSurviveFirstPresentation()
    {
        var configured = Widget("first-presentation", worker: 'p', catalog: 'p',
            residency: new WidgetResidencyPolicy { Mode = WidgetResidencyPolicies.SuspendWhenHidden });
        foreach (var failFirst in new[] { false, true })
        {
            var ready = false;
            await using var fixture = new RegistryFixture(Catalog(configured), configure: (_, client) =>
            {
                client.SnapshotFactory = sequence => new ViewSnapshot
                {
                    Sequence = sequence,
                    WidgetInstanceId = configured.InstanceId,
                    ActiveInputScopeId = "root",
                    Root = new ViewNode
                    {
                        Id = "root", Kind = ViewNodeKind.Stack, InputScopeId = "root",
                        Children = [new ViewNode { Id = "status", Kind = ViewNodeKind.Text,
                            Text = ready ? "Ready" : "Loading" }],
                    },
                };
                client.AfterSnapshot = () =>
                {
                    // The first frame still says Loading, but provider data has
                    // arrived before the bridge can commit that frame.
                    ready = true;
                    client.RaiseInvalidated(41);
                    client.RaiseInvalidated(42);
                    if (failFirst) throw new InvalidOperationException("synthetic publication failure");
                };
            });
            if (failFirst)
            {
                await RegistryAssert.ThrowsAsync<InvalidOperationException>(() => Establish());
                await fixture.Registry.DrainNotificationsAsync(configured.Id);
                RegistryAssert.Equal(0, fixture.Invalidations.Count);
                var failedClient = fixture.Clients.Single();
                failedClient.AfterSnapshot = null;
                failedClient.RaiseInvalidated(99); // Ordinary Background still suppresses updates.
                using var recovered = await Establish();
                await fixture.Registry.DrainNotificationsAsync(configured.Id);
                RegistryAssert.Equal(0, fixture.Invalidations.Count);
            }
            else
            {
                using var admitted = await Establish();
                RegistryAssert.Equal("Loading", admitted.Value.Snapshot.Root.Children[0].Text);
                await fixture.Registry.DrainNotificationsAsync(configured.Id);
                RegistryAssert.Equal(1, fixture.Invalidations.Count);
                RegistryAssert.Equal(42L, fixture.Invalidations[0].Revision);
                fixture.Clients.Single().AfterSnapshot = null;
                using var refreshed = await fixture.Registry.GetSnapshotAsync(
                    configured.Id, CancellationToken.None, CancellationToken.None);
                RegistryAssert.Equal("Ready", refreshed.Value.Snapshot.Root.Children[0].Text);
                RegistryAssert.True(refreshed.Value.Snapshot.Sequence > admitted.Value.Snapshot.Sequence,
                    "The retained update must permit a fresh frame without a user action.");
            }

            Task<BridgeClientPublication<BridgeClientPresentation>> Establish() =>
                fixture.Registry.EstablishPresentationAsync(configured.Id,
                    WidgetLifecycleState.Visible, WidgetPresentationTransactionKind.OrdinaryCheckpoint,
                    PresentationUpdateCapabilities.None, 0, 0, CancellationToken.None, CancellationToken.None);
        }
    }

    internal static async Task ActivationInvalidationsSurviveLifecycleCommit()
    {
        var configured = Widget("lifecycle-invalidation", worker: 'p', catalog: 'p',
            residency: new WidgetResidencyPolicy { Mode = WidgetResidencyPolicies.SuspendWhenHidden });
        await using var fixture = new RegistryFixture(Catalog(configured), configure: (_, client) =>
            client.AfterLifecycle = () => client.RaiseInvalidated(17));
        await fixture.SetLifecycleAsync(configured.Id, WidgetLifecycleState.Visible);
        await fixture.Registry.DrainNotificationsAsync(configured.Id);
        RegistryAssert.Equal(1, fixture.Invalidations.Count);
        RegistryAssert.Equal(17L, fixture.Invalidations[0].Revision);
        await fixture.SetLifecycleAsync(configured.Id, WidgetLifecycleState.Background);
        await fixture.Registry.DrainNotificationsAsync(configured.Id);
        var count = fixture.Invalidations.Count;
        fixture.Clients.Single().RaiseInvalidated(18);
        await fixture.Registry.DrainNotificationsAsync(configured.Id);
        RegistryAssert.Equal(count, fixture.Invalidations.Count);
    }

    internal static async Task LifecycleAndFirstSnapshotAreAtomic()
    {
        var configured = Widget("presentation-admission", worker: 'p', catalog: 'p');
        await using (var fixture = new RegistryFixture(Catalog(configured)))
        {
            using var admitted = await fixture.Registry.EstablishPresentationAsync(
                configured.Id,
                WidgetLifecycleState.Visible,
                WidgetPresentationTransactionKind.OrdinaryCheckpoint,
                PresentationUpdateCapabilities.None,
                0,
                0,
                CancellationToken.None,
                CancellationToken.None);
            RegistryAssert.Equal(1, fixture.Clients.Count);
            RegistryAssert.SequenceEqual(
                [WidgetLifecycleState.Visible], fixture.Clients[0].LifecycleStates);
            RegistryAssert.Equal(1L, admitted.Value.Snapshot.Sequence >> 32);
        }

        await using var failed = new RegistryFixture(
            Catalog(configured),
            configure: (_, client) =>
            {
                if (client.ClientGeneration == 1) client.FailSnapshots = 1;
            });
        await RegistryAssert.ThrowsAsync<InvalidOperationException>(() =>
            failed.Registry.EstablishPresentationAsync(
                configured.Id,
                WidgetLifecycleState.Interactive,
                WidgetPresentationTransactionKind.OrdinaryCheckpoint,
                PresentationUpdateCapabilities.None,
                0,
                0,
                CancellationToken.None,
                CancellationToken.None));
        var retained = failed.Clients.Single();
        RegistryAssert.SequenceEqual(
            [WidgetLifecycleState.Interactive, WidgetLifecycleState.Background],
            retained.LifecycleStates);
        RegistryAssert.Equal(0, retained.DisposeCount);
        RegistryAssert.Equal(1, failed.Registry.RunningWorkerCount);
        RegistryAssert.Equal(1, failed.Registry.ResidencyBudget.ApplicationWorkers);

        using var recovered = await failed.Registry.EstablishPresentationAsync(
            configured.Id,
            WidgetLifecycleState.Interactive,
            WidgetPresentationTransactionKind.OrdinaryCheckpoint,
            PresentationUpdateCapabilities.None,
            0,
            0,
            CancellationToken.None,
            CancellationToken.None);
        RegistryAssert.Equal(1, failed.Clients.Count);
        RegistryAssert.Equal(1L, recovered.Value.Snapshot.Sequence >> 32);
        RegistryAssert.SequenceEqual(
            [
                WidgetLifecycleState.Interactive,
                WidgetLifecycleState.Background,
                WidgetLifecycleState.Interactive,
            ],
            retained.LifecycleStates);
        retained.RaiseInvalidated(72);
        await failed.Registry.DrainNotificationsAsync(configured.Id);
        RegistryAssert.Equal(1, failed.Invalidations.Count);
    }

    internal static async Task PublicationAdmissionSerializesReplacement()
    {
        var initial = Widget("publication", worker: '7', catalog: '7');
        var replacement = initial with
        {
            WorkerFingerprint = Fingerprint('8'),
            CatalogFingerprint = Fingerprint('8'),
        };

        await using (var eventFixture = new RegistryFixture(Catalog(initial)))
        {
            await eventFixture.SetLifecycleAsync(initial.Id, WidgetLifecycleState.Visible);
            eventFixture.BlockInvalidationPublication = true;
            var old = eventFixture.Clients.Single();
            old.RaiseInvalidated(41);
            await eventFixture.InvalidationPublicationEntered.WaitAsync(TimeSpan.FromSeconds(2));
            RegistryAssert.True(eventFixture.Registry.ApplyCatalog(Catalog(replacement), revision: 1));
            var replacementOperation = eventFixture.Registry.SetLifecycleAsync(
                replacement.Id,
                WidgetLifecycleState.Visible,
                CancellationToken.None,
                CancellationToken.None);
            await eventFixture.InvalidationPublicationCancelled.WaitAsync(TimeSpan.FromSeconds(2));
            await eventFixture.InvalidationPublicationCompleted.WaitAsync(TimeSpan.FromSeconds(2));
            using var replacementPublication = await replacementOperation.WaitAsync(
                TimeSpan.FromSeconds(2));
            await old.Disposed.WaitAsync(TimeSpan.FromSeconds(2));
            RegistryAssert.Equal(2, eventFixture.Clients.Count);
            RegistryAssert.Equal(0, eventFixture.Invalidations.Count);
            RegistryAssert.Equal(1, old.DisposeCount);
        }

        await using var resultFixture = new RegistryFixture(Catalog(initial));
        await resultFixture.SetLifecycleAsync(initial.Id, WidgetLifecycleState.Visible);
        var resultPublication = await resultFixture.Registry.GetSnapshotAsync(
            initial.Id, CancellationToken.None, CancellationToken.None);
        var resultOld = resultFixture.Clients.Single();
        RegistryAssert.True(resultFixture.Registry.ApplyCatalog(Catalog(replacement), revision: 1));
        var resultReplacement = resultFixture.Registry.SetLifecycleAsync(
            replacement.Id,
            WidgetLifecycleState.Visible,
            CancellationToken.None,
            CancellationToken.None);
        RegistryAssert.True(!resultOld.Disposed.IsCompleted && !resultReplacement.IsCompleted,
            "Replacement passed an admitted old-generation snapshot result.");
        RegistryAssert.Equal(1, resultFixture.Clients.Count);

        resultPublication.Dispose();
        using var resultReplacementPublication = await resultReplacement.WaitAsync(
            TimeSpan.FromSeconds(2));
        await resultOld.Disposed.WaitAsync(TimeSpan.FromSeconds(2));
        RegistryAssert.Equal(2, resultFixture.Clients.Count);
    }

    internal static async Task NotificationLaneBoundsAndBalancesAdmission()
    {
        var failures = 0;
        var accepted = 0;
        var released = 0;
        var firstReleased = 0;
        var pendingInvalidationReleased = 0;
        var coalescedReleased = 0;
        var fullReleased = 0;
        var closedReleased = 0;
        var entered = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var lane = new BridgeClientNotificationLane(_ => failures++);

        BridgeClientNotificationAdmission Enqueue(
            BridgeClientNotificationKind kind,
            Func<CancellationToken, Task> publish,
            Action release)
        {
            var admission = lane.Enqueue(kind, publish, release, out var startPump);
            if (admission == BridgeClientNotificationAdmission.Accepted) accepted++;
            if (startPump) lane.StartPump();
            return admission;
        }

        var first = Enqueue(
            BridgeClientNotificationKind.Invalidation,
            async cancellationToken =>
            {
                RegistryAssert.True(!lane.IsGateHeldByCurrentThread,
                    "The lane invoked external publication while holding its gate.");
                entered.TrySetResult();
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken)
                    .ConfigureAwait(false);
            },
            () =>
            {
                RegistryAssert.True(!lane.IsGateHeldByCurrentThread,
                    "The lane released a registry admission while holding its gate.");
                firstReleased++;
                released++;
            });
        RegistryAssert.Equal(BridgeClientNotificationAdmission.Accepted, first);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(2));

        var pending = Enqueue(
            BridgeClientNotificationKind.Invalidation,
            _ => Task.CompletedTask,
            () => { pendingInvalidationReleased++; released++; });
        var coalesced = Enqueue(
            BridgeClientNotificationKind.Invalidation,
            _ => Task.CompletedTask,
            () => { coalescedReleased++; released++; });
        RegistryAssert.Equal(BridgeClientNotificationAdmission.Accepted, pending);
        RegistryAssert.Equal(BridgeClientNotificationAdmission.Coalesced, coalesced);
        for (var index = 0; index < BridgeClientNotificationLane.MaximumPendingFailures; index++)
        {
            RegistryAssert.Equal(
                BridgeClientNotificationAdmission.Accepted,
                Enqueue(
                    BridgeClientNotificationKind.Failure,
                    _ => Task.CompletedTask,
                    () => released++));
        }
        var full = Enqueue(
            BridgeClientNotificationKind.Failure,
            _ => Task.CompletedTask,
            () => { fullReleased++; released++; });
        RegistryAssert.Equal(BridgeClientNotificationAdmission.RejectedFull, full);
        RegistryAssert.Equal(33, lane.PendingCount);
        RegistryAssert.Equal(1, lane.DroppedFailures);
        RegistryAssert.Equal(34, accepted);

        await lane.CloseAndDrainAsync().WaitAsync(TimeSpan.FromSeconds(2));
        RegistryAssert.Equal(34, released);
        RegistryAssert.Equal(1, firstReleased);
        RegistryAssert.Equal(1, pendingInvalidationReleased);
        RegistryAssert.Equal(0, coalescedReleased);
        RegistryAssert.Equal(0, fullReleased);
        RegistryAssert.Equal(0, failures);
        RegistryAssert.Equal(0, lane.PendingCount);

        var closed = Enqueue(
            BridgeClientNotificationKind.Failure,
            _ => Task.CompletedTask,
            () => { closedReleased++; released++; });
        RegistryAssert.Equal(BridgeClientNotificationAdmission.RejectedClosed, closed);
        RegistryAssert.Equal(34, accepted);
        RegistryAssert.Equal(34, released);
        RegistryAssert.Equal(0, closedReleased);

        var orderingEntered = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var orderingRelease = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var published = new List<int>();
        var orderingLane = new BridgeClientNotificationLane(_ => failures++);
        RegistryAssert.Equal(
            BridgeClientNotificationAdmission.Accepted,
            orderingLane.Enqueue(
                BridgeClientNotificationKind.Invalidation,
                async cancellationToken =>
                {
                    orderingEntered.TrySetResult();
                    await orderingRelease.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
                    published.Add(1);
                },
                () => { },
                out var startOrderingPump));
        RegistryAssert.True(startOrderingPump);
        orderingLane.StartPump();
        await orderingEntered.Task.WaitAsync(TimeSpan.FromSeconds(2));
        RegistryAssert.Equal(
            BridgeClientNotificationAdmission.Accepted,
            orderingLane.Enqueue(
                BridgeClientNotificationKind.Invalidation,
                _ =>
                {
                    published.Add(2);
                    return Task.CompletedTask;
                },
                () => { },
                out _));
        RegistryAssert.Equal(
            BridgeClientNotificationAdmission.Coalesced,
            orderingLane.Enqueue(
                BridgeClientNotificationKind.Invalidation,
                _ =>
                {
                    published.Add(3);
                    return Task.CompletedTask;
                },
                () => { },
                out _));
        orderingRelease.TrySetResult();
        await orderingLane.DrainAsync().WaitAsync(TimeSpan.FromSeconds(2));
        RegistryAssert.SequenceEqual([1, 3], published);
        await orderingLane.CloseAndDrainAsync();
    }

    internal static async Task NotificationBurstIsBoundedAndRetires()
    {
        var initial = Widget("notification-burst", worker: 'b', catalog: 'b');
        var replacement = initial with
        {
            WorkerFingerprint = Fingerprint('c'),
            CatalogFingerprint = Fingerprint('c'),
        };
        await using var fixture = new RegistryFixture(Catalog(initial));
        await fixture.SetLifecycleAsync(initial.Id, WidgetLifecycleState.Visible);
        fixture.BlockInvalidationPublication = true;
        var old = fixture.Clients.Single();
        old.RaiseInvalidated(1);
        await fixture.InvalidationPublicationEntered.WaitAsync(TimeSpan.FromSeconds(2));
        for (var revision = 2; revision <= 100; revision++) old.RaiseInvalidated(revision);
        for (var index = 0; index < 100; index++) old.RaiseActionFailed($"failure-{index}");

        var bounded = fixture.Registry.NotificationStatus(initial.Id);
        RegistryAssert.Equal(33, bounded.Pending);
        RegistryAssert.Equal(68, bounded.DroppedFailures);
        RegistryAssert.Equal(34, bounded.ActivePublications);
        RegistryAssert.True(!bounded.IsRetiring);

        RegistryAssert.True(fixture.Registry.ApplyCatalog(Catalog(replacement), revision: 1));
        await fixture.InvalidationPublicationCancelled.WaitAsync(TimeSpan.FromSeconds(2));
        await fixture.SetLifecycleAsync(replacement.Id, WidgetLifecycleState.Visible);
        await old.Disposed.WaitAsync(TimeSpan.FromSeconds(2));
        RegistryAssert.Equal(1, old.DisposeCount);
        RegistryAssert.Equal(0, fixture.Invalidations.Count);
        RegistryAssert.Equal(0, fixture.ActionFailures.Count);
        var current = fixture.Registry.NotificationStatus(replacement.Id);
        RegistryAssert.Equal(0, current.Pending);
        RegistryAssert.Equal(0, current.ActivePublications);
        RegistryAssert.Equal(0, current.DroppedFailures);
    }

    internal static async Task CancelledRestartTransfersRetirement()
    {
        var configured = Widget("restart-cancel", worker: 'd', catalog: 'd');
        await using var fixture = new RegistryFixture(
            Catalog(configured),
            configure: (_, client) =>
            {
                if (client.ClientGeneration == 1) client.BlockDispose = true;
            });
        await fixture.SetLifecycleAsync(configured.Id, WidgetLifecycleState.Visible);
        fixture.BlockInvalidationPublication = true;
        var old = fixture.Clients.Single();
        old.RaiseInvalidated(1);
        await fixture.InvalidationPublicationEntered.WaitAsync(TimeSpan.FromSeconds(2));

        using var cancellation = new CancellationTokenSource();
        var restart = fixture.Registry.RestartAsync(configured.Id, cancellation.Token);
        await old.DisposeEntered.WaitAsync(TimeSpan.FromSeconds(2));
        await fixture.InvalidationPublicationCancelled.WaitAsync(TimeSpan.FromSeconds(2));
        cancellation.Cancel();
        _ = await RegistryAssert.ThrowsAsync<OperationCanceledException>(() => restart);

        var concurrent = fixture.Registry.SetLifecycleAsync(
            configured.Id,
            WidgetLifecycleState.Visible,
            CancellationToken.None,
            CancellationToken.None);
        RegistryAssert.True(!concurrent.IsCompleted,
            "Cancelled restart released its reserved generation before exact retirement completed.");
        old.ReleaseDispose();
        using var current = await concurrent.WaitAsync(TimeSpan.FromSeconds(2));
        await old.Disposed.WaitAsync(TimeSpan.FromSeconds(2));
        RegistryAssert.Equal(1, old.DisposeCount);
        RegistryAssert.Equal(2, fixture.Clients.Count);
        RegistryAssert.Equal(1, fixture.Registry.RunningWorkerCount);

        var terminal = new RegistryFixture(
            Catalog(configured),
            configure: (_, client) => client.BlockDispose = true);
        await terminal.SetLifecycleAsync(configured.Id, WidgetLifecycleState.Visible);
        var terminalOld = terminal.Clients.Single();
        using var terminalCancellation = new CancellationTokenSource();
        var terminalRestart = terminal.Registry.RestartAsync(
            configured.Id, terminalCancellation.Token);
        await terminalOld.DisposeEntered.WaitAsync(TimeSpan.FromSeconds(2));
        terminalCancellation.Cancel();
        _ = await RegistryAssert.ThrowsAsync<OperationCanceledException>(() => terminalRestart);
        var firstDispose = terminal.Registry.DisposeAsync().AsTask();
        var secondDispose = terminal.Registry.DisposeAsync().AsTask();
        RegistryAssert.True(!firstDispose.IsCompleted && !secondDispose.IsCompleted,
            "Terminal registry disposal returned before transferred restart retirement.");
        terminalOld.ReleaseDispose();
        await Task.WhenAll(firstDispose, secondDispose).WaitAsync(TimeSpan.FromSeconds(2));
        RegistryAssert.Equal(1, terminalOld.DisposeCount);
        RegistryAssert.Equal(1, terminal.Clients.Count);
        await terminal.DisposeAsync();
    }

    internal static async Task ExternalRetirementStartsOutsideIdentityGate()
    {
        var configured = Widget("external-dispose", worker: 'e', catalog: 'e');
        await using var fixture = new RegistryFixture(Catalog(configured));
        await fixture.SetLifecycleAsync(configured.Id, WidgetLifecycleState.Visible);
        var client = fixture.Clients.Single();
        client.OnDisposeStarted = () => RegistryAssert.True(
            !fixture.Registry.IsGateHeldByCurrentThread,
            "External client disposal began under the registry identity gate.");

        RegistryAssert.True(fixture.Registry.ApplyCatalog(Catalog(), revision: 1));
        await client.Disposed.WaitAsync(TimeSpan.FromSeconds(2));
        RegistryAssert.Equal(1, client.DisposeCount);
    }

    internal static async Task RetirementCompletionIncludesResidencyRelease()
    {
        var configured = Widget("retirement-completion", worker: 'r', catalog: 'r');
        await using var fixture = new RegistryFixture(
            Catalog(configured),
            configure: (_, client) => client.BlockDispose = true);
        await fixture.SetLifecycleAsync(configured.Id, WidgetLifecycleState.Visible);
        var client = fixture.Clients.Single();
        RegistryAssert.Equal(1, fixture.Registry.RunningWorkerCount);
        RegistryAssert.Equal(1, fixture.Registry.ResidencyBudget.ApplicationWorkers);
        RegistryAssert.Equal(64L,
            fixture.Registry.ResidencyBudget.ApplicationAdvisoryMemoryMb);

        RegistryAssert.True(fixture.Registry.ApplyCatalog(Catalog(), revision: 1));
        await client.DisposeEntered.WaitAsync(TimeSpan.FromSeconds(2));
        RegistryAssert.True(!client.IsRunning,
            "A terminal client remained operation-ready during retirement.");
        RegistryAssert.Equal(1, fixture.Registry.RunningWorkerCount);
        RegistryAssert.Equal(1, fixture.Registry.ResidencyBudget.ApplicationWorkers);
        RegistryAssert.Equal(64L,
            fixture.Registry.ResidencyBudget.ApplicationAdvisoryMemoryMb);

        client.ReleaseDispose();
        await client.Disposed.WaitAsync(TimeSpan.FromSeconds(2));
        RegistryAssert.Equal(0, fixture.Registry.RunningWorkerCount);
        RegistryAssert.Equal(0, fixture.Registry.ResidencyBudget.ApplicationWorkers);
        RegistryAssert.Equal(0L,
            fixture.Registry.ResidencyBudget.ApplicationAdvisoryMemoryMb);
        RegistryAssert.Equal(1, client.DisposeCount);
    }

    internal static async Task BudgetRefusalAndFailedStartReleaseReservations()
    {
        var first = Widget("first", worker: '1', catalog: '1');
        var second = Widget("second", worker: '2', catalog: '2');
        var failed = Widget("failed", worker: '3', catalog: '3');
        await using var fixture = new RegistryFixture(
            Catalog(first, second, failed),
            options: new WorkerResidencyBudgetOptions
            {
                MaximumApplicationWorkers = 1,
            },
            configure: (configured, client) =>
            {
                if (configured.Id == failed.Id) client.FailStartsAfterReservation = 1;
            });

        await fixture.SetLifecycleAsync(first.Id, WidgetLifecycleState.Visible);
        var refusal = await RegistryAssert.ThrowsAsync<BridgeWidgetRequestException>(() =>
            fixture.SetLifecycleAsync(second.Id, WidgetLifecycleState.Visible));
        RegistryAssert.Equal(second.Id, refusal.WidgetId);
        RegistryAssert.Equal("worker-admission-failed", refusal.FailureCode);
        RegistryAssert.Equal(
            typeof(WidgetProcessAdmissionException),
            refusal.InnerException?.GetType());
        RegistryAssert.Equal(1, fixture.Registry.ResidencyBudget.ApplicationWorkers);

        RegistryAssert.True(fixture.Registry.ApplyCatalog(Catalog(second, failed), revision: 1));
        await fixture.Clients.Single(client => client.WidgetId == first.Id).Disposed
            .WaitAsync(TimeSpan.FromSeconds(2));
        RegistryAssert.Equal(0, fixture.Registry.ResidencyBudget.ApplicationWorkers);
        await fixture.SetLifecycleAsync(second.Id, WidgetLifecycleState.Visible);
        RegistryAssert.Equal(1, fixture.Registry.ResidencyBudget.ApplicationWorkers);

        RegistryAssert.True(fixture.Registry.ApplyCatalog(Catalog(failed), revision: 2));
        await fixture.Clients.Single(client => client.WidgetId == second.Id).Disposed
            .WaitAsync(TimeSpan.FromSeconds(2));
        await RegistryAssert.ThrowsAsync<InvalidOperationException>(() =>
            fixture.SetLifecycleAsync(failed.Id, WidgetLifecycleState.Visible));
        RegistryAssert.Equal(0, fixture.Registry.ResidencyBudget.ApplicationWorkers);
        await fixture.SetLifecycleAsync(failed.Id, WidgetLifecycleState.Visible);
        RegistryAssert.Equal(1, fixture.Registry.ResidencyBudget.ApplicationWorkers);
    }

    internal static async Task TerminalDisposalSerializesWithConcurrentOperation()
    {
        var configured = Widget("blocked", worker: '4', catalog: '4');
        await using var fixture = new RegistryFixture(
            Catalog(configured),
            configure: (_, client) => client.BlockSnapshots = true);

        var snapshot = fixture.Registry.GetSnapshotAsync(
            configured.Id, CancellationToken.None, CancellationToken.None);
        var client = fixture.Clients.Single();
        await client.SnapshotEntered.WaitAsync(TimeSpan.FromSeconds(2));
        var firstDispose = fixture.Registry.DisposeAsync().AsTask();
        var secondDispose = fixture.Registry.DisposeAsync().AsTask();
        RegistryAssert.True(!client.Disposed.IsCompleted,
            "Terminal disposal bypassed the in-flight operation gate.");
        RegistryAssert.True(!firstDispose.IsCompleted && !secondDispose.IsCompleted,
            "A concurrent disposer returned before the shared terminal boundary.");

        client.ReleaseSnapshot();
        await RegistryAssert.ThrowsAsync<BridgeProtocolException>(() => snapshot);
        await Task.WhenAll(firstDispose, secondDispose).WaitAsync(TimeSpan.FromSeconds(2));
        RegistryAssert.Equal(1, client.DisposeCount);
        RegistryAssert.Equal(0, fixture.Registry.ResidencyBudget.ApplicationWorkers);
        client.RaiseInvalidated(11);
        client.RaiseActionFailed("stale");
        client.RaiseFailure();
        RegistryAssert.Equal(0, fixture.Invalidations.Count);
        RegistryAssert.Equal(0, fixture.ActionFailures.Count);
        RegistryAssert.Equal(0, fixture.Failures.Count);
    }

    internal static async Task RetirementFailuresAreObservedAndDrained()
    {
        var throwing = Widget("throwing", worker: '9', catalog: '9');
        var healthy = Widget("healthy", worker: 'a', catalog: 'a');
        var fixture = new RegistryFixture(
            Catalog(throwing, healthy),
            configure: (configured, client) =>
            {
                if (configured.Id == throwing.Id)
                    client.DisposeFailure = new OutOfMemoryException(
                        "synthetic fatal disposal failure");
            });
        await fixture.SetLifecycleAsync(throwing.Id, WidgetLifecycleState.Visible);
        await fixture.SetLifecycleAsync(healthy.Id, WidgetLifecycleState.Visible);
        RegistryAssert.True(fixture.Registry.ApplyCatalog(Catalog(), revision: 1));
        await Task.WhenAll(fixture.Clients.Select(client => client.Disposed))
            .WaitAsync(TimeSpan.FromSeconds(2));

        var firstDispose = fixture.Registry.DisposeAsync().AsTask();
        var secondDispose = fixture.Registry.DisposeAsync().AsTask();
        _ = await RegistryAssert.ThrowsAsync<AggregateException>(() => firstDispose);
        _ = await RegistryAssert.ThrowsAsync<AggregateException>(() => secondDispose);
        RegistryAssert.SequenceEqual([1, 1], fixture.Clients.Select(client => client.DisposeCount));
        RegistryAssert.Equal(0, fixture.Registry.RunningWorkerCount);
        RegistryAssert.Equal(0, fixture.Registry.ResidencyBudget.ApplicationWorkers);
    }

    internal static async Task LocalPackageImportOriginIsExact()
    {
        var settings = Widget("settings", worker: 'a', catalog: 'a') with
        {
            PackageId = "widgetrail.firstparty.settings",
            PublisherId = "widgetrail.firstparty",
            InstanceId = "settings.default",
            RequiresAppContainer = false,
            DeclaredCapabilities = [],
        };
        await using var fixture = new RegistryFixture(Catalog(settings));
        await fixture.SetLifecycleAsync(settings.Id, WidgetLifecycleState.Interactive);
        var descriptor = settings.PublicDescriptor();
        var exact = new BridgeLocalWidgetPackageOrigin(
            settings.Id,
            settings.PackageId,
            settings.PublisherId,
            descriptor.InstanceId,
            descriptor.RuntimeGeneration,
            descriptor.PresentationGeneration);
        using (fixture.Registry.AdmitLocalWidgetPackageImport(exact)) { }

        await fixture.SetLifecycleAsync(settings.Id, WidgetLifecycleState.Visible);
        _ = await RegistryAssert.ThrowsAsync<BridgeProtocolException>(() => Task.Run(() =>
        {
            using var refused = fixture.Registry.AdmitLocalWidgetPackageImport(exact);
        }));

        await fixture.SetLifecycleAsync(settings.Id, WidgetLifecycleState.Interactive);
        foreach (var forged in new[]
        {
            exact with { PackageId = "dev.example.settings" },
            exact with { RuntimeGeneration = new string('f', 64) },
            exact with { PresentationGeneration = new string('e', 64) },
        })
        {
            _ = await RegistryAssert.ThrowsAsync<BridgeProtocolException>(() => Task.Run(() =>
            {
                using var refused = fixture.Registry.AdmitLocalWidgetPackageImport(forged);
            }));
        }
    }

    private static BridgeCatalog Catalog(params ConfiguredWidget[] widgets) => new(widgets);

    private static ConfiguredWidget Widget(
        string id,
        char worker,
        char catalog,
        WidgetResidencyPolicy? residency = null) => new()
    {
        Id = id,
        PackageId = $"dev.example.{id}",
        PublisherId = "dev.example",
        Name = id,
        InstanceId = $"{id}.instance",
        WorkerExecutable = Environment.ProcessPath!,
        MemoryRequestMb = 64,
        ResidencyPolicy = residency ?? new WidgetResidencyPolicy(),
        WorkerFingerprint = Fingerprint(worker),
        CatalogFingerprint = Fingerprint(catalog),
    };

    private static string Fingerprint(char value) => new(value, 64);
}

internal sealed class BlockingRegistrationBackend : IAppLibraryPlatformBrokerBackend
{
    internal TaskCompletionSource Started { get; } =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal TaskCompletionSource Cancelled { get; } =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal int Committed { get; private set; }

    public Task<RunningAppBackendObservationPage> ObserveRunningAppsAsync(
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(new RunningAppBackendObservationPage(
            [new("portable-running", "process-instance", "Portable running",
                AppLibraryKind.Application, "Portable")],
            "running-revision"));
    }

    public async Task<RegisterRunningAppBackendSummary> RegisterRunningAppAsync(
        BrokerWidgetIdentity identity,
        RegisterRunningAppBackendRequest request,
        CancellationToken cancellationToken)
    {
        Started.TrySetResult();
        try
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken)
                .ConfigureAwait(false);
            Committed++;
            throw new InvalidOperationException("Blocked registration unexpectedly resumed.");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            Cancelled.TrySetResult();
            throw;
        }
    }
}

internal sealed class RegistryPrivateStateBackend(
    ConfiguredWidget first,
    ConfiguredWidget second) :
    IPrivateStatePlatformBrokerBackend,
    IAppLibraryPlatformBrokerBackend
{
    private readonly Dictionary<string, (bool Exists, long Revision)> _state = new()
    {
        [first.PackageId] = (true, 3),
        [second.PackageId] = (true, 7),
    };
    private readonly Dictionary<string, (bool Exists, long Revision)> _registrations = new()
    {
        [first.PackageId] = (true, 5),
        [second.PackageId] = (true, 11),
    };

    internal bool Exists(string packageId) => _state[packageId].Exists;
    internal bool RegistrationExists(string packageId) =>
        _registrations[packageId].Exists;
    internal void Advance(string packageId)
    {
        var current = _state[packageId];
        _state[packageId] = (current.Exists, current.Revision + 1);
    }

    public Task<PrivateStateSnapshotSummary> ReadPrivateStateAsync(
        BrokerWidgetIdentity identity,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var current = _state[identity.PackageId];
        return Task.FromResult(new PrivateStateSnapshotSummary(
            current.Exists,
            current.Exists ? Convert.ToBase64String("{}"u8) : null,
            current.Revision));
    }

    public Task<PrivateStateMutationSummary> ClearPrivateStateAsync(
        BrokerWidgetIdentity identity,
        ClearPrivateStateRequest request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var current = _state[identity.PackageId];
        if (request.ExpectedRevision != current.Revision)
            throw new BrokerException("private_state_conflict", "Synthetic conflict.");
        _state[identity.PackageId] = (false, current.Revision + 1);
        return Task.FromResult(new PrivateStateMutationSummary(current.Revision + 1));
    }

    public Task<AppLibraryRegistrationStateSummary>
        GetRunningAppRegistrationStateAsync(
            BrokerWidgetIdentity identity,
            CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var current = _registrations[identity.PackageId];
        return Task.FromResult(new AppLibraryRegistrationStateSummary(
            current.Exists, current.Revision));
    }

    public Task ClearRunningAppRegistrationsAsync(
        BrokerWidgetIdentity identity,
        long expectedRevision,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var current = _registrations[identity.PackageId];
        if (current.Revision != expectedRevision)
            throw new BrokerException(
                "app_registration_conflict", "Synthetic registration conflict.");
        _registrations[identity.PackageId] = (false, current.Revision + 1);
        return Task.CompletedTask;
    }
}

internal sealed class RegistryFixture : IAsyncDisposable
{
    private readonly Action<ConfiguredWidget, RegistryTestClient>? _configure;
    private readonly TaskCompletionSource _invalidationPublicationEntered = new(
        TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _invalidationPublicationCompleted = new(
        TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _invalidationPublicationCancelled = new(
        TaskCreationOptions.RunContinuationsAsynchronously);

    internal RegistryFixture(
        BridgeCatalog catalog,
        WorkerResidencyBudgetOptions? options = null,
        Action<ConfiguredWidget, RegistryTestClient>? configure = null,
        Func<TimeSpan, CancellationToken, Task>? delay = null)
    {
        _configure = configure;
        Registry = new BridgeClientRegistry(
            catalog,
            options ?? new WorkerResidencyBudgetOptions(),
            CreateClient,
            async (item, cancellationToken) =>
            {
                if (BlockInvalidationPublication)
                {
                    _invalidationPublicationEntered.TrySetResult();
                    try
                    {
                        await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken)
                            .ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        _invalidationPublicationCancelled.TrySetResult();
                        throw;
                    }
                    finally
                    {
                        _invalidationPublicationCompleted.TrySetResult();
                    }
                }
                Invalidations.Add(item);
            },
            (item, cancellationToken) =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                ActionFailures.Add(item);
                return Task.CompletedTask;
            },
            (item, cancellationToken) =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                Failures.Add(item);
                return Task.CompletedTask;
            },
            delay);
    }

    internal BridgeClientRegistry Registry { get; }
    internal List<RegistryTestClient> Clients { get; } = [];
    internal List<BridgeClientInvalidation> Invalidations { get; } = [];
    internal List<BridgeClientActionFailure> ActionFailures { get; } = [];
    internal List<BridgeClientRuntimeFailure> Failures { get; } = [];
    internal bool BlockInvalidationPublication { get; set; }
    internal Task InvalidationPublicationEntered => _invalidationPublicationEntered.Task;
    internal Task InvalidationPublicationCompleted => _invalidationPublicationCompleted.Task;
    internal Task InvalidationPublicationCancelled => _invalidationPublicationCancelled.Task;

    public ValueTask DisposeAsync() => Registry.DisposeAsync();

    internal async Task SetLifecycleAsync(string widgetId, WidgetLifecycleState state)
    {
        using var publication = await Registry.SetLifecycleAsync(
            widgetId, state, CancellationToken.None, CancellationToken.None);
    }

    internal async Task<BridgeClientSnapshot> GetSnapshotAsync(string widgetId)
    {
        using var publication = await Registry.GetSnapshotAsync(
            widgetId, CancellationToken.None, CancellationToken.None);
        return publication.Value;
    }

    internal async Task<ViewSnapshot> GetPresentationAsync(
        string widgetId,
        WidgetPresentationTransactionKind transactionKind,
        long baseSequence,
        long recoveryOriginSequence)
    {
        using var publication = await Registry.GetPresentationAsync(
            widgetId,
            transactionKind,
            transactionKind == WidgetPresentationTransactionKind.IncrementalUpdate
                ? PresentationUpdateCapabilities.Current
                : PresentationUpdateCapabilities.None,
            baseSequence,
            recoveryOriginSequence,
            CancellationToken.None,
            CancellationToken.None);
        return publication.Value.Snapshot;
    }

    internal async Task<WidgetLifecycleState> RestartAsync(string widgetId)
    {
        using var publication = await Registry.RestartAsync(
            widgetId, CancellationToken.None);
        return publication.Value;
    }

    private IBridgeWidgetClient CreateClient(
        ConfiguredWidget configured,
        Func<IDisposable> reserve)
    {
        var client = new RegistryTestClient(
            configured.Id,
            configured.InstanceId,
            Clients.Count + 1,
            reserve);
        _configure?.Invoke(configured, client);
        Clients.Add(client);
        return client;
    }
}

internal sealed class RegistryTestClient(
    string widgetId,
    string instanceId,
    int clientGeneration,
    Func<IDisposable> reserve) : IBridgeWidgetClient
{
    private readonly object _gate = new();
    private readonly TaskCompletionSource _snapshotEntered = new(
        TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _snapshotRelease = new(
        TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _disposed = new(
        TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _disposeEntered = new(
        TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _disposeRelease = new(
        TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _unloaded = new(
        TaskCreationOptions.RunContinuationsAsynchronously);
    private IDisposable? _reservation;
    private int _running;
    private int _starts;
    private int _disposeCount;
    private int _unloadCount;
    private long _snapshotSequence;

    public event EventHandler<long>? Invalidated;
    public event EventHandler<WidgetActionFailure>? ActionFailed;
    public event EventHandler<WidgetFailure>? Failed;
    public event EventHandler<WidgetProcessLifetimeDiagnostic>? LifetimeChanged;

    internal string WidgetId { get; } = widgetId;
    internal int ClientGeneration { get; } = clientGeneration;
    internal bool BlockSnapshots { get; set; }
    internal bool BlockDispose { get; set; }
    internal Exception? DisposeFailure { get; set; }
    internal int FailStartsAfterReservation { get; set; }
    internal int FailLifecycleTransitions { get; set; }
    internal int FailSnapshots { get; set; }
    internal Exception? SnapshotFailure { get; set; }
    internal Action? OnDisposeStarted { get; set; }
    internal Func<ValueTask>? OnDisposeAsync { get; set; }
    internal Task SnapshotEntered => _snapshotEntered.Task;
    internal Task Disposed => _disposed.Task;
    internal Task DisposeEntered => _disposeEntered.Task;
    internal Task Unloaded => _unloaded.Task;
    internal int DisposeCount => Volatile.Read(ref _disposeCount);
    internal int UnloadCount => Volatile.Read(ref _unloadCount);
    internal List<WidgetLifecycleState> LifecycleStates { get; } = [];
    internal List<(WidgetPresentationTransactionKind TransactionKind,
        long BaseSequence, long RecoveryOriginSequence)> PresentationRequests { get; } = [];
    internal List<ControllerInputEvent> ControllerInputs { get; } = [];
    internal bool? RevalidatedHandled { get; set; } = true;
    internal int RevalidatedAttempts { get; private set; }
    public Task<bool?> SendRevalidatedControllerInputAsync(
        ControllerInputEvent input, CancellationToken cancellationToken, string? admittedActionId = null)
    {
        cancellationToken.ThrowIfCancellationRequested();
        RevalidatedAttempts++;
        if (RevalidatedHandled is not null) ControllerInputs.Add(input);
        return Task.FromResult(RevalidatedHandled);
    }
    internal List<WidgetActionEvent> ActionEvents { get; } = [];
    internal List<EmbeddedMediaPlaybackEvent> EmbeddedMediaPlaybackEvents { get; } = [];
    internal Func<long, ViewSnapshot>? SnapshotFactory { get; set; }
    internal Action? AfterSnapshot { get; set; }
    internal Action? AfterLifecycle { get; set; }
    internal bool RebaseRecoverySequence { get; set; }
    public bool IsRunning => Volatile.Read(ref _running) != 0;
    public int Starts => Volatile.Read(ref _starts);

    public async Task<ViewSnapshot> GetSnapshotAsync(CancellationToken cancellationToken)
    {
        EnsureStarted();
        if (SnapshotFailure is { } snapshotFailure)
            throw snapshotFailure;
        if (FailSnapshots > 0)
        {
            FailSnapshots--;
            throw new InvalidOperationException("synthetic first snapshot failure");
        }
        if (BlockSnapshots)
        {
            _snapshotEntered.TrySetResult();
            await _snapshotRelease.Task.ConfigureAwait(false);
        }
        cancellationToken.ThrowIfCancellationRequested();
        var sequence = Interlocked.Increment(ref _snapshotSequence) +
            ((long)ClientGeneration << 32);
        if (SnapshotFactory is { } snapshotFactory) return snapshotFactory(sequence);
        return new ViewSnapshot
        {
            Sequence = sequence,
            WidgetInstanceId = instanceId,
            ActiveInputScopeId = "root",
            Root = new ViewNode
            {
                Id = "root",
                Kind = ViewNodeKind.Stack,
                InputScopeId = "root",
            },
        };
    }

    public async Task<WidgetRuntimePresentation> GetPresentationAsync(
        PresentationUpdateCapabilities capabilities,
        string presentationGeneration,
        long baseSequence,
        WidgetPresentationTransactionKind transactionKind,
        long recoveryOriginSequence,
        CancellationToken cancellationToken)
    {
        PresentationRequests.Add((transactionKind, baseSequence, recoveryOriginSequence));
        var snapshot = await GetSnapshotAsync(cancellationToken).ConfigureAwait(false);
        AfterSnapshot?.Invoke();
        if (RebaseRecoverySequence &&
            transactionKind == WidgetPresentationTransactionKind.RecoveryCheckpoint)
        {
            snapshot = snapshot with { Sequence = checked(recoveryOriginSequence + 1) };
        }
        return new(
            transactionKind,
            baseSequence,
            recoveryOriginSequence,
            snapshot,
            null);
    }

    public Task SetLifecycleStateAsync(
        WidgetLifecycleState state,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (state != WidgetLifecycleState.Background) EnsureStarted();
        if (FailLifecycleTransitions > 0)
        {
            FailLifecycleTransitions--;
            throw new InvalidOperationException("synthetic lifecycle restore failure");
        }
        lock (_gate) LifecycleStates.Add(state);
        AfterLifecycle?.Invoke();
        return Task.CompletedTask;
    }

    public Task<bool> TryRestoreLifecycleStateAsync(
        WidgetLifecycleState state,
        int expectedStartOrdinal,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            if (_running == 0 || _starts != expectedStartOrdinal)
                return Task.FromResult(false);
            LifecycleStates.Add(state);
            return Task.FromResult(true);
        }
    }

    public Task<WidgetOperationAdmission> AdmitActionAsync(
        WidgetActionEvent action,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        EnsureStarted();
        ActionEvents.Add(action);
        return Task.FromResult(WidgetOperationAdmission.Enqueued);
    }

    public Task<bool> SendControllerInputAsync(
        ControllerInputEvent input,
        WidgetDashboardGestureAuthority? authority,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        EnsureStarted();
        ControllerInputs.Add(input);
        return Task.FromResult(true);
    }

    public Task SendEmbeddedMediaPlaybackEventAsync(
        EmbeddedMediaPlaybackEvent playbackEvent,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        EnsureStarted();
        EmbeddedMediaPlaybackEvents.Add(playbackEvent);
        return Task.CompletedTask;
    }

    public Task UnloadAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Interlocked.Increment(ref _unloadCount);
        Stop();
        _unloaded.TrySetResult();
        return Task.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Increment(ref _disposeCount) == 1)
        {
            OnDisposeStarted?.Invoke();
            StopOperationally();
            _disposeEntered.TrySetResult();
            try
            {
                if (OnDisposeAsync is not null)
                    await OnDisposeAsync().ConfigureAwait(false);
                if (BlockDispose)
                    await _disposeRelease.Task.ConfigureAwait(false);
                if (DisposeFailure is { } failure)
                    throw failure;
            }
            finally
            {
                ReleaseReservation();
                _disposed.TrySetResult();
            }
        }
    }

    internal void ReleaseSnapshot() => _snapshotRelease.TrySetResult();
    internal void ReleaseDispose() => _disposeRelease.TrySetResult();
    internal void StopForTest() => Stop();
    internal void RaiseInvalidated(long revision) => Invalidated?.Invoke(this, revision);
    internal void RaiseActionFailed(string actionId) => ActionFailed?.Invoke(
        this, new WidgetActionFailure(actionId, "source", "failed"));
    internal void RaiseFailure() => Failed?.Invoke(
        this,
        new WidgetFailure(
            WidgetFailureReason.ProcessExited,
            17,
            null,
            RestartsUsed: 0,
            CanRestart: true));
    internal void RaiseLifetime(WidgetProcessLifetimeDiagnostic diagnostic) =>
        LifetimeChanged?.Invoke(this, diagnostic);

    private void EnsureStarted()
    {
        lock (_gate)
        {
            if (_running != 0) return;
            var lease = reserve();
            if (FailStartsAfterReservation > 0)
            {
                FailStartsAfterReservation--;
                lease.Dispose();
                throw new InvalidOperationException("synthetic failed start");
            }
            _reservation = lease;
            _running = 1;
            _starts++;
        }
    }

    private void Stop()
    {
        StopOperationally();
        ReleaseReservation();
    }

    private void StopOperationally()
    {
        lock (_gate)
        {
            _running = 0;
        }
    }

    private void ReleaseReservation()
    {
        IDisposable? reservation;
        lock (_gate)
        {
            reservation = _reservation;
            _reservation = null;
        }
        reservation?.Dispose();
    }
}

internal sealed class ManualRegistryDelay
{
    private readonly Channel<ManualRegistryDelayCall> _calls =
        Channel.CreateUnbounded<ManualRegistryDelayCall>();

    internal Task InvokeAsync(TimeSpan delay, CancellationToken cancellationToken)
    {
        var call = new ManualRegistryDelayCall(cancellationToken);
        if (!_calls.Writer.TryWrite(call))
            throw new InvalidOperationException("Unable to publish manual delay call.");
        return call.WaitAsync();
    }

    internal ValueTask<ManualRegistryDelayCall> NextAsync() =>
        _calls.Reader.ReadAsync();
}

internal sealed class ManualRegistryDelayCall
{
    private readonly TaskCompletionSource _release = new(
        TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly CancellationTokenRegistration _registration;

    internal ManualRegistryDelayCall(CancellationToken cancellationToken)
    {
        _registration = cancellationToken.Register(
            () => CancellationObserved.TrySetResult());
    }

    internal TaskCompletionSource CancellationObserved { get; } = new(
        TaskCreationOptions.RunContinuationsAsynchronously);

    internal async Task WaitAsync()
    {
        try { await _release.Task.ConfigureAwait(false); }
        finally { _registration.Dispose(); }
    }

    internal void Release() => _release.TrySetResult();
}

internal static class RegistryAssert
{
    internal static void True(bool condition, string message = "Expected true.")
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    internal static void False(bool condition, string message = "Expected false.")
    {
        if (condition) throw new InvalidOperationException(message);
    }

    internal static void Equal<T>(T expected, T actual)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new InvalidOperationException($"Expected '{expected}', got '{actual}'.");
    }

    internal static void SequenceEqual<T>(IEnumerable<T> expected, IEnumerable<T> actual)
    {
        if (!expected.SequenceEqual(actual))
            throw new InvalidOperationException(
                $"Expected [{string.Join(", ", expected)}], got [{string.Join(", ", actual)}].");
    }

    internal static async Task<T> ThrowsAsync<T>(Func<Task> action) where T : Exception
    {
        try { await action().ConfigureAwait(false); }
        catch (T exception) { return exception; }
        throw new InvalidOperationException($"Expected {typeof(T).Name}.");
    }
}
