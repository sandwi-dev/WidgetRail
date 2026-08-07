using System.Text;
using GameBarAlternative.Samples.ClockWidget;
using GameBarAlternative.WidgetProtocol;
using GameBarAlternative.WidgetSdk;

var tests = new (string Name, Func<Task> Run)[]
{
    ("Snapshot serialization is deterministic and round-trips", SnapshotRoundTrip),
    ("Duplicate stable IDs are rejected", DuplicateIdsAreRejected),
    ("Broken focus neighbors are rejected", BrokenFocusIsRejected),
    ("Invalid progress is rejected", InvalidProgressIsRejected),
    ("Image and icon nodes round-trip as renderer-neutral primitives", VisualNodesRoundTrip),
    ("Input surfaces serialize and validate scoped shortcuts", InputSurfacesValidate),
    ("Unsafe image sources are rejected", UnsafeImageSourcesAreRejected),
    ("Visual nodes require accessibility and semantic data", VisualNodeRequirementsAreEnforced),
    ("Button interaction states serialize deterministically", ButtonStatesRoundTrip),
    ("Buttons expose closed semantic icons without action-ID inference", ButtonIconsRoundTrip),
    ("Settings composites expose stable controller and accessibility semantics", SettingsCompositesAreSemantic),
    ("Interaction states reject invalid node combinations", InvalidInteractionStatesAreRejected),
    ("Unknown protocol JSON fields are rejected", UnknownFieldsAreRejected),
    ("Null protocol collections report validation errors", NullCollectionsAreRejected),
    ("Valid manifest passes", ValidManifestPasses),
    ("Unsafe manifest values report errors", UnsafeManifestFails),
    ("Null manifest collections report validation errors", NullManifestCollectionsFail),
    ("Clock sample renders controller metadata", ClockRenders),
    ("Clock refresh invalidates once", ClockInvalidates),
    ("Default controller routing resolves dashboard quick actions", DashboardInputResolves),
    ("Default controller routing resolves focused shortcuts", FocusedShortcutResolves),
    ("Default controller routing activates focused buttons with A", FocusedButtonActivates),
    ("Default controller routing ignores A on non-buttons", NonButtonDoesNotActivate),
    ("Default controller routing blocks disabled and busy buttons", DisabledAndBusyButtonsDoNotActivate),
    ("Controller shortcut fallback stays in explicit active input surface", ScopedShortcutRouting),
};

var failures = new List<string>();
foreach (var test in tests)
{
    try
    {
        await test.Run();
        Console.WriteLine($"PASS {test.Name}");
    }
    catch (Exception exception)
    {
        failures.Add($"FAIL {test.Name}: {exception.Message}");
        Console.Error.WriteLine(failures[^1]);
    }
}

Console.WriteLine($"{tests.Length - failures.Count}/{tests.Length} tests passed.");
return failures.Count == 0 ? 0 : 1;

static Task SnapshotRoundTrip()
{
    var view = new WidgetView(
        UI.Row("root",
            UI.Button("Previous", "previous", "previous").FocusRight("next").Shortcut(ControllerButton.LeftBumper),
            UI.Button("Next", "next", "next").FocusLeft("previous").Shortcut(ControllerButton.RightBumper)),
        "previous");
    var snapshot = view.CreateSnapshot("test.instance", 42);
    var first = SnapshotJson.Serialize(snapshot);
    var second = SnapshotJson.Serialize(snapshot);
    Assert.SequenceEqual(first, second, "Serialization must be byte-for-byte deterministic.");
    var restored = SnapshotJson.Deserialize(first);
    Assert.Equal(42L, restored.Sequence);
    Assert.Equal("previous", restored.InitialFocusId);
    Assert.Equal(2, restored.Root.Children.Count);
    return Task.CompletedTask;
}

static Task DuplicateIdsAreRejected()
{
    var view = new WidgetView(UI.Stack("root", UI.Text("One", "same"), UI.Text("Two", "same")));
    var exception = Assert.Throws<ProtocolValidationException>(() => view.CreateSnapshot("test.instance", 0));
    Assert.True(exception.Errors.Any(error => error.Code == "duplicate_id"), "Expected duplicate_id.");
    return Task.CompletedTask;
}

static Task BrokenFocusIsRejected()
{
    var view = new WidgetView(UI.Stack("root", UI.Button("Go", "go", "go").FocusDown("missing")), "go");
    var exception = Assert.Throws<ProtocolValidationException>(() => view.CreateSnapshot("test.instance", 0));
    Assert.True(exception.Errors.Any(error => error.Code == "invalid_focus_target"), "Expected invalid_focus_target.");
    return Task.CompletedTask;
}

static Task InvalidProgressIsRejected()
{
    var view = new WidgetView(UI.Stack("root", UI.Progress(11, 10, "progress")));
    var exception = Assert.Throws<ProtocolValidationException>(() => view.CreateSnapshot("test.instance", 0));
    Assert.True(exception.Errors.Any(error => error.Code == "invalid_progress"), "Expected invalid_progress.");
    return Task.CompletedTask;
}

static Task VisualNodesRoundTrip()
{
    var snapshot = new WidgetView(
        UI.Row("root",
            UI.Image("https://images.example/cover.jpg", "cover", "Album cover", ImageFit.Contain)
                .Classes("album-cover"),
            UI.Icon(WidgetGlyph.Music, "music-icon", "Music").Classes("media-glyph")))
        .CreateSnapshot("test.instance", 3);

    var restored = SnapshotJson.Deserialize(SnapshotJson.Serialize(snapshot));
    var cover = Find(restored.Root, "cover");
    Assert.Equal(ViewNodeKind.Image, cover.Kind);
    Assert.Equal("https://images.example/cover.jpg", cover.ImageSource);
    Assert.Equal(ImageFit.Contain, cover.ImageFit);
    Assert.Equal("Album cover", cover.AccessibilityLabel);
    Assert.Equal("album-cover", cover.StyleClasses.Single());
    var icon = Find(restored.Root, "music-icon");
    Assert.Equal(ViewNodeKind.Icon, icon.Kind);
    Assert.Equal(WidgetGlyph.Music, icon.Glyph);
    Assert.Equal("Music", icon.AccessibilityLabel);
    return Task.CompletedTask;
}

static Task InputSurfacesValidate()
{
    var valid = new WidgetView(
        UI.Stack("root",
            UI.Button("Root back", "root-back", "root-back").Shortcut(ControllerButton.LeftBumper),
            UI.Stack("dialog",
                UI.Button("Dialog back", "dialog-back", "dialog-back").Shortcut(ControllerButton.LeftBumper))
                .InputScope("dialog-window")
                .Shortcut(ControllerButton.B, "close-dialog")),
        InitialFocusId: "dialog-back",
        ActiveInputScopeId: "dialog-window")
        .CreateSnapshot("scope.instance", 1);
    var restored = SnapshotJson.Deserialize(SnapshotJson.Serialize(valid));
    Assert.Equal("dialog-window", restored.Root.Children[1].InputScopeId);
    Assert.Equal("close-dialog", restored.Root.Children[1].Shortcuts.Single().ActionId);
    Assert.Equal("dialog-back", restored.InitialFocusId);

    var ambiguous = new WidgetView(
        UI.Stack("root",
            UI.Button("One", "one", "one").Shortcut(ControllerButton.RightBumper),
            UI.Button("Two", "two", "two").Disabled().Shortcut(ControllerButton.RightBumper)));
    var exception = Assert.Throws<ProtocolValidationException>(() =>
        ambiguous.CreateSnapshot("scope.instance", 2));
    Assert.True(exception.Errors.Any(error => error.Code == "ambiguous_scope_shortcut"),
        "Duplicate bindings in one input surface must be rejected.");

    var crossScopeFocus = new WidgetView(
        UI.Stack("root",
            UI.Button("Outside", "outside", "outside").FocusRight("inside"),
            UI.Stack("dialog", UI.Button("Inside", "inside", "inside"))
                .InputScope("dialog-window")));
    var focusException = Assert.Throws<ProtocolValidationException>(() =>
        crossScopeFocus.CreateSnapshot("scope.instance", 3));
    Assert.True(focusException.Errors.Any(error => error.Code == "cross_input_scope_focus"),
        "Focus graphs must remain inside their owning input surface.");

    var unreachable = new WidgetView(UI.Stack("root")
        .Shortcut(ControllerButton.A, "reserved")
        .Shortcut(ControllerButton.DPadLeft, "reserved-dpad")
        .Shortcut(ControllerButton.B, "released", ControllerEventPhase.Released)
        .Shortcut(ControllerButton.X, "repeated", ControllerEventPhase.Repeated));
    var unreachableException = Assert.Throws<ProtocolValidationException>(() =>
        unreachable.CreateSnapshot("scope.instance", 4));
    Assert.True(unreachableException.Errors.Any(error => error.Code == "reserved_shortcut_button"),
        "A and D-pad shortcut bindings must be rejected.");
    Assert.True(unreachableException.Errors.Count(error => error.Code == "unsupported_shortcut_phase") == 2,
        "Released and repeated shortcut phases must be rejected for the MVP host.");

    var wrongInitialScope = new WidgetView(
        UI.Stack("root",
            UI.Button("Outside", "outside", "outside"),
            UI.Stack("dialog", UI.Button("Inside", "inside", "inside"))
                .InputScope("dialog-window")),
        InitialFocusId: "outside",
        ActiveInputScopeId: "dialog-window");
    var initialException = Assert.Throws<ProtocolValidationException>(() =>
        wrongInitialScope.CreateSnapshot("scope.instance", 5));
    Assert.True(initialException.Errors.Any(error => error.Code == "initial_focus_outside_active_scope"),
        "Initial focus must belong to the explicitly active scope.");

    var invalidActive = new WidgetView(UI.Stack("root"), ActiveInputScopeId: "missing-window");
    var activeException = Assert.Throws<ProtocolValidationException>(() =>
        invalidActive.CreateSnapshot("scope.instance", 6));
    Assert.True(activeException.Errors.Any(error => error.Code == "invalid_active_input_scope"),
        "The active input scope must exist.");

    var dashboardButtons = new WidgetView(
        UI.Stack("root"),
        QuickActions:
        [
            new WidgetQuickAction(ControllerButton.B, "back", "Back"),
            new WidgetQuickAction(ControllerButton.Menu, "menu", "Menu"),
            new WidgetQuickAction(ControllerButton.View, "view", "View"),
        ]);
    _ = dashboardButtons.CreateSnapshot("scope.instance", 7);
    foreach (var reserved in new[]
             {
                 ControllerButton.A, ControllerButton.Y,
                 ControllerButton.DPadUp, ControllerButton.DPadDown,
                 ControllerButton.DPadLeft, ControllerButton.DPadRight,
             })
    {
        var reservedView = new WidgetView(
            UI.Stack("root"),
            QuickActions: [new WidgetQuickAction(reserved, "reserved", "Reserved")]);
        var reservedException = Assert.Throws<ProtocolValidationException>(() =>
            reservedView.CreateSnapshot("scope.instance", 8));
        Assert.True(reservedException.Errors.Any(error => error.Code == "reserved_button"),
            $"Dashboard button {reserved} must remain host-owned.");
    }
    return Task.CompletedTask;
}

static Task UnsafeImageSourcesAreRejected()
{
    foreach (var source in new[]
             {
                 "http://images.example/cover.jpg",
                 "file:///C:/cover.jpg",
                 "javascript:alert(1)",
                 "https://user:secret@images.example/cover.jpg",
             })
    {
        var view = new WidgetView(UI.Stack("root", UI.Image(source, "cover", "Album cover")));
        var exception = Assert.Throws<ProtocolValidationException>(() => view.CreateSnapshot("test.instance", 0));
        Assert.True(exception.Errors.Any(error => error.Code == "invalid_image_source"), $"Expected '{source}' to be rejected.");
    }
    return Task.CompletedTask;
}

static Task VisualNodeRequirementsAreEnforced()
{
    var image = new WidgetView(UI.Stack("root", UI.Image("https://images.example/cover.jpg", "cover", "")));
    var imageException = Assert.Throws<ProtocolValidationException>(() => image.CreateSnapshot("test.instance", 0));
    Assert.True(imageException.Errors.Any(error => error.Path.EndsWith("accessibilityLabel", StringComparison.Ordinal)), "Expected image accessibility label validation.");

    var icon = new ViewSnapshot
    {
        Sequence = 0,
        WidgetInstanceId = "test.instance",
        ActiveInputScopeId = "icon",
        Root = new ViewNode
        {
            Id = "icon",
            Kind = ViewNodeKind.Icon,
            AccessibilityLabel = "Music",
        },
    };
    var iconErrors = ViewSnapshotValidator.Validate(icon);
    Assert.True(iconErrors.Any(error => error.Path == "$.root.glyph" && error.Code == "required"), "Expected icon glyph validation.");

    var unknownGlyph = icon with { Root = icon.Root with { Glyph = (WidgetGlyph)999 } };
    Assert.True(ViewSnapshotValidator.Validate(unknownGlyph).Any(error => error.Code == "invalid_glyph"), "Expected the closed glyph set to reject unknown numeric values.");
    var unknownFit = icon with
    {
        Root = icon.Root with
        {
            Kind = ViewNodeKind.Image,
            ImageSource = "https://images.example/cover.jpg",
            ImageFit = (ImageFit)999,
            Glyph = null,
        },
    };
    Assert.True(ViewSnapshotValidator.Validate(unknownFit).Any(error => error.Code == "invalid_image_fit"), "Expected unknown image fit values to be rejected.");
    return Task.CompletedTask;
}

static Task ButtonStatesRoundTrip()
{
    var snapshot = new WidgetView(
        UI.Row("root",
            UI.Button("Normal", "normal", "normal"),
            UI.Button("Unavailable", "disabled", "disabled").Disabled(),
            UI.Button("Current", "selected", "selected").Selected(),
            UI.Button("Saving", "busy", "busy").Busy()))
        .CreateSnapshot("test.instance", 7);

    var first = SnapshotJson.Serialize(snapshot);
    var second = SnapshotJson.Serialize(snapshot);
    Assert.SequenceEqual(first, second, "State serialization must be byte-for-byte deterministic.");
    var json = Encoding.UTF8.GetString(first);
    Assert.True(json.Contains("\"isDisabled\":true", StringComparison.Ordinal), "Disabled state was not serialized.");
    Assert.True(json.Contains("\"isSelected\":true", StringComparison.Ordinal), "Selected state was not serialized.");
    Assert.True(json.Contains("\"isBusy\":true", StringComparison.Ordinal), "Busy state was not serialized.");
    Assert.True(!json.Contains("\"isDisabled\":false", StringComparison.Ordinal), "Default false state should be omitted.");

    var restored = SnapshotJson.Deserialize(first);
    Assert.Equal(true, Find(restored.Root, "disabled").IsDisabled);
    Assert.Equal(true, Find(restored.Root, "selected").IsSelected);
    Assert.Equal(true, Find(restored.Root, "busy").IsBusy);
    Assert.Equal(null, Find(restored.Root, "normal").IsDisabled);
    return Task.CompletedTask;
}

static Task ButtonIconsRoundTrip()
{
    var snapshot = new WidgetView(
        UI.Row("root",
            UI.Button("Previous", "previous-track", "previous")
                .Icon(WidgetGlyph.Previous, "Previous track"),
            UI.Button("Pause", "toggle-playback", "play-pause")
                .Icon(WidgetGlyph.Pause)))
        .CreateSnapshot("test.instance", 8);

    var restored = SnapshotJson.Deserialize(SnapshotJson.Serialize(snapshot));
    Assert.Equal(WidgetGlyph.Previous, Find(restored.Root, "previous").Glyph);
    Assert.Equal("Previous track", Find(restored.Root, "previous").AccessibilityLabel);
    Assert.Equal(WidgetGlyph.Pause, Find(restored.Root, "play-pause").Glyph);

    var invalid = snapshot with
    {
        Root = snapshot.Root with
        {
            Children = [snapshot.Root.Children[0] with { Glyph = (WidgetGlyph)999 }],
        },
    };
    Assert.True(
        ViewSnapshotValidator.Validate(invalid).Any(error => error.Code == "invalid_glyph"),
        "Buttons must use the same closed semantic glyph set as icon nodes.");
    return Task.CompletedTask;
}

static Task SettingsCompositesAreSemantic()
{
    var snapshot = new WidgetView(
        UI.Stack("settings-root",
            UI.ToggleButton("Reduced motion", true, "toggle-motion", "motion-toggle"),
            UI.Stepper("Text scale", "110%", "text-smaller", "text-larger", "text-scale",
                canDecrement: false)),
        "motion-toggle").CreateSnapshot("settings.test", 1);

    var toggle = Find(snapshot.Root, "motion-toggle");
    Assert.Equal("Reduced motion: On", toggle.Text);
    Assert.Equal("Reduced motion, On", toggle.AccessibilityLabel);
    Assert.Equal(true, toggle.IsSelected);
    Assert.Equal(WidgetGlyph.Check, toggle.Glyph);
    Assert.Equal("setting-toggle", toggle.StyleClasses.Single());

    var decrement = Find(snapshot.Root, "text-scale.decrement");
    var increment = Find(snapshot.Root, "text-scale.increment");
    Assert.Equal(true, decrement.IsDisabled);
    Assert.Equal("text-scale.increment", decrement.Focus!.Right);
    Assert.Equal("text-scale.decrement", increment.Focus!.Left);
    Assert.Equal("Text scale: 110%", Find(snapshot.Root, "text-scale.value").AccessibilityLabel);
    return Task.CompletedTask;
}

static Task InvalidInteractionStatesAreRejected()
{
    var nonButton = new ViewSnapshot
    {
        Sequence = 0,
        WidgetInstanceId = "test.instance",
        ActiveInputScopeId = "status",
        Root = new ViewNode
        {
            Id = "status",
            Kind = ViewNodeKind.Text,
            Text = "Ready",
            IsDisabled = false,
        },
    };
    Assert.True(
        ViewSnapshotValidator.Validate(nonButton).Any(error => error.Code == "interaction_state_not_allowed"),
        "Even explicit false interaction state is invalid on a non-button node.");

    var unnamedStatefulButton = nonButton with
    {
        Root = new ViewNode
        {
            Id = "choice",
            Kind = ViewNodeKind.Button,
            ActionId = "choose",
            IsSelected = true,
        },
    };
    Assert.True(
        ViewSnapshotValidator.Validate(unnamedStatefulButton).Any(error => error.Code == "missing_accessible_name"),
        "A stateful button needs a visible or accessibility name.");
    return Task.CompletedTask;
}

static Task UnknownFieldsAreRejected()
{
    var snapshot = new WidgetView(UI.Stack("root")).CreateSnapshot("test.instance", 0);
    var json = Encoding.UTF8.GetString(SnapshotJson.Serialize(snapshot));
    var malformed = Encoding.UTF8.GetBytes(json.Insert(json.Length - 1, ",\"surprise\":true"));
    Assert.Throws<System.Text.Json.JsonException>(() => SnapshotJson.Deserialize(malformed));
    return Task.CompletedTask;
}

static Task NullCollectionsAreRejected()
{
    const string json = "{\"protocolVersion\":1,\"sequence\":0,\"widgetInstanceId\":\"test.instance\",\"activeInputScopeId\":\"root\",\"root\":{\"id\":\"root\",\"kind\":\"stack\",\"styleClasses\":null,\"shortcuts\":null,\"children\":null}}";
    var exception = Assert.Throws<ProtocolValidationException>(() => SnapshotJson.Deserialize(Encoding.UTF8.GetBytes(json)));
    Assert.True(exception.Errors.Any(error => error.Path.EndsWith("styleClasses", StringComparison.Ordinal)), "Expected null style classes to be rejected.");
    return Task.CompletedTask;
}

static Task ValidManifestPasses()
{
    var manifest = ValidManifest();
    Assert.Equal(0, WidgetManifestValidator.Validate(manifest).Count);
    var roundTrip = ManifestJson.Deserialize(ManifestJson.Serialize(manifest));
    Assert.Equal(manifest.Id, roundTrip.Id);
    return Task.CompletedTask;
}

static Task UnsafeManifestFails()
{
    var manifest = ValidManifest() with
    {
        Entrypoint = new WidgetEntrypoint("native-dll", "../escape.dll", "NoNamespace"),
        Permissions = ["network.client:https://bad value", "storage.own", "storage.own"],
        OptionalPermissions = ["storage.own"],
        ResourceRequest = new WidgetResourceRequest(1024, 1000),
    };
    var errors = WidgetManifestValidator.Validate(manifest);
    Assert.True(errors.Any(error => error.Code == "invalid_path"), "Expected invalid_path.");
    Assert.True(errors.Any(error => error.Code == "unsupported_runtime"), "Expected unsupported_runtime.");
    Assert.True(errors.Any(error => error.Code == "duplicate_permission"), "Expected duplicate_permission.");
    Assert.True(errors.Count >= 6, "Expected independent manifest failures.");
    return Task.CompletedTask;
}

static Task NullManifestCollectionsFail()
{
    var manifest = ValidManifest() with
    {
        Permissions = null!,
        OptionalPermissions = null!,
        Architectures = null!,
        ResourceRequest = null!,
    };
    var errors = WidgetManifestValidator.Validate(manifest);
    Assert.True(errors.Count(error => error.Code == "required") >= 4, "Expected null manifest properties to be reported.");
    return Task.CompletedTask;
}

static Task ClockRenders()
{
    var clock = new ClockWidget(new FixedTimeProvider(new DateTimeOffset(2026, 8, 7, 21, 5, 0, TimeSpan.Zero)));
    var snapshot = clock.Render().CreateSnapshot("clock.instance", 1);
    Assert.Equal("refresh", snapshot.InitialFocusId);
    var refresh = Find(snapshot.Root, "refresh");
    Assert.Equal(ControllerButton.X, refresh.Shortcuts.Single().Button);
    Assert.Equal("9:05 PM", Find(snapshot.Root, "clock-time").Text);
    return Task.CompletedTask;
}

static async Task ClockInvalidates()
{
    var clock = new ClockWidget();
    long observedRevision = 0;
    clock.Invalidated += (_, args) => observedRevision = args.Revision;
    await clock.OnActionAsync(new WidgetActionEvent("ignored", "source"));
    Assert.Equal(0L, observedRevision);
    await clock.OnActionAsync(new WidgetActionEvent("refresh", "refresh", ControllerButton.X));
    Assert.Equal(1L, observedRevision);
    Assert.Equal(1L, clock.Revision);
}

static async Task DashboardInputResolves()
{
    var widget = new RoutingWidget();
    _ = widget.RenderSnapshot("routing.instance", 1);
    await widget.SetActiveAsync(true, CancellationToken.None);
    var handled = await widget.OnControllerInputAsync(new ControllerInputEvent(
        ControllerButton.X,
        ControllerEventPhase.Pressed,
        ControllerInputContext.DashboardQuickAction,
        Sequence: 9,
        MonotonicTimestampMicroseconds: 1234));
    Assert.True(handled, "Expected dashboard quick action to resolve.");
    var action = await widget.NextActionAsync();
    Assert.Equal("quick-refresh", action.ActionId);
    Assert.Equal("dashboard-card", action.SourceElementId);
    Assert.Equal(9L, action.Sequence);
}

static async Task FocusedShortcutResolves()
{
    var widget = new RoutingWidget();
    _ = widget.RenderSnapshot("routing.instance", 1);
    await widget.SetActiveAsync(true, CancellationToken.None);
    var handled = await widget.OnControllerInputAsync(OpenInput(
        ControllerButton.RightBumper, "play", 1, "root"));
    Assert.True(handled, "Expected focused shortcut to resolve.");
    Assert.Equal("next", (await widget.NextActionAsync()).ActionId);

    var ignored = await widget.OnControllerInputAsync(OpenInput(
        ControllerButton.LeftTrigger, "play", 1, "root"));
    Assert.True(!ignored, "Undeclared input must remain widget-owned and unhandled by default.");
}

static async Task FocusedButtonActivates()
{
    var widget = new RoutingWidget();
    _ = widget.RenderSnapshot("routing.instance", 1);
    await widget.SetActiveAsync(true, CancellationToken.None);
    var handled = await widget.OnControllerInputAsync(OpenInput(
        ControllerButton.A, "play", 1, "root", inputSequence: 12));
    Assert.True(handled, "A Pressed should activate the focused button without a shortcut.");
    var action = await widget.NextActionAsync();
    Assert.Equal("play", action.ActionId);
    Assert.Equal("play", action.SourceElementId);
    Assert.Equal(12L, action.Sequence);
}

static async Task NonButtonDoesNotActivate()
{
    var widget = new RoutingWidget();
    _ = widget.RenderSnapshot("routing.instance", 1);
    await widget.SetActiveAsync(true, CancellationToken.None);
    var handled = await widget.OnControllerInputAsync(OpenInput(
        ControllerButton.A, "status", 1, "root"));
    Assert.True(!handled, "A must not synthesize an action for a non-button node with no action ID.");
    Assert.Equal(0, widget.Actions.Count);
}

static async Task DisabledAndBusyButtonsDoNotActivate()
{
    var widget = new StateRoutingWidget();
    _ = widget.RenderSnapshot("routing.instance", 1);
    await widget.SetActiveAsync(true, CancellationToken.None);

    foreach (var input in new[]
             {
                 OpenInput(ControllerButton.A, "disabled", 1, "root"),
                 OpenInput(ControllerButton.RightBumper, "disabled", 1, "root"),
                 OpenInput(ControllerButton.A, "busy", 1, "root"),
                 OpenInput(ControllerButton.X, "busy", 1, "root"),
             })
    {
        Assert.True(!await widget.OnControllerInputAsync(input), "Disabled or busy controls must not handle controller activation.");
    }
    Assert.Equal(0, widget.Actions.Count);

    var selectedHandled = await widget.OnControllerInputAsync(OpenInput(
        ControllerButton.A, "selected", 1, "root"));
    Assert.True(selectedHandled, "Selected is semantic state, not an activation lock.");
    Assert.Equal("select", (await widget.NextActionAsync()).ActionId);
}

static async Task ScopedShortcutRouting()
{
    var widget = new SurfaceRoutingWidget();
    _ = widget.RenderSnapshot("scope.instance", 1);
    await widget.SetActiveAsync(true, CancellationToken.None);

    Assert.True(await widget.OnControllerInputAsync(OpenInput(
        ControllerButton.LeftBumper, "root-focus", 1, "root")), "Root surface shortcut did not resolve.");
    Assert.Equal("root-back", (await widget.NextActionAsync()).ActionId);

    widget.SetSurface("dialog-window");
    _ = widget.RenderSnapshot("scope.instance", 2);
    Assert.True(!await widget.OnControllerInputAsync(OpenInput(
        ControllerButton.LeftBumper, "dialog-focus", 1, "dialog-window")),
        "Stale snapshot input must be rejected.");
    Assert.True(!await widget.OnControllerInputAsync(OpenInput(
        ControllerButton.LeftBumper, "dialog-focus", 2, "root")),
        "Input naming a non-active scope must be rejected.");
    Assert.True(!await widget.OnControllerInputAsync(OpenInput(
        ControllerButton.LeftBumper, "root-focus", 2, "dialog-window")),
        "Focus outside the active scope must be rejected.");
    Assert.True(await widget.OnControllerInputAsync(OpenInput(
        ControllerButton.LeftBumper, "dialog-focus", 2, "dialog-window")), "Nested surface shortcut did not resolve.");
    Assert.Equal("dialog-back", (await widget.NextActionAsync()).ActionId);
    Assert.True(await widget.OnControllerInputAsync(OpenInput(
        ControllerButton.B, null, 2, "dialog-window")),
        "A focusless modal should resolve its scope-container B shortcut.");
    Assert.Equal("dialog-close", (await widget.NextActionAsync()).ActionId);

    widget.SetSurface("empty-window");
    _ = widget.RenderSnapshot("scope.instance", 3);
    Assert.True(!await widget.OnControllerInputAsync(OpenInput(
        ControllerButton.LeftBumper, "empty-focus", 3, "empty-window")),
        "Nested surfaces must not bubble to their parent by default.");
}

static ControllerInputEvent OpenInput(
    ControllerButton button,
    string? focusedElementId,
    long snapshotSequence,
    string activeInputScopeId,
    long inputSequence = 0) => new(
        button,
        ControllerEventPhase.Pressed,
        ControllerInputContext.OpenWidget,
        focusedElementId,
        Sequence: inputSequence,
        ActiveInputScopeId: activeInputScopeId,
        SnapshotSequence: snapshotSequence);

static WidgetManifest ValidManifest() => new()
{
    Id = "org.gbar.samples.clock",
    Publisher = "org.gbar.samples",
    Name = "Clock",
    Version = "0.1.0",
    HostApi = new HostApiRange("1.0", 1),
    Entrypoint = new WidgetEntrypoint("dotnet-worker", "payload/ClockWidget.dll", "Example.ClockWidget"),
    Permissions = ["storage.own"],
};

static ViewNode Find(ViewNode node, string id)
{
    if (node.Id == id) return node;
    foreach (var child in node.Children)
    {
        var found = FindOrNull(child, id);
        if (found is not null) return found;
    }
    throw new InvalidOperationException($"Node '{id}' was not found.");
}

static ViewNode? FindOrNull(ViewNode node, string id)
{
    if (node.Id == id) return node;
    foreach (var child in node.Children)
    {
        var found = FindOrNull(child, id);
        if (found is not null) return found;
    }
    return null;
}

file sealed class FixedTimeProvider(DateTimeOffset value) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => value;
    public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
}

file sealed class RoutingWidget : Widget
{
    private readonly System.Threading.Channels.Channel<WidgetActionEvent> _observed =
        System.Threading.Channels.Channel.CreateUnbounded<WidgetActionEvent>();
    public List<WidgetActionEvent> Actions { get; } = [];

    public ValueTask<WidgetActionEvent> NextActionAsync() => _observed.Reader.ReadAsync();

    public override WidgetView Render() => new(
        UI.Stack("root",
            UI.Button("Play", "play", "play").Shortcut(ControllerButton.RightBumper, actionId: "next"),
            UI.Text("Ready", "status")),
        "play",
        [new WidgetQuickAction(ControllerButton.X, "quick-refresh", "Refresh")]);

    public override ValueTask OnActionAsync(
        WidgetActionEvent action,
        CancellationToken cancellationToken = default)
    {
        Actions.Add(action);
        _observed.Writer.TryWrite(action);
        return ValueTask.CompletedTask;
    }
}

file sealed class StateRoutingWidget : Widget
{
    private readonly System.Threading.Channels.Channel<WidgetActionEvent> _observed =
        System.Threading.Channels.Channel.CreateUnbounded<WidgetActionEvent>();
    public List<WidgetActionEvent> Actions { get; } = [];

    public ValueTask<WidgetActionEvent> NextActionAsync() => _observed.Reader.ReadAsync();

    public override WidgetView Render() => new(
        UI.Row("root",
            UI.Button("Unavailable", "disabled", "disabled").Disabled().Shortcut(ControllerButton.RightBumper),
            UI.Button("Saving", "busy", "busy").Busy().Shortcut(ControllerButton.X),
            UI.Button("Selected", "select", "selected").Selected()),
        "selected");

    public override ValueTask OnActionAsync(WidgetActionEvent action, CancellationToken cancellationToken = default)
    {
        Actions.Add(action);
        _observed.Writer.TryWrite(action);
        return ValueTask.CompletedTask;
    }
}

file sealed class SurfaceRoutingWidget : Widget
{
    private readonly System.Threading.Channels.Channel<WidgetActionEvent> _observed =
        System.Threading.Channels.Channel.CreateUnbounded<WidgetActionEvent>();

    public ValueTask<WidgetActionEvent> NextActionAsync() => _observed.Reader.ReadAsync();

    public string ActiveScope { get; private set; } = "root";
    public void SetSurface(string scopeId) => ActiveScope = scopeId;

    public override WidgetView Render() => new(
        UI.Stack("root",
            UI.Button("Root back", "root-back", "root-back").Shortcut(ControllerButton.LeftBumper),
            UI.Button("Root focus", "root-focus", "root-focus"),
            UI.Stack("dialog",
                UI.Button("Dialog back", "dialog-back", "dialog-back").Shortcut(ControllerButton.LeftBumper),
                UI.Button("Dialog focus", "dialog-focus", "dialog-focus"))
                .InputScope("dialog-window")
                .Shortcut(ControllerButton.B, "dialog-close"),
            UI.Stack("empty-dialog",
                UI.Button("Empty focus", "empty-focus", "empty-focus"))
                .InputScope("empty-window")),
        ActiveScope switch
        {
            "dialog-window" => "dialog-focus",
            "empty-window" => "empty-focus",
            _ => "root-focus",
        },
        ActiveInputScopeId: ActiveScope);

    public override ValueTask OnActionAsync(
        WidgetActionEvent action,
        CancellationToken cancellationToken = default)
    {
        _observed.Writer.TryWrite(action);
        return ValueTask.CompletedTask;
    }
}

file static class Assert
{
    public static void True(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    public static void Equal<T>(T expected, T actual)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new InvalidOperationException($"Expected '{expected}', got '{actual}'.");
    }

    public static void SequenceEqual(byte[] expected, byte[] actual, string message)
    {
        if (!expected.AsSpan().SequenceEqual(actual)) throw new InvalidOperationException(message);
    }

    public static T Throws<T>(Action action) where T : Exception
    {
        try
        {
            action();
        }
        catch (T exception)
        {
            return exception;
        }
        throw new InvalidOperationException($"Expected {typeof(T).Name}.");
    }
}
