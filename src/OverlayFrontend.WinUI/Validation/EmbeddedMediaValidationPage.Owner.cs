using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using WidgetRail.OverlayFrontend.WinUI.Media;
using WidgetRail.OverlayFrontend.WinUI.Presentation;
using WidgetRail.WidgetBridge;
using WidgetRail.WidgetProtocol;

namespace WidgetRail.OverlayFrontend.WinUI.Validation;

internal sealed partial class EmbeddedMediaValidationPage
{
    private EmbeddedMediaOwner? mediaOwner;
    private WidgetViewPresenter? ownerPresenter;
    private bool ownerChecks;
    private int treeRevision;
    private bool coverActivation;
    private int nativePlayActions;
    private Grid? parkingLayer;
    private static BridgeWidgetDescriptor[] FixtureDescriptors() => Enumerable.Range(0, 5)
        .Select(index => index == 0 ? Descriptor : Descriptor with { Id = "media-extra-" + index, InstanceId = "media-extra-" + index + ".instance" }).ToArray();

    private ViewNode OwnerRoot() => new()
    {
        Id = "layout-" + treeRevision, InputScopeId = "page", Kind = ViewNodeKind.Stack,
        Children = (ViewNode[])[new ViewNode { Id = "native.commands", Kind = ViewNodeKind.Row, Children = (ViewNode[])[
            new() { Id = "native.play", Kind = ViewNodeKind.Button, Text = "Play", ActionId = "native.play" },
            new() { Id = "native.fullscreen", Kind = ViewNodeKind.Button, Text = "Fullscreen", ActionId = WidgetRail.WidgetPresentationSession.WidgetPresentationSession.EnterMediaFullscreenAction },
            new() { Id = "native.options", Kind = ViewNodeKind.Select, Text = "Options", AccessibilityLabel = "Options", AccessibilityValue = "First",
                SelectOptions = (WidgetSelectOption[])[new("first", "First", "pick.first", IsSelected: true), new("second", "Second", "pick.second")] }] },
            .. (declaration is null || parked ? Array.Empty<ViewNode>() : [new ViewNode { Id = "viewport-" + treeRevision, Kind = ViewNodeKind.MediaViewport,
                MediaSessionId = declaration.Id, AccessibilityLabel = declaration.AccessibleName }])],
    };

    private async Task RunOwnerChecksAsync(EmbeddedMediaSession definition)
    {
        ownerChecks = true;
        viewport.Children.Clear();
        parkingLayer = new();
        ((StackPanel)Content).Children.Add(parkingLayer);
        mediaOwner = new(session!, parkingLayer)
        {
            Diagnostic = (_, code) =>
            {
                diagnostics.Add(code);
                if (coverActivation && code == "media-activation-requested")
                {
                    coverActivation = false;
                    var button = OwnerElement<Button>("Widget.native.options");
                    button.Focus(FocusState.Keyboard);
                    ownerPresenter!.ActivateFocused();
                }
            },
        };
        ownerPresenter = new() { Session = session, MediaOwner = mediaOwner, Failed = error => throw error };
        ownerPresenter.DispatchActionAsync = async request =>
        {
            if (request.Action.ActionId != "native.play") return;
            ++nativePlayActions;
            declaration = declaration! with { PendingCommand = new() { Sequence = 2, Kind = EmbeddedMediaPlaybackCommandKind.Play, MediaKey = "fixture" } };
            ownerPresenter.Apply(await SnapshotAsync());
        };
        viewport.Children.Add(ownerPresenter);
        declaration = definition with { Id = "owner-player", PendingCommand = new() { Sequence = 1, Kind = EmbeddedMediaPlaybackCommandKind.Load, MediaKey = "fixture" } };
        parked = false;
        ownerPresenter.Apply(await SnapshotAsync());
        mediaOwner.SetHostState(Descriptor.Id, true, true);
        await Until(() => mediaOwner.IsReady(Descriptor.Id) && events.Any(value => value.SessionId == "owner-player" && value.CommandSequence == 1));
        var initialBrowser = OwnerBrowser();
        var slot = OwnerElement<WidgetMediaViewport>("Widget.viewport-0");
        Check(!slot.IsTabStop && !slot.IsHitTestVisible && !initialBrowser.IsTabStop && !initialBrowser.IsHitTestVisible,
            "production MediaViewport preserves pixel-only SDK focus and pointer semantics");
        Check(slot.ActualWidth > 0 && slot.ActualHeight > 0 && initialBrowser.ActualWidth <= slot.ActualWidth && initialBrowser.ActualHeight <= slot.ActualHeight,
            "native layout constrains the real WebView2 to its declared viewport");
        OwnerElement<Button>("Widget.native.play").Focus(FocusState.Keyboard);
        await ownerPresenter.HandleControllerButtonAsync(ControllerButton.A);
        await ownerPresenter.HandleControllerButtonAsync(ControllerButton.A, ControllerEventPhase.Released);
        await Until(() => events.Any(value => value.SessionId == "owner-player" && value.CommandSequence == 2));
        Check(nativePlayActions == 1 && events.Last(value => value.SessionId == "owner-player" && value.CommandSequence == 2).State == EmbeddedMediaPlaybackState.Playing,
            "normalized A activates the authored native Play control exactly once before typed media playback");

        var creations = mediaOwner.BrowserCreationCount;
        var resolutions = resolveCount;
        declaration = declaration with { PendingCommand = new() { Sequence = 3, Kind = EmbeddedMediaPlaybackCommandKind.SetVolume, MediaKey = "fixture", Volume = .4 } };
        ownerPresenter.Apply(await SnapshotAsync());
        await Until(() => events.Any(value => value.SessionId == "owner-player" && value.CommandSequence == 3));
        Check(ReferenceEquals(initialBrowser, OwnerBrowser()) && mediaOwner.BrowserCreationCount == creations && resolveCount == resolutions,
            "compatible command snapshots preserve the native browser and sealed resource admission");
        mediaOwner.SetHostState(Descriptor.Id, false, false);
        await ownerPresenter.SetPresentationActiveAsync(false);
        Check(!slot.Retired && !slot.AcceptsInput && mediaOwner.IsParked(Descriptor.Id),
            "presenter suspension detaches placement without terminally retiring the native media viewport");
        await ownerPresenter.SetPresentationActiveAsync(true);
        mediaOwner.SetHostState(Descriptor.Id, true, true);
        await Until(() => slot.SurfaceHost.Children.Contains(initialBrowser));
        Check(ReferenceEquals(initialBrowser, OwnerBrowser()) && mediaOwner.BrowserCreationCount == creations && resolveCount == resolutions,
            "presenter resume reattaches the same viewport and browser without resource readmission");
        await RunFullscreenOwnerChecksAsync();
        ++treeRevision;
        ownerPresenter.Apply(await SnapshotAsync());
        await Until(() => Descendants(ownerPresenter).OfType<WidgetMediaViewport>().Any(value =>
            AutomationProperties.GetAutomationId(value) == "Widget.viewport-1" && value.SurfaceHost.Children.Contains(initialBrowser)));
        Check(mediaOwner.IsReady(Descriptor.Id) && mediaOwner.BrowserCreationCount == creations,
            "replacement presentation trees borrow the same durable browser through shell parking");

        coverActivation = true;
        declaration = declaration with { PendingCommand = new() { Sequence = 4, Kind = EmbeddedMediaPlaybackCommandKind.Play, MediaKey = "fixture" } };
        ownerPresenter.Apply(await SnapshotAsync());
        await Until(() => events.Any(value => value.SessionId == "owner-player" && value.CommandSequence == 4 && value.ErrorCode == "activation-canceled"));
        await Until(() => mediaOwner.IsReady(Descriptor.Id) && events.Last().CommandSequence == 0);
        Check(ownerPresenter.HasTransientControl && mediaOwner.BrowserCreationCount == creations &&
            events.Last().State == EmbeddedMediaPlaybackState.Playing,
            "native popup cancels an armed Play gesture while the existing audio and document remain alive");
        Write(new { passed = true, phase = "owner-popup", checks, diagnostics });
        await Task.Delay(1200);
        ownerPresenter.DismissTransientControl();
        OwnerElement<Button>("Widget.native.play").Focus(FocusState.Keyboard);
        mediaOwner.Refresh();
        await Task.Delay(150);
        Check(events.Count(value => value.SessionId == "owner-player" && value.CommandSequence == 4) == 1,
            "closing the popup does not replay the canceled Play command");

        mediaOwner.SetHostState(Descriptor.Id, false, false);
        Check(mediaOwner.IsParked(Descriptor.Id) && parkingLayer.Children.Contains(initialBrowser), "overlay hide parks the browser in the shell-owned layer");
        declaration = declaration with { PendingCommand = new() { Sequence = 5, Kind = EmbeddedMediaPlaybackCommandKind.SetVolume, MediaKey = "fixture", Volume = .2 } };
        await SnapshotAsync(); // Deliberately do not apply this hidden publication to the presenter.
        await Until(() => events.Any(value => value.SessionId == "owner-player" && value.CommandSequence == 5));
        Check(mediaOwner.IsReady(Descriptor.Id) && mediaOwner.IsParked(Descriptor.Id), "hidden publications still reach parked media independently of active presenter filtering");
        ownerPresenter.Apply(session!.GetState(Descriptor.Id)!.LastGood!);
        mediaOwner.SetHostState(Descriptor.Id, true, true);
        Check(ReferenceEquals(initialBrowser, OwnerBrowser()) && mediaOwner.BrowserCreationCount == creations, "overlay reopening restores the same browser without navigation or command replay");

        declaration = declaration with { PendingCommand = new() { Sequence = 6, Kind = EmbeddedMediaPlaybackCommandKind.Pause, MediaKey = "fixture" } };
        ownerPresenter.Apply(await SnapshotAsync());
        await Until(() => events.Any(value => value.SessionId == "owner-player" && value.CommandSequence == 6));
        ownerPresenter.SetPresentationInputEnabled(false);
        mediaOwner.SetHostState(Descriptor.Id, true, false, acceptsDashboardPlayback: true);
        declaration = declaration with { PendingCommand = new() { Sequence = 7, Kind = EmbeddedMediaPlaybackCommandKind.Play, MediaKey = "fixture" } };
        ownerPresenter.Apply(await SnapshotAsync());
        await Until(() => events.Any(value => value.SessionId == "owner-player" && value.CommandSequence == 7 && value.State == EmbeddedMediaPlaybackState.Playing));
        Check(!slot.AcceptsInput && !initialBrowser.IsHitTestVisible && ReferenceEquals(initialBrowser, OwnerBrowser()),
            "dashboard playback activates the existing visible media without granting widget or browser input");
        ownerPresenter.SetPresentationInputEnabled(true);
        mediaOwner.SetHostState(Descriptor.Id, true, true);

        var invalidModal = session.GetState(Descriptor.Id)!.LastGood!.Snapshot with
        {
            ActiveInputScopeId = "dialog", Root = new() { Id = "unsupported-modal", Kind = ViewNodeKind.ModalLayer,
                Children = (ViewNode[])[OwnerRoot(), new() { Id = "dialog", InputScopeId = "dialog", Kind = ViewNodeKind.Stack }] },
        };
        Check(ViewSnapshotValidator.Validate(invalidModal).Any(error => error.Code == "unsupported_modal_surface"),
            "widget-local modal plus embedded media remains rejected by the unchanged SDK contract");

        // A prohibited top-level navigation is canceled by the native adapter and
        // faults this exact document. No external request is admitted by its policy.
        var failureNotifications = 0;
        mediaOwner.FailureChanged = _ => ++failureNotifications;
        await FaultFullscreenOwnerAsync(initialBrowser);
        await Until(() => mediaOwner.GetFailure(Descriptor.Id) is not null);
        var failureCode = mediaOwner.GetFailure(Descriptor.Id);
        Check(failureCode == "media-navigation-failed" && !mediaOwner.IsReady(Descriptor.Id),
            "native browser failure becomes persistent owner error state instead of a silent blank viewport");
        ownerPresenter.Apply(await SnapshotAsync());
        mediaOwner.Refresh();
        Check(mediaOwner.GetFailure(Descriptor.Id) == failureCode && mediaOwner.BrowserCreationCount == creations && failureNotifications == 1,
            "ordinary publications retain media failure without browser recreation or repeated error notification");
        declaration = null;
        ownerPresenter.Apply(await SnapshotAsync());
        await Until(() => mediaOwner.ResidentCount == 0);
        Check(mediaOwner.GetFailure(Descriptor.Id) is null, "document retirement clears only its owned media error");
        declaration = definition with { Id = "owner-player", PendingCommand = null };
        ownerPresenter.Apply(await SnapshotAsync());
        await Until(() => mediaOwner.IsReady(Descriptor.Id));
        creations = mediaOwner.BrowserCreationCount;
        Check(mediaOwner.GetFailure(Descriptor.Id) is null && !ReferenceEquals(initialBrowser, OwnerBrowser()),
            "explicit document recovery creates one fresh browser without replaying a consumed playback command");
        if (Environment.GetCommandLineArgs().Contains("--validate-media-process-failures"))
        {
            await CheckNativeGpuRecoveryAsync();
            await CheckNativeBrowserRecoveryAsync(definition);
            creations = mediaOwner.BrowserCreationCount;
        }

        // Four actual native controllers may remain parked; a fifth never evicts audio.
        declaration = definition with { Id = "owner-player", PendingCommand = null };
        foreach (var descriptor in FixtureDescriptors().Skip(1))
        {
            mediaOwner.SetHostState(descriptor.Id, true, true);
            ownerPresenter.Apply(await SnapshotAsync(descriptor.Id));
            if (descriptor.Id != "media-extra-4") await Until(() => mediaOwner.IsReady(descriptor.Id));
        }
        await Task.Delay(150);
        Check(mediaOwner.ResidentCount == 4 && mediaOwner.BrowserCreationCount == creations + 3 && diagnostics.Contains("media-session-capacity"),
            "resident capacity counts real parked controllers and rejects a fifth without eviction");
        var liveDocument = await session!.ResolveEmbeddedMediaAsync(session.GetState(Descriptor.Id)!.LastGood!.Authority);
        for (var pass = 0; pass < 8; ++pass)
        {
            var retiringBrowser = new EmbeddedMediaSurface(session, liveDocument);
            viewport.Children.Add(retiringBrowser.Element);
            retiringBrowser.UpdatePresentation(true, false);
            // Yield a native layout/Loaded opportunity, then retire while its
            // controller creation or resource callbacks may still be pending.
            await Task.Delay(pass % 2 == 0 ? 1 : 35);
            await retiringBrowser.DisposeAsync();
            viewport.Children.Remove(retiringBrowser.Element);
            Check(retiringBrowser.IsRetired, "early browser retirement drains native initialization " + (pass + 1));
        }
        declaration = null;
        await SnapshotAsync(Descriptor.Id); // Current presenter belongs to a different widget.
        await Until(() => mediaOwner.ResidentCount < 4 || mediaOwner.IsReady("media-extra-4"));
        await Until(() => mediaOwner.IsReady("media-extra-4"));
        Check(mediaOwner.ResidentCount == 4 && mediaOwner.BrowserCreationCount == creations + 4,
            "inactive declaration removal retires its controller and admits the waiting visible widget");
        await ownerPresenter.DisposeAsync(); ownerPresenter = null;
        Check(parkingLayer.Children.Count == 4, "presenter disposal parks all media without ending shell-owned sessions");
        await mediaOwner.DisposeAsync();
        Check(parkingLayer.Children.Count == 0 && mediaOwner.ResidentCount == 0, "shell owner teardown awaits all browser admissions, observations and native controller closures");
        mediaOwner = null;
    }

    private T OwnerElement<T>(string id) where T : FrameworkElement => Descendants(ownerPresenter!).OfType<T>()
        .Single(value => AutomationProperties.GetAutomationId(value) == id);
    private WebView2 OwnerBrowser() => Descendants(ownerPresenter!).OfType<WebView2>().Single();
    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        yield return root;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); ++i)
            foreach (var child in Descendants(VisualTreeHelper.GetChild(root, i))) yield return child;
    }
}
