using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using WidgetRail.OverlayFrontend.WinUI.Presentation;
using WidgetRail.WidgetBridge;
using WidgetRail.WidgetPresentationSession;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;
using WidgetRail.OverlayFrontend.WinUI.Media;

namespace WidgetRail.OverlayFrontend.WinUI.Shell;

internal sealed partial class OverlayShellPage
{
    private sealed class PinnedSurface(WidgetPinnedSelection? selection, WidgetViewPresenter? presenter,
        PinnedWidgetWindow window, PinnedPlacementLimits limits, PinnedMonitor monitor)
    {
        internal string ChoiceId { get; }=Guid.NewGuid().ToString("N");
        internal WidgetPinnedSelection? Selection { get; } = selection;
        internal WidgetViewPresenter? Presenter { get; } = presenter;
        internal WidgetMediaPresentation? Media;
        internal MediaFullscreenView? MediaView;
        internal Browser.BrowserPopupPresentation? Popup;
        internal ShellChromeStyles? MediaStyles;
        internal Func<bool>? MediaCurrent;
        internal string WidgetId => Selection?.WidgetId ?? Popup?.WidgetId ?? Media!.Document.Authority.WidgetId;
        internal string PopupPlacementId { get; init; }=PopupPinLayout;
        internal string LayoutId => Selection?.LayoutId ?? (Popup is null ? CompactMediaLayout : PopupPlacementId);
        internal bool IsCurrent => Selection?.IsCurrent ?? Popup?.IsCurrent ?? MediaCurrent?.Invoke() == true;
        internal void SetInput(bool enabled)
        {
            Presenter?.SetPresentationInputEnabled(enabled);
            Presenter?.SetAutomaticFocusEnabled(enabled);
            MediaView?.SetInteraction(enabled);
            if (Popup is { } popup) { popup.Slot.AcceptsInput = enabled; popup.Slot.PlacementChanged?.Invoke(); }
        }
        internal void Enter()
        { if (Presenter is { } widget) widget.Enter(restoreNativeFocus: true); else if (Popup is { } popup) { popup.Slot.Focus(FocusState.Keyboard); popup.Slot.Enter(); } else MediaView?.Enter(); }
        internal PinnedWidgetWindow Window { get; } = window;
        internal PinnedPlacementLimits Limits { get; set; } = limits;
        internal PinnedMonitor Monitor { get; set; } = monitor;
        private bool nativeForeground;
        internal Func<bool>? BorrowedBrowserFocus;
        internal bool HasForeground {get=>nativeForeground||BorrowedBrowserFocus?.Invoke()==true;set=>nativeForeground=value;}
        internal TaskCompletionSource? FocusAcquired;
        internal long Publication;
        internal WidgetSurfaceAppearance Appearance;
        internal bool PlacementRefreshQueued;
        internal PinnedPlacement? LogicalPlacement;
        internal string? DisplayId;
        internal long DisplayRevision;
        internal NativePopupTheme PopupTheme { get; } = new();
    }
    private const int MaximumPinnedSurfaces = BridgeProtocol.MaximumPinnedSurfaceCount;
    private readonly List<PinnedSurface> pins=[];
    // The selected surface owns interaction. All live surfaces receive updates.
    private PinnedSurface? pinned;
    private void RegisterPin(PinnedSurface current)
    {
        pins.Add(current);
        AttachPinnedKeyboardReturn(current);
        AttachPinnedForeground(current);
        if(!PinnedInteractionRequested && pinnedAdjustment is null)pinned=current;
    }
    private void ForgetPin(PinnedSurface current)
    {
        pins.Remove(current);
        if(ReferenceEquals(current,pinned))pinned=pins.LastOrDefault();
    }
    private string? pendingPinWidget;
    private PinnedPreferences pinnedPreferences = PinnedPreferences.Empty;
    private PinnedPreferencesStore? pinnedStore;
    private long pinIntent;
    internal event Action? ReturnFromPinnedRequested;
    private bool PinnedInputActive => pinned is { Window.Interactive: true, HasForeground: true };
    private bool CanInteractWithPin(PinnedSurface pin) => pin.IsCurrent && pin.Window.IsVisible &&
        (pin.Selection is null || owner?.Session.GetState(pin.WidgetId)?.LastGood is { } frame &&
            (pin.LayoutId == WidgetPinnedProjection.FullWidgetLayoutId ||
                frame.Snapshot.PinnedLayouts.Any(layout => layout.Id == pin.LayoutId && layout.Interaction == PinnedInteractionMode.Interactive)));
    private bool HasInteractivePins => pins.Any(CanInteractWithPin);
    internal void ReconcilePeerWindowOrder(nint backdrop = 0)
    {
        RefreshPinPointerAdmission();
        foreach(var pin in pins)if(!ReferenceEquals(pin,pinned))pin.Window.ReconcileOrder(backdrop);
        pinned?.Window.ReconcileOrder(backdrop);
        if(pinAdjustmentToolbar is {} toolbar)
        {
            toolbar.Window.ReconcileOrder(backdrop);
            if(pinnedAdjustment is {} adjustment)OverlayWindowFrame.EnsureAbove(toolbar.Window.Handle,adjustment.Surface.Window.Handle);
        }
        notificationBanner?.ReconcileOrder(backdrop);
    }
    private bool PinnedInteractionRequested => pinned?.Window.Interactive == true;
    private bool MainFocusEnabled => visible && interactive && foreground && !switching && activeWidget == requestedWidget &&
        !PinnedInteractionRequested && !PinnedAdjustmentActive && webAppEditor is null && !LocalInstallActive && !SystemFilePickerOpen && !HostChoiceActive;

    private WidgetLifecycleState LifecycleFor(string id)
    {
        var main = !retired && visible && (id == activeWidget || switching && preparingSurface?.Descriptor.Id == id);
        var pin = !retired && pins.Any(current=>current.Window.IsVisible && current.WidgetId==id);
        if (main && id == activeWidget && !switching && interactive && (foreground || SystemFilePickerOpen) && !PinnedInputActive || pin && PinnedInputActive && pinned?.WidgetId==id)
            return WidgetLifecycleState.Interactive;
        return main || pin || id == pendingPinWidget ? WidgetLifecycleState.Visible : WidgetLifecycleState.Background;
    }

    private async Task LoadPinnedPreferencesAsync()
    {
        pinnedStore = new(options.SettingsRoot);
        pinnedPreferences = await pinnedStore.LoadAsync(lifetime.Token);
    }

    private sealed record PinChoice(string Label, string AutomationId, string LayoutId, bool Media = false, bool Popup = false, Browser.BrowserSurface? PopupSource = null);

    private List<PinChoice> GetPinChoices(BridgeWidgetDescriptor descriptor)
    {
        var choices = new List<PinChoice>();
        if (descriptor.IsStopped) return choices;
        if (owner?.Session.GetState(descriptor.Id)?.LastGood is not { } frame) return choices;
        if (descriptor.FullWidgetPinningSupported && browserOwner is { } browsers)
        {
            var popups = browsers.PinnablePopups(descriptor.Id);
            for (var index = 0; index < popups.Length; index++)
                choices.Add(new(popups.Length == 1 ? "Pin additional page" : "Pin additional page: " + popups[index].PermissionTargetLabel,
                    "Pin.Popup." + index, PopupPinLayout, Popup: true, PopupSource: popups[index]));
        }
        if (descriptor.PinningSupported && mediaOwner?.CanPin(descriptor.Id) == true &&
            frame.Snapshot.EmbeddedMediaSession?.SupportedPresentations.Contains(MediaPresentationKind.CompactPinned) == true)
            choices.Add(new("Pin video", "Pin.Media", CompactMediaLayout, Media: true));
        if (descriptor.PinningSupported)
            foreach (var layout in frame.Snapshot.PinnedLayouts)
                choices.Add(new("Pin " + layout.Name, "Pin." + layout.Id, layout.Id));
        if (descriptor.FullWidgetPinningSupported && frame.Snapshot.EmbeddedMediaSession is null)
            choices.Add(new("Pin full widget", "Pin.Full", WidgetPinnedProjection.FullWidgetLayoutId));
        return choices;
    }

    private void AddPinnedCommands(BridgeWidgetDescriptor descriptor, Action<string, string, Func<Task>> add)
    {
        var choices = GetPinChoices(descriptor);
        if (pinned is { } current)
        {
            foreach(var choice in choices.Where(choice=>!pins.Any(pin=>pin.WidgetId==descriptor.Id && pin.LayoutId==choice.LayoutId &&
                (!choice.Popup || ReferenceEquals(pin.Popup?.Child,choice.PopupSource)))))
                add(choice.Label,choice.AutomationId,()=>ExecutePinChoiceAsync(descriptor.Id,choice));
            if (choices.Any(choice => current.WidgetId != descriptor.Id || choice.LayoutId != current.LayoutId || choice.Popup))
                add("Replace pin", "Pin.Replace", () => ChoosePinAsync("Replace pin", target => ReplacePinAsync(descriptor, target)));
            add(pins.Count>1 || current.Popup is null ? "Unpin widget" : "Return page to widget", "Pin.Remove",
                ()=>ChoosePinAsync("Unpin widget",pin=>UnpinAsync(save:true,pin),selectTarget:false));
            add("Adjust pin — position, size & opacity", "Pin.Adjust", ()=>ChoosePinAsync("Adjust pin",_=>BeginPinnedAdjustmentAsync()));
            if (HasInteractivePins)
                add("Interact with pinned widget", "Pin.Interact", ()=>ChoosePinAsync("Interact with pinned widget",_=>EnterPinnedAsync(),interactionOnly:true));
        }
        else
            foreach (var choice in choices)
                add(choice.Label, choice.AutomationId, () => ExecutePinChoiceAsync(descriptor.Id, choice));
    }

    private void ReportPinCapacity(Func<PinnedSurface,Task> replace)
    {
        _=ChoosePinAsync("All six pins are in use. Choose one to replace.",replace,selectTarget:false);
    }

    private async Task ChoosePinAsync(string title,Func<PinnedSurface,Task> action,bool selectTarget=true,bool interactionOnly=false,Func<PinnedSurface,bool>? eligible=null)
    {
        var candidates=pins.Where(pin=>pin.IsCurrent && pin.Window.IsVisible && (!interactionOnly || CanInteractWithPin(pin)) && eligible?.Invoke(pin)!=false).ToArray();
        if(candidates.Length==0)return;
        var target=candidates[0];
        if(candidates.Length>1)
        {
            var entries=candidates.Select(pin=>
            {
                var descriptor=catalogItems.FirstOrDefault(item=>item.Id==pin.WidgetId);
                var layout=owner?.Session.GetState(pin.WidgetId)?.LastGood?.Snapshot.PinnedLayouts.FirstOrDefault(item=>item.Id==pin.LayoutId);
                var label=layout?.Name??pin.Popup?.Child.PermissionTargetLabel??(pin.Media is null?"Full widget":"Media");
                return new HostDialogChoice(pin.ChoiceId,(descriptor?.Name??pin.WidgetId)+" · "+label);
            }).ToArray();
            PinnedSurface? outlined=null;
            string? choice;
            try
            {
                choice=await ShowHostChoiceAsync(title,"",entries,lifetime.Token,pinnedOwner:PinnedInputActive?pinned:null,configure:dialog=>
                {
                    dialog.ConfigurePreferenceList(pinned?.ChoiceId,"Cancel");
                    dialog.PreviewHoveredChoices=true;
                    dialog.SelectionContextChanged+=()=>
                    {
                        var next=candidates.FirstOrDefault(pin=>pin.ChoiceId==dialog.PreviewChoiceId && pins.Contains(pin) && pin.IsCurrent);
                        if(ReferenceEquals(next,outlined))return;
                        if(outlined is not null && pins.Contains(outlined))outlined.Window.SetPlacementActive(false);
                        outlined=next;
                        outlined?.Window.SetPlacementActive(true);
                    };
                });
            }
            finally { if(outlined is not null && pins.Contains(outlined))outlined.Window.SetPlacementActive(false); }
            if(candidates.FirstOrDefault(pin=>pin.ChoiceId==choice) is not { } chosen)return;
            target=chosen;
        }
        if(!pins.Contains(target)||!target.IsCurrent || interactionOnly && !CanInteractWithPin(target) || eligible?.Invoke(target)==false)return;
        if(selectTarget)
        {
            if(!ReferenceEquals(target,pinned))ExitPinnedInteraction(restoreMain:false);
            pinned=target;
        }
        await action(target);
    }

    private Task ExecutePinChoiceAsync(string widgetId, PinChoice choice, PinnedSurface? expected = null) =>
        choice.Popup ? PinBrowserPopupAsync(widgetId, expected, choice.PopupSource) : choice.Media ? PinMediaAsync(widgetId, expected) : PinAsync(widgetId, choice.LayoutId, expected);

    private async Task ReplacePinAsync(BridgeWidgetDescriptor descriptor, PinnedSurface expected)
    {
        var originalOwner = owner;
        var intent = pinIntent;
        var selection = selectionVersion;
        var visibleSession = visibleSince;
        bool Current() => !retired && visible && foreground && visibleSince == visibleSession && selectionVersion == selection &&
            ReferenceEquals(owner, originalOwner) && ReferenceEquals(pinned, expected) && pinIntent == intent &&
            activeWidget == descriptor.Id && requestedWidget == descriptor.Id &&
            owner?.Session.GetState(descriptor.Id)?.LastGood is { } latest && SameSurfaceOwner(descriptor, latest.Descriptor);
        if (!Current()) return;
        var choices = GetPinChoices(descriptor).Where(choice => expected.WidgetId != descriptor.Id ||
            expected.LayoutId != choice.LayoutId || choice.Popup).ToArray();
        if (choices.Length == 0) return;
        var closesOwnPage = expected.Popup is not null && expected.WidgetId == descriptor.Id;
        // Replace pin is already the user's explicit command. Only ask another
        // question when there is a real layout choice or an additional page to lose.
        if (choices.Length == 1 && !closesOwnPage)
        {
            if (Current()) await ExecutePinChoiceAsync(descriptor.Id, choices[0], expected);
            return;
        }
        var selected = await ShowHostChoiceAsync("Replace pin", closesOwnPage
                ? "Replacing this pin will close its additional page. To keep that page, cancel and choose Return page to widget."
                  : $"Choose a layout for {descriptor.Name}.",
                choices.Select(choice => new HostDialogChoice(choice.AutomationId, choice.Label)).ToArray(),
                  lifetime.Token,
                  configure: choices.Length > 1 ? dialog => dialog.ConfigurePreferenceList(closeLabel: "Cancel") : null,
                  primaryButtonText: choices.Length == 1 ? "Replace pin" : null);
        if (selected is null || !Current()) return;
        // Layouts and media readiness can change while the chooser is open.
        var choice = GetPinChoices(descriptor).FirstOrDefault(choice => choice.AutomationId == selected);
        if (choice is not null) await ExecutePinChoiceAsync(descriptor.Id, choice, expected);
    }

    private async Task RequestWidgetPinAsync(WidgetPresentationHostEffect effect)
    {
        var requestOwner=owner;
        var selection=selectionVersion;
        var visibleSession=visibleSince;
        var pinRevision=pinIntent;
        var widgetId=effect.Authority.WidgetId;
        var layoutId=effect.PinnedLayoutId;
        if(activeWidget!=widgetId && !(PinnedInputActive && pinned?.WidgetId==widgetId))return;
        bool Current() => requestOwner is not null && ReferenceEquals(owner,requestOwner) && !retired && visible &&
            !switching && !IsMediaFullscreen && visibleSince==visibleSession && selectionVersion==selection &&
            effect.InitiatedAtMilliseconds>=visibleSession && requestOwner.Session.IsHostEffectAuthorityCurrent(effect.Authority) &&
            requestOwner.Session.GetState(widgetId)?.LastGood is { Descriptor.PinningSupported:true };
        if(!Current() || HostChoiceActive || LocalInstallActive || SystemFilePickerOpen || PinnedAdjustmentActive)return;
        if(effect.Kind==WidgetHostEffectKind.ManagePinnedSurface)
        {
            try
            {
                if(layoutId is not null)
                {
                    var target=pins.FirstOrDefault(pin=>pin.WidgetId==widgetId && pin.LayoutId==layoutId &&
                        pin.Selection is {IsCurrent:true} selected && selected.SelectionId==effect.PinnedSelectionId);
                    if(target is not null)await ManagePinAsync(target,Current);
                }
                else if(pins.Any(pin=>pin.WidgetId==widgetId&&pin.IsCurrent&&pin.Window.IsVisible))
                    await ChoosePinAsync("Manage pin",target=>ManagePinAsync(target,Current),selectTarget:false,
                        eligible:pin=>Current()&&pin.WidgetId==widgetId);
                else
                    await ShowHostChoiceAsync("Manage pin","This widget has no open pins.",[],lifetime.Token);
            }
            catch(OperationCanceledException) when(retired) { }
            catch(Exception error) { if(Current())ReportFailure(error); }
            return;
        }
        if(layoutId is null)return;
        if(effect.Kind==WidgetHostEffectKind.ClosePinnedLayout)
        {
            var target=pins.FirstOrDefault(pin=>pin.WidgetId==widgetId && pin.LayoutId==layoutId &&
                pin.Selection is {IsCurrent:true} selected && selected.SelectionId==effect.PinnedSelectionId);
            if(target is not null)await UnpinAsync(save:true,target);
            return;
        }
        if(pins.Any(pin=>pin.WidgetId==widgetId&&pin.LayoutId==layoutId&&pin.IsCurrent))return;
        var failureCurrent=CapturePinIntentFailureGuard(pinRevision);
        try
        {
            // The action may have just republished a withdrawn layout. Refresh once
            // after the request rather than racing its asynchronous invalidation.
            // Keep exact worker/visibility/selection authority across the await.
            var frame = requestOwner!.Session.GetState(widgetId)?.LastGood;
            if (frame is null) return;
            await requestOwner.Session.RefreshAsync(frame.Authority,lifetime.Token);
            if(!Current())return;
            if (requestOwner.Session.GetState(widgetId)?.LastGood?.Snapshot.PinnedLayouts.Any(layout=>layout.Id==layoutId)!=true)return;
            await PinAsync(widgetId,layoutId,admission:Current);
        }
        catch(OperationCanceledException) when(retired) { }
        catch(Exception error) { ReportOperationFailure(error,failureCurrent); }
    }

    private async Task PinAsync(string widgetId, string layoutId, PinnedSurface? expected = null, Func<bool>? admission = null, bool closeReplacedPage = true,bool restoring=false)
    {
        if (owner is null || retired || admission?.Invoke()==false || expected is not null && !pins.Contains(expected)) return;
        var intent = pinIntent;
        var failureCurrent = CapturePinIntentFailureGuard(intent);
        try
        {
            await transitions.WaitAsync(lifetime.Token);
            try
            {
                intent=++pinIntent;
                failureCurrent = CapturePinIntentFailureGuard(intent);
                if (retired || admission?.Invoke()==false || expected is not null && !pins.Contains(expected)) return;
                pendingPinWidget = widgetId;
                var frame = await owner.Session.EstablishPresentationAsync(owner.Session.GetTarget(widgetId), LifecycleFor(widgetId), lifetime.Token);
                if (retired || intent != pinIntent || admission?.Invoke()==false) return;
                var projection = owner.Session.ResolvePinnedProjection(frame, layoutId);
                // Resolve first so a withdrawn layout cannot discard the current pin.
                if (expected is not null && !pins.Contains(expected)) return;
                // Additional pages remain intact while the new layout/window is prepared.
                // Ordinary layout selections still require retiring the old selection first.
                foreach (var stale in pins.Where(pin=>pin.WidgetId==widgetId && pin.LayoutId==layoutId && !pin.IsCurrent).ToArray())
                    await RemovePinnedCoreAsync(target:stale);
                if (retired || intent != pinIntent || admission?.Invoke()==false) return;
                if(pins.Any(pin=>pin.WidgetId==widgetId && pin.LayoutId==layoutId))return;
                var independent=frame.Snapshot.PinnedLayouts.Any(layout=>layout.Id==layoutId&&layout.IndependentSelection);
                var replacing=expected ?? (independent?null:pins.FirstOrDefault(pin=>pin.WidgetId==widgetId && pin.Selection is {Independent:false}));
                if(replacing is null && pins.Count>=MaximumPinnedSurfaces){if(!restoring)ReportPinCapacity(target=>PinAsync(widgetId,layoutId,target,admission,closeReplacedPage:false));return;}
                var outgoingPage=replacing?.Popup is not null?replacing:null;
                if(replacing is not null && outgoingPage is null)await RemovePinnedCoreAsync(target:replacing,forgetRemembered:true);
                if (retired || intent != pinIntent || admission?.Invoke()==false) return;
                if(owner.Session.GetState(widgetId)?.LastGood is { } refreshedFrame)
                    projection=owner.Session.ResolvePinnedProjection(refreshedFrame,layoutId);
                var selection = await owner.Session.SelectPinnedLayoutAsync(projection, cancellationToken: lifetime.Token);
                // Selection can synchronously publish its demanded content. Use
                // the latest genuine frame rather than the pre-selection shell.
                if (owner.Session.GetState(widgetId)?.LastGood is { } selectedFrame)
                    projection = owner.Session.ResolvePinnedProjection(selectedFrame, layoutId);
                WidgetViewPresenter? presenter = null;
                PinnedWidgetWindow? window = null;
                try
                {
                    if (retired || intent != pinIntent)
                    {
                        if (owner.Session.GetState(widgetId)?.LastGood is { } last)
                            await RevokePinSelectionAsync(selection, last);
                        return;
                    }
                    var hints = projection.Snapshot.Surface;
                    var saved = pinnedPreferences.Find(widgetId, layoutId);
                    window = new(frame.Descriptor.Name, (nint)hostWindow);
                    var prepared = await PreparePinnedPlacementAsync(window, hints, saved, outgoingPage);
                    if (retired || intent != pinIntent || !selection.IsCurrent || admission?.Invoke()==false)
                        throw new WidgetPresentationSessionException("pinned_input_stale", "Pinned placement owner retired.");
                    var (placement, limits, displayId) = prepared;
                    presenter = new() { NativeAudioChanged = ReportNativeAudio, Session = owner.Session, BrowserOwner = browserOwner };
                    presenter.SetAutomaticFocusEnabled(false);
                    presenter.SetPresentationInputEnabled(false);
                    await presenter.SetPresentationActiveAsync(false);
                    presenter.ApplyAppearance(AppearanceForDisplay(displayId), systemUi.AnimationsEnabled);
                    var surface = new PinnedSurface(selection, presenter, window, limits, placement.Monitor)
                    { Appearance = projection.Snapshot.Surface?.Appearance ?? WidgetSurfaceAppearance.Theme, DisplayId = displayId };
                    surface.PopupTheme.Attach(window.AutomationRoot);
                    window.ThemeChanged += () => ApplyPinnedAppearance(surface);
                    window.PlacementEnvironmentChanged += () => QueuePinnedPlacementEnvironment(surface);
                    presenter.EnsureInteractionAsync = (authority, token) => EnsurePinnedInteractionAsync(surface, authority, token);
                    presenter.DispatchActionAsync = request => InvokePinnedIntentAsync(surface, request);
                    presenter.Failed = error => { if (ReferenceEquals(pinned, surface)) ReportFailure(error); };
                    window.CloseRequested += () => _ = UnpinAsync(save: true, surface);
                    window.SetContent(presenter);
                    window.Place(placement.Bounds);
                    window.SetOpacity(saved?.OpacityPercent ?? 100);
                    surface.LogicalPlacement = PinnedPlacementPolicy.Capture(placement.Bounds, placement.Monitor, limits, layoutId, window.OpacityPercent);
                    presenter.ApplyPinned(selection, projection);
                    if (outgoingPage is not null)
                    {
                        if (retired || intent != pinIntent || !selection.IsCurrent || !pins.Contains(outgoingPage))
                            throw new WidgetPresentationSessionException("pinned_input_stale", "Pinned replacement owner retired.");
                        await RemovePinnedCoreAsync(closePage: closeReplacedPage && outgoingPage.WidgetId == widgetId && layoutId == WidgetPinnedProjection.FullWidgetLayoutId, target: outgoingPage);
                        if (retired || intent != pinIntent || !selection.IsCurrent)
                            throw new WidgetPresentationSessionException("pinned_input_stale", "Pinned replacement owner retired.");
                    }
                    RegisterPin(surface);
                    ApplyPinnedAppearance(surface);
                    window.Show();
                    await presenter.SetPresentationActiveAsync(true);
                    if (owner.Session.GetState(widgetId) is { } currentState) ApplyPinnedState(currentState);
                    // Display clamping/DPI rounding on startup is temporary.
                    // Preserve the user's durable bounds until an explicit edit.
                    if(saved is null)await SavePinnedAsync(surface);
                    if(!restoring&&surface.IsCurrent)await RememberPinAsync(frame.Descriptor,layoutId,
                        frame.Snapshot.PinnedLayouts.Any(layout=>layout.Id==layoutId&&layout.RestoreWhenAvailable));
                    UpdateDiagnostics();
                }
                catch
                {
                    if (presenter is not null && pins.FirstOrDefault(pin=>ReferenceEquals(pin.Presenter,presenter)) is { } failed)ForgetPin(failed);
                    try { if (presenter is not null) await presenter.DisposeAsync(); }
                    finally { window?.Dispose(); }
                    throw;
                }
            }
            catch
            {
                // A canceled/failed select can have reached the worker. Revoke
                // the session's recorded attempt before another pin can replace it.
                if (owner.Session.GetState(widgetId)?.LastGood is { } latest)
                {
                    try
                    {
                        if (owner.Session.GetPinnedSelection(latest,layoutId) is { } attempt && !pins.Any(pin=>ReferenceEquals(pin.Selection,attempt)))
                            await RevokePinSelectionAsync(attempt, latest);
                    }
                    catch (WidgetPresentationSessionException error) when (error.Code is "pinned_input_stale" or "presentation_stale" or "unknown_widget") { }
                }
                throw;
            }
            finally
            {
                pendingPinWidget = null;
                try
                {
                    if (!retired && !pins.Any(pin=>pin.WidgetId==widgetId)) await TryBackgroundAsync(widgetId);
                }
                finally { transitions.Release(); }
            }
        }
        catch (OperationCanceledException) when (retired) { }
        catch (WidgetPresentationSessionException error) when (error.Code is "pinned_input_stale" or "catalog_stale" or "unknown_widget") { }
        catch (Exception error) when(restoring) { System.Diagnostics.Trace.WriteLine("Remembered pin could not reopen: "+error.GetType().Name); }
        catch (Exception error) { ReportOperationFailure(error, failureCurrent); }
    }

    private async Task<bool> EnsurePinnedInteractionAsync(PinnedSurface surface, WidgetPresentationAuthority authority, CancellationToken token)
    {
        if (!Current()) return false;
        await transitions.WaitAsync(token);
        try
        {
            if (!Current() || owner is null) return false;
            var target = owner.Session.GetTarget(authority.WidgetId);
            if (target.Descriptor.InstanceId != authority.WidgetInstanceId || target.Descriptor.RuntimeGeneration != authority.RuntimeGeneration ||
                target.Descriptor.PresentationGeneration != authority.PresentationGeneration) return false;
            await owner.Session.SetLifecycleAsync(target, WidgetLifecycleState.Interactive, token);
            return Current();
        }
        finally { transitions.Release(); }
        bool Current() => !retired && visible && ReferenceEquals(pinned, surface) && CanInteractWithPin(surface) &&
            pinnedAdjustment is null && !savingPinnedAdjustment && surface.Window.Interactive && surface.HasForeground && authority.WidgetId == surface.WidgetId;
    }

    private async Task EnterPinnedAsync(bool pointerActivation=false)
    {
        if (pinned is not { } current || retired || !visible || (!foreground&&!pointerActivation) || !CanInteractWithPin(current) ||
            pinnedAdjustment is not null || savingPinnedAdjustment || current.FocusAcquired is not null) return;
        var requestOwner = owner;
        var visibleSession = visibleSince;
        var acquired = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var failureCurrent = CapturePinnedFailureGuard(current);
        ClearTrayFocus();
        surface?.SetAutomaticFocusEnabled(false);
        current.FocusAcquired = acquired;
        current.Window.SetInteraction(true);
        try
        {
            current.Window.Activate();
            if (!current.HasForeground) await acquired.Task.WaitAsync(TimeSpan.FromSeconds(2), lifetime.Token);
            if (!EntryCurrent()) return;
            if (!await SetPinnedLifecycleAsync(current) || !EntryCurrent() || !current.HasForeground)
            {
                if (EntryCurrent()) ExitPinnedInteraction(restoreMain: false);
                return;
            }
            current.SetInput(true);
            if (current.Media is not null) mediaOwner?.SetCompactInput(current.Media,true);
            current.Enter();
        }
        catch (OperationCanceledException) when (retired || !EntryCurrent()) { }
        catch (WidgetPresentationSessionException error) when (WidgetInputFailure.IsStale(error))
        { if (EntryCurrent()) ExitPinnedInteraction(restoreMain: false); }
        catch (Exception error)
        {
            var report = failureCurrent() && EntryCurrent();
            if (EntryCurrent()) ExitPinnedInteraction(restoreMain: false);
            ReportOperationFailure(error, () => report);
        }
        finally { if (ReferenceEquals(current.FocusAcquired, acquired)) current.FocusAcquired = null; }

        bool EntryCurrent() => !retired && visible && visibleSince == visibleSession &&
            ReferenceEquals(owner, requestOwner) && ReferenceEquals(pinned, current) && CanInteractWithPin(current) &&
            ReferenceEquals(current.FocusAcquired, acquired) && current.Window.Interactive &&
            pinnedAdjustment is null && !savingPinnedAdjustment;
    }

    private void ExitPinnedInteraction(bool restoreMain)
    {
        if (pinned is not { } current) return;
        DismissPinnedHostChoice(current);
        if (!current.Window.Interactive) return;
        PreserveReturnPointerOnFocusLoss();
        ClearBorrowedBrowserFocus(current);
        current.FocusAcquired?.TrySetCanceled();
        current.FocusAcquired = null;
        // Retain the widget's logical target before revoking input. The window
        // suppresses focused presentation without moving native/XAML focus.
        current.SetInput(false);
        if (current.Media is not null) mediaOwner?.SetCompactInput(current.Media,false);
        current.Presenter?.ResetPressedStyles(); current.Presenter?.DismissTransientControl();
        current.Window.SetInteraction(false);
        surface?.SetAutomaticFocusEnabled(MainFocusEnabled);
        _ = ReconcilePinnedLifecycleAsync();
        PublishControllerGuide();
        if (restoreMain && visible && !retired) ReturnFromPinnedRequested?.Invoke();
    }

    private async Task ReconcilePinnedLifecycleAsync()
    {
        if (pinned is not { } current) return;
        var failureCurrent = CapturePinnedFailureGuard(current);
        try
        {
            await SetPinnedLifecycleAsync(current);
        }
        catch (OperationCanceledException) when (retired) { }
        catch (WidgetPresentationSessionException error) when (WidgetInputFailure.IsStale(error)) { }
        catch (Exception error) { ReportOperationFailure(error, failureCurrent); }
    }

    // Entry awaits this throwing core. The fire-and-forget passive reconciliation
    // catches separately, so a failed lifecycle can never enable pinned input.
    private async Task<bool> SetPinnedLifecycleAsync(PinnedSurface current)
    {
        var requestOwner = owner;
        if (requestOwner is null || !Current()) return false;
        await transitions.WaitAsync(lifetime.Token);
        try
        {
            if (!Current()) return false;
            await requestOwner.Session.SetLifecycleAsync(requestOwner.Session.GetTarget(current.WidgetId), LifecycleFor(current.WidgetId), lifetime.Token);
            if (!Current()) return false;
            UpdateDiagnostics();
            return true;
        }
        finally { transitions.Release(); }
        bool Current() => !retired && ReferenceEquals(owner, requestOwner) &&
            pins.Contains(current) && current.IsCurrent;
    }

    private Func<bool> CapturePinIntentFailureGuard(long intent)
    {
        var presentationCurrent = CapturePresentationFailureGuard();
        return () => intent == pinIntent && presentationCurrent();
    }

    private Func<bool> CapturePinnedFailureGuard(PinnedSurface current)
    {
        var intentCurrent = CapturePinIntentFailureGuard(pinIntent);
        return () => intentCurrent() && ReferenceEquals(pinned, current) && current.IsCurrent;
    }

    private void ApplyPinnedState(WidgetPresentationState state)
    {
        if(retired || owner is null)return;
        foreach(var current in pins.Where(pin=>pin.WidgetId==state.WidgetId).ToArray())ApplyPinnedState(current,state);
    }
    private void ApplyPinnedState(PinnedSurface current,WidgetPresentationState state)
    {
        if(owner is null || state.PublicationRevision <= current.Publication)return;
        current.Publication = state.PublicationRevision;
        if (state.Failure is not null || !current.IsCurrent)
        { current.Window.Hide(); _ = UnpinAsync(save: false, current,preserveRemembered:true); return; }
        if (state.LastGood is not { } frame) return;
        if (current.Media is not null) { RefreshCompactView(); return; }
        if (current.Popup is not null) return;
        try
        {
            var projection = owner.Session.ResolvePinnedProjection(frame, current.LayoutId);
            if (projection.Interaction == PinnedInteractionMode.Passive && ReferenceEquals(pinned,current))
                ExitPinnedInteraction(restoreMain:true);
            current.Presenter!.ApplyPinned(current.Selection!, projection);
            var appearance = projection.Snapshot.Surface?.Appearance ?? WidgetSurfaceAppearance.Theme;
            if (current.Appearance != appearance) { current.Appearance = appearance; ApplyPinnedAppearance(current); }
        }
        catch (WidgetPresentationSessionException) { current.Window.Hide(); _ = UnpinAsync(save: false, current,preserveRemembered:true); }
        catch (Exception error) { current.Window.Hide(); ReportFailure(error); _ = UnpinAsync(save: false, current,preserveRemembered:true); }
    }

    private async Task UnpinAsync(bool save, PinnedSurface? expected = null,bool preserveRemembered=false)
    {
        expected ??= pinned;
        if(expected is null || !pins.Contains(expected))return;
        var intent = pinIntent;
        var requestOwner = owner;
        var failureCurrent = CapturePinIntentFailureGuard(intent);
        try
        {
            await transitions.WaitAsync(lifetime.Token);
            try
            {
                if (!ReferenceEquals(owner, requestOwner) || !pins.Contains(expected)) return;
                if(save){intent=++pinIntent;failureCurrent=CapturePinIntentFailureGuard(intent);}
                await RemovePinnedCoreAsync(target:expected,forgetRemembered:!preserveRemembered);

            }
            finally { transitions.Release(); QueueRememberedPins(); }
        }
        catch (OperationCanceledException) when (retired) { }
        catch (WidgetPresentationSessionException error) when (WidgetInputFailure.IsStale(error)) { }
        catch (Exception error) { ReportOperationFailure(error, failureCurrent); }
    }

    private async Task RemovePinnedCoreAsync(bool closePage = false,PinnedSurface? target=null,bool forgetRemembered=false)
    {
        var current=target??pinned;
        if(current is null || !pins.Contains(current))return;
        if(forgetRemembered&&current.Presenter?.CurrentBinding?.Frame.Descriptor is {} descriptor)
            await RememberPinAsync(descriptor,current.LayoutId,false);
        if (ReferenceEquals(hostChoicePin, current) || ReferenceEquals(managedPin, current)) hostChoiceDialog?.Hide();
        if (ReferenceEquals(intentPinnedTarget, current)) intentCancellation?.Cancel();
        if(ReferenceEquals(pinnedAdjustment?.Surface,current))CancelPinnedAdjustment();
        if(ReferenceEquals(pinned,current))ExitPinnedInteraction(restoreMain: !retired && visible);
        current.FocusAcquired?.TrySetCanceled();
        current.FocusAcquired = null;
        ForgetPin(current);
        try
        {
            if (current.Popup is { } popup && browserOwner is not null) await browserOwner.ReleasePopupPresentationAsync(popup, closePage);
            if (current.Media is not null && mediaOwner is not null) await mediaOwner.ReleaseCompactAsync(current.Media);
            current.Window.Hide();
            if (current.Presenter is { } presenter) await presenter.DisposeAsync();
        }
        finally { current.Popup?.Slot.Retire(); current.MediaStyles?.Dispose(); current.Window.Dispose(); }
        if (owner is not null && owner.Session.GetState(current.WidgetId)?.LastGood is { } frame)
        {
            if (current.Selection is { } selection) await RevokePinSelectionAsync(selection, frame);
            if (!retired) await owner.Session.SetLifecycleAsync(owner.Session.GetTarget(current.WidgetId), LifecycleFor(current.WidgetId), lifetime.Token);
        }
        UpdateDiagnostics();
    }

    private async Task SavePinnedAsync(PinnedSurface current, bool required = false,bool opacityOnly=false)
    {
        var placement=opacityOnly && pinnedPreferences.Find(current.WidgetId,current.LayoutId) is { } saved
            ? saved with {OpacityPercent=current.Window.OpacityPercent}
            : PinnedPlacementPolicy.Capture(current.Window.Bounds,current.Monitor,current.Limits,current.LayoutId,current.Window.OpacityPercent);
        var next=pinnedPreferences.WithPlacement(current.WidgetId,placement);
        if (required && pinnedStore is null) throw new InvalidOperationException("Pinned placement storage is unavailable.");
        if (pinnedStore is not null)
        {
            try { await pinnedStore.SaveAsync(next, lifetime.Token); }
            catch (Exception error) when (!required && error is (IOException or UnauthorizedAccessException))
            { System.Diagnostics.Trace.WriteLine("Pinned placement could not be saved: " + error.GetType().Name); return; }
        }
        pinnedPreferences = next;
    }

    private async Task RevokePinSelectionAsync(WidgetPinnedSelection selection, WidgetPresentationFrame frame)
    {
        if (owner is null) return;
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        try { await owner.Session.ClearPinnedSelectionAsync(selection, frame, cancellationToken: deadline.Token); }
        catch (OperationCanceledException) when (deadline.IsCancellationRequested)
        { System.Diagnostics.Trace.WriteLine("Pinned teardown acknowledgement timed out; local authority is retired."); }
        catch (WidgetPresentationSessionException error) when (error.Code is "pinned_input_stale" or "presentation_stale" or "unknown_widget") { }
    }

    private void ApplyPinnedAppearance(PinnedSurface current)
    {
        if (!pins.Contains(current)) return;
        mediaAudioPreferences?.Attach(current.Window.AutomationRoot);
        var appearance = AppearanceForDisplay(current.DisplayId);
        if (current.Window.InterfaceScale != appearance.InterfaceScale)
        {
            if(ReferenceEquals(pinnedAdjustment?.Surface,current))CancelPinnedAdjustment();
            if (owner?.Session.GetState(current.WidgetId)?.LastGood is { } frame)
            {
                var hints = current.Popup is not null ? PopupPinHints : current.Media is not null ? CompactMediaHints : owner.Session.ResolvePinnedProjection(frame, current.LayoutId).Snapshot.Surface;
                current.Limits = ResolvePinnedLimits(hints, appearance.InterfaceScale);
                ReconcilePinnedPlacementEnvironment(current);
            }
        }
        current.Window.InterfaceScale = appearance.InterfaceScale;
        current.Presenter?.ApplyAppearance(appearance, systemUi.AnimationsEnabled);
        current.MediaStyles?.Update(ShellPalette, appearance, systemUi.AnimationsEnabled);
        current.PopupTheme.Update(ShellPalette, appearance);
        var policy = OverlayAppearancePolicy.Resolve(appearance, current.WidgetId, current.Appearance, current.Window.SystemHighContrast);
        OverlaySurfacePaint.Apply(current.Window.SurfaceBackground, policy, ShellPalette);
        current.Window.ApplyFocusAppearance(ShellChromePalette.Resolve(ShellPalette, appearance));
    }

    private static PinnedPlacementLimits ResolvePinnedLimits(WidgetSurfaceHints? hints, double zoom)
    {
        var minimumWidth = Math.Max(240, (hints?.MinimumWidth ?? 240) * zoom);
        var minimumHeight = Math.Max(135, (hints?.MinimumHeight ?? 135) * zoom);
        return new(minimumWidth, minimumHeight, Math.Max(960, Math.Max(minimumWidth, (hints?.PreferredWidth ?? 0) * zoom)),
            Math.Max(540, Math.Max(minimumHeight, (hints?.PreferredHeight ?? 0) * zoom)));
    }

    private void ReconcilePinnedCatalog(WidgetPresentationCatalog catalog)
    {
        foreach(var current in pins.ToArray())ReconcilePinnedCatalog(catalog,current);
    }
    private void ReconcilePinnedCatalog(WidgetPresentationCatalog catalog,PinnedSurface current)
    {
        var descriptor = catalog.Widgets.FirstOrDefault(item => item.Id == current.WidgetId);
        if (!current.IsCurrent || descriptor?.IsStopped == true || descriptor is null && catalog.IsComplete ||
            descriptor is not null && current.Presenter?.CurrentBinding is { } binding && !SameSurfaceOwner(descriptor, binding.Frame.Descriptor))
        { current.Window.Hide(); _ = UnpinAsync(save: false, current,preserveRemembered:true); }
    }
}
