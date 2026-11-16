using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using WidgetRail.OverlayFrontend.WinUI.Presentation;
using WidgetRail.OverlayPlatformClient;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

namespace WidgetRail.OverlayFrontend.WinUI.Shell;

internal sealed partial class OverlayShellPage
{
    private sealed record PinnedAdjustment(PinnedSurface Surface, PinnedPlacementAdjustment Placement,
        long Selection, long VisibleSession, IReadOnlyList<PinnedMonitor> Monitors)
    {internal bool ControllerDriven{get;set;}}
    private PinnedAdjustment? pinnedAdjustment;
    private bool savingPinnedAdjustment;
    private bool restorePinnedAdjustmentFocus;
    private bool PinnedAdjustmentActive => pinnedAdjustment is not null || savingPinnedAdjustment;
    private readonly PinnedPlacementInput pinnedPlacementInput = new();
    private RawControllerState lastPlacementController;
    private PinnedAdjustmentToolbar? pinAdjustmentToolbar;

    private uint? adjustmentReturnPointer;
    private void InitializePinnedPlacement()
    {
        PreviewKeyDown += PinnedAdjustmentKeyDown;
        ProductionRoot.PointerReleased += CompleteAdjustmentReturnPointer;
        ProductionRoot.PointerCanceled += CompleteAdjustmentReturnPointer;
        ProductionRoot.PointerCaptureLost += CompleteAdjustmentReturnPointer;
    }

    private bool TryReturnFromAdjustment(PointerRoutedEventArgs args)
    {
        if(!PinnedAdjustmentActive)return false;
        var point=args.GetCurrentPoint(ProductionRoot).Position;
        // These host-owned regions stay drawn while adjustment revokes their
        // content input. Their clicks must not fall through as empty backdrop.
        bool Contains(FrameworkElement? region)=>region is {Visibility:Visibility.Visible,ActualWidth:>0,ActualHeight:>0,Opacity:>0}&&
            region.TransformToVisual(ProductionRoot).TransformBounds(new(0,0,region.ActualWidth,region.ActualHeight)).Contains(point);
        if(!(RadialOpen?Contains(radialView):Contains(WidgetSurface)&&surface?.Visibility==Visibility.Visible)&&!Contains(RailControls))return false;
        args.Handled=true;
        // Keep the complete return gesture at the host boundary: re-enabling
        // the widget must not deliver an orphan release to a newly active control.
        if(!ProductionRoot.CapturePointer(args.Pointer))return true;
        adjustmentReturnPointer=args.Pointer.PointerId;
        CancelPinnedAdjustment();
        return true;
    }

    private void CompleteAdjustmentReturnPointer(object sender,PointerRoutedEventArgs args)
    {
        if(adjustmentReturnPointer!=args.Pointer.PointerId)return;
        adjustmentReturnPointer=null;
        args.Handled=true;
        ProductionRoot.ReleasePointerCapture(args.Pointer);
    }
    private void PinnedAdjustmentKeyDown(object sender,KeyRoutedEventArgs args)
    {
        if (pinnedAdjustment is null && !savingPinnedAdjustment) return;
        if(!Input.GamepadKeyBoundary.IsGamepadKey(args.OriginalKey)&&pinnedAdjustment is {} keyboardAdjustment)
            keyboardAdjustment.ControllerDriven=false;
        if (!savingPinnedAdjustment && (args.Key is Windows.System.VirtualKey.Tab or Windows.System.VirtualKey.Space ||
            pinAdjustmentToolbar?.Opacity.FocusState!=FocusState.Unfocused&&pinAdjustmentToolbar is not null&&
                args.Key is Windows.System.VirtualKey.Left or Windows.System.VirtualKey.Right or Windows.System.VirtualKey.Up or Windows.System.VirtualKey.Down)) return;
        if(args.Key==Windows.System.VirtualKey.Enter&&pinAdjustmentToolbar is {} toolbar&&
            (toolbar.Save.FocusState!=FocusState.Unfocused||toolbar.Cancel.FocusState!=FocusState.Unfocused||toolbar.Close.FocusState!=FocusState.Unfocused))return;
        args.Handled = true;
        if (Input.GamepadKeyBoundary.IsGamepadKey(args.OriginalKey) || savingPinnedAdjustment) return;
        var direction = args.Key switch
        {
            Windows.System.VirtualKey.Left => PinnedPlacementDirection.Left,
            Windows.System.VirtualKey.Right => PinnedPlacementDirection.Right,
            Windows.System.VirtualKey.Up => PinnedPlacementDirection.Up,
            Windows.System.VirtualKey.Down => PinnedPlacementDirection.Down,
            _ => PinnedPlacementDirection.None,
        };
        if (direction != PinnedPlacementDirection.None)
        {
            var resize = (Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(Windows.System.VirtualKey.Shift) &
                Windows.UI.Core.CoreVirtualKeyStates.Down) != 0;
            StepPinnedPlacement(direction, resize);
        }
        else if (args.Key == Windows.System.VirtualKey.Enter) _ = FinishPinnedAdjustmentAsync(save: true);
        else if (args.Key == Windows.System.VirtualKey.Escape) CancelPinnedAdjustment();
    }

    private Task BeginPinnedAdjustmentAsync()
    {
        if (retired || !visible || !foreground || switching || IsMediaFullscreen || savingPinnedAdjustment ||
            pinned is not { } current || !current.IsCurrent) return Task.CompletedTask;
        CancelPinnedAdjustment();
        ExitPinnedInteraction(restoreMain: true);
        ReconcilePinnedPlacementEnvironment(current);
        if (!current.Window.IsVisible) return Task.CompletedTask;
        var placement = new PinnedPlacementAdjustment(current.Window.Bounds, current.Monitor, current.Limits,
            current.LayoutId, current.Window.OpacityPercent);
        pinnedAdjustment = new(current, placement, selectionVersion, visibleSince, PinnedDisplayAreas.Read());
        restorePinnedAdjustmentFocus = true;
        var state = lastPlacementController;
        pinnedPlacementInput.Prime(state.Buttons, state.LeftThumbX, state.LeftThumbY, state.RightThumbX, state.RightThumbY, Environment.TickCount64);
        rightStick.Reset(); trayHold.Reset(); radialInput.Reset();
        // Adjustment owns input without changing the underlying widget/radial
        // focus context. The wheel stays drawn and retains its selection.
        ClearTrayFocus();
        surface?.SetAutomaticFocusEnabled(false);
        surface?.SetPresentationInputEnabled(false);
        current.SetInput(false);
        current.Window.SetPlacementActive(true);
        try
        {
            current.Window.SetPointerPlacement((bounds,edge)=>PreviewPinnedPointerPlacement(current,bounds,edge),error=>
                {CancelPinnedAdjustment();ReportFailure(error);});
            OpenPinnedAdjustmentToolbar();
        }
        catch{CancelPinnedAdjustment();throw;}
        WidgetHost.IsHitTestVisible = Tray.IsHitTestVisible = false;
        RefreshRadialChooser(); UpdateTrayHelp(); UpdateDiagnostics();
        return Task.CompletedTask;
    }

    private IReadOnlyList<ControllerGuideHint> PinnedPlacementHints() =>
    (ControllerGuideHint[])[
        new(ControllerPrompt.LeftStickMove, "Move · D-pad", Required: true),
        new(ControllerPrompt.RightStickMove, "Resize", Required: true),
        new(ControllerPrompt.A, savingPinnedAdjustment ? "Saving…" : "Save", ControllerButton.A, Required: true),
        new(ControllerPrompt.B, "Cancel", ControllerButton.B, Required: true),
    ];

    // Called before any widget, rail or radial navigation. Even a mode-ending
    // sample belongs entirely to the adjustment; its input is never replayed.
    private bool ReceivePinnedPlacement(ControllerFrame frame)
    {
        lastPlacementController = frame.State;
        if (pinnedAdjustment is null && !savingPinnedAdjustment) return false;
        if (retired || !visible || !foreground ||
            pinnedAdjustment is { } pending && (!ReferenceEquals(pinned, pending.Surface) || !pending.Surface.IsCurrent))
        {
            if (frame.Connected == 0) { shellGestures.Reset(); trayHold.Reset(); radialInput.Reset(); }
            CancelPinnedAdjustment(); return true;
        }
        if(frame.Connected==0)
        {
            shellGestures.Reset();trayHold.Reset();radialInput.Reset();
            if(pinnedAdjustment?.ControllerDriven==true)CancelPinnedAdjustment();
            return true;
        }
        // This route runs before ReceiveCore's ordinary priming/gesture guards.
        // A capture-source handoff must not turn the menu-opening A into Save.
        if (frame.Primed != 0)
        {
            var primed = frame.State;
            pinnedPlacementInput.Prime(primed.Buttons, primed.LeftThumbX, primed.LeftThumbY,
                primed.RightThumbX, primed.RightThumbY, Environment.TickCount64);
            shellGestures.PrimeDigitalButtons(primed.Buttons);
            return true;
        }
        var savePressed = (frame.PressedButtons & 0x1000) != 0 && !shellGestures.Owns(ControllerButton.A);
        var cancelPressed = (frame.PressedButtons & 0x2000) != 0 && !shellGestures.Owns(ControllerButton.B);
        var lessOpaque=(frame.PressedButtons&0x0100)!=0&&!shellGestures.Owns(ControllerButton.LeftBumper);
        var moreOpaque=(frame.PressedButtons&0x0200)!=0&&!shellGestures.Owns(ControllerButton.RightBumper);
        var closePressed=(frame.PressedButtons&0x4000)!=0&&!shellGestures.Owns(ControllerButton.X);
        shellGestures.CaptureFrame(frame);
        if (savingPinnedAdjustment) return true;
        var sample = frame.State;
        var changes = pinnedPlacementInput.Sample(sample.Buttons, sample.LeftThumbX, sample.LeftThumbY,
            sample.RightThumbX, sample.RightThumbY, Environment.TickCount64);
        if(pinnedAdjustment is {} controllerAdjustment && (changes.Dpad!=PinnedPlacementDirection.None ||
            changes.Move!=PinnedPlacementDirection.None || changes.Resize!=PinnedPlacementDirection.None))
            controllerAdjustment.ControllerDriven=true;
        StepPinnedPlacement(changes.Dpad, resize: false);
        StepPinnedPlacement(changes.Move, resize: false);
        StepPinnedPlacement(changes.Resize, resize: true);
        if(lessOpaque)StepPinnedOpacity(PinnedPlacementDirection.Left);
        if(moreOpaque)StepPinnedOpacity(PinnedPlacementDirection.Right);
        if(closePressed){_ = CloseAdjustedPinAsync();return true;}
        if (savePressed) _ = FinishPinnedAdjustmentAsync(save: true);
        else if (cancelPressed) CancelPinnedAdjustment();
        return true;
    }

    private async Task<bool> RoutePinnedPlacementButtonAsync(ControllerButton button, ControllerEventPhase phase)
    {
        if (pinnedAdjustment is null && !savingPinnedAdjustment) return false;
        if (phase != ControllerEventPhase.Pressed) return true;
        shellGestures.Claim(button);
        if (savingPinnedAdjustment) return true;
        if (button == ControllerButton.A) await FinishPinnedAdjustmentAsync(save: true);
        else if (button == ControllerButton.B) CancelPinnedAdjustment();
        else if(button==ControllerButton.LeftBumper)StepPinnedOpacity(PinnedPlacementDirection.Left);
        else if(button==ControllerButton.RightBumper)StepPinnedOpacity(PinnedPlacementDirection.Right);
        else if(button==ControllerButton.X)await CloseAdjustedPinAsync();
        return true;
    }

    private void StepPinnedPlacement(PinnedPlacementDirection direction, bool resize)
    {
        if (pinnedAdjustment is not { } adjustment || !ReferenceEquals(pinned, adjustment.Surface) ||
            !adjustment.Surface.IsCurrent || direction == PinnedPlacementDirection.None) return;
        if (!adjustment.Placement.Step(direction, resize, adjustment.Monitors)) return;
        var changedMonitor = adjustment.Surface.Monitor.Id != adjustment.Placement.Monitor.Id;
        adjustment.Surface.Monitor = adjustment.Placement.Monitor;
        adjustment.Surface.LogicalPlacement = PinnedPlacementPolicy.Capture(adjustment.Placement.Current, adjustment.Surface.Monitor,
            adjustment.Surface.Limits, adjustment.Surface.LayoutId, adjustment.Surface.Window.OpacityPercent);
        adjustment.Surface.Window.Place(adjustment.Placement.Current);
        RefreshPinnedAdjustmentToolbar();
        if (changedMonitor) _ = RefreshPinnedDisplayAsync(adjustment.Surface);
        UpdateDiagnostics();
    }

    private PinnedBounds PreviewPinnedPointerPlacement(PinnedSurface current,PinnedBounds bounds,int edge)
    {
        if(pinnedAdjustment is not { } adjustment || !ReferenceEquals(adjustment.Surface,current) ||
            !ReferenceEquals(pinned,current) || !current.IsCurrent || retired || !visible || !foreground)
            return current.Window.Bounds;
        var next=adjustment.Placement.PreviewPointer(bounds,edge,adjustment.Monitors);
        adjustment.ControllerDriven=false;
        var changedMonitor=current.Monitor.Id!=adjustment.Placement.Monitor.Id;
        current.Monitor=adjustment.Placement.Monitor;
        current.LogicalPlacement=PinnedPlacementPolicy.Capture(next,current.Monitor,current.Limits,current.LayoutId,current.Window.OpacityPercent);
        RefreshPinnedAdjustmentToolbar();
        if(changedMonitor)_=RefreshPinnedDisplayAsync(current);
        UpdateDiagnostics();
        return next;
    }

    private void StepPinnedOpacity(PinnedPlacementDirection direction)
    {
        if (pinnedAdjustment is not { } adjustment || !ReferenceEquals(pinned, adjustment.Surface) ||
            !adjustment.Surface.IsCurrent || !adjustment.Placement.StepOpacity(direction)) return;
        var current = adjustment.Surface;
        current.Window.SetOpacity(adjustment.Placement.OpacityPercent);
        adjustment.ControllerDriven=true;
        RefreshPinnedAdjustmentToolbar();
        current.LogicalPlacement = PinnedPlacementPolicy.Capture(current.Window.Bounds, current.Monitor,
            current.Limits, current.LayoutId, current.Window.OpacityPercent);
        UpdateTrayHelp(); UpdateDiagnostics();
    }

    private void SetPinnedOpacity(int value)
    {
        if (pinnedAdjustment is not { } adjustment || !ReferenceEquals(pinned, adjustment.Surface) ||
            !adjustment.Surface.IsCurrent || !adjustment.Placement.SetOpacity(value)) return;
        adjustment.ControllerDriven=false;
        var current = adjustment.Surface;
        current.Window.SetOpacity(adjustment.Placement.OpacityPercent);
        RefreshPinnedAdjustmentToolbar();
        current.LogicalPlacement = PinnedPlacementPolicy.Capture(current.Window.Bounds, current.Monitor,
            current.Limits, current.LayoutId, current.Window.OpacityPercent);
        UpdateTrayHelp(); UpdateDiagnostics();
    }

    private async Task FinishPinnedAdjustmentAsync(bool save)
    {
        if (!save) { CancelPinnedAdjustment(); return; }
        if (pinnedAdjustment is not { } adjustment || savingPinnedAdjustment) return;
        savingPinnedAdjustment = true;
        RetirePinnedAdjustmentToolbar();
        adjustment.Surface.Window.SetPointerPlacement(null);
        // A is the commit boundary. Hide/deactivation after this point does not
        // reinterpret a requested save as Cancel while the atomic write finishes.
        pinnedAdjustment = null;
        var entered = false;
        try
        {
            await transitions.WaitAsync(lifetime.Token);
            entered = true;
            if (retired || !ReferenceEquals(pinned, adjustment.Surface) || !adjustment.Surface.IsCurrent) return;
            ReconcilePinnedPlacementEnvironment(adjustment.Surface);
            await SavePinnedAsync(adjustment.Surface, required: true, opacityOnly:!adjustment.Placement.GeometryChanged);
        }
        catch (Exception error) when (error is not (OutOfMemoryException or StackOverflowException or AccessViolationException))
        {
            if (ReferenceEquals(pinned, adjustment.Surface)) RestorePinnedPlacement(adjustment);
            if (!retired) ReportFailure(error);
        }
        finally
        {
            if (entered) transitions.Release();
            savingPinnedAdjustment = false;
            EndPinnedAdjustmentPresentation(adjustment);
            if (!retired) { UpdateTrayHelp(); UpdateDiagnostics(); }
        }
    }

    private void CancelPinnedAdjustment(bool restoreFocus = true)
    {
        // Also revoke a pending save's focus return. Saving remains atomic,
        // but completion after hide/deactivation must not restore native focus.
        if (!restoreFocus) restorePinnedAdjustmentFocus = false;
        if (pinnedAdjustment is not { } adjustment) return;
        RetirePinnedAdjustmentToolbar(restoreFocus);
        adjustment.Surface.Window.SetPointerPlacement(null);
        pinnedAdjustment = null;
        if (ReferenceEquals(pinned, adjustment.Surface)) RestorePinnedPlacement(adjustment);
        EndPinnedAdjustmentPresentation(adjustment);
        if (!retired) { UpdateTrayHelp(); UpdateDiagnostics(); }
    }

    private void RestorePinnedPlacement(PinnedAdjustment adjustment)
    {
        var current = adjustment.Surface;
        current.Window.SetOpacity(adjustment.Placement.Original.OpacityPercent);
        var placement = PinnedPlacementPolicy.Resolve(PinnedDisplayAreas.Read(), adjustment.Placement.Original, current.Limits);
        if (placement is null) { current.Window.Hide(); return; }
        current.Monitor = placement.Monitor;
        current.Window.Place(placement.Bounds);
        current.LogicalPlacement = PinnedPlacementPolicy.Capture(placement.Bounds, current.Monitor, current.Limits,
            current.LayoutId, current.Window.OpacityPercent);
    }

    private void EndPinnedAdjustmentPresentation(PinnedAdjustment adjustment)
    {
        var current = adjustment.Surface;
        if (ReferenceEquals(pinned, current))
        {
            current.Window.SetPlacementActive(false);
            current.SetInput(PinnedInputActive);
            _ = RefreshPinnedDisplayAsync(current);
        }
        WidgetHost.IsHitTestVisible = Tray.IsHitTestVisible = true;
        surface?.SetPresentationInputEnabled(!retired && visible && foreground && !switching && activeWidget == requestedWidget);
        surface?.SetAutomaticFocusEnabled(MainFocusEnabled);
        rightStick.Reset();
        RefreshRadialChooser();
        var restore=restorePinnedAdjustmentFocus && !retired && visible && foreground &&
            adjustment.Selection==selectionVersion && adjustment.VisibleSession==visibleSince;
        if(RestorePinManagementAdjustmentFocus(allowFocus:restore)||!restore)return;
        if (RadialOpen) radialView?.FocusSelected();
        else QueueEntryFocus();
    }

    private void QueuePinnedPlacementEnvironment(PinnedSurface current)
    {
        ++current.DisplayRevision; // Invalidate in-flight reads before deferred layout reconciliation.
        if (current.PlacementRefreshQueued || retired) return;
        current.PlacementRefreshQueued = true;
        DispatcherQueue.TryEnqueue(() =>
        {
            current.PlacementRefreshQueued = false;
            if (retired || !pins.Contains(current)) return;
            try
            {
                if (pinnedAdjustment is { } adjustment && ReferenceEquals(adjustment.Surface, current) &&
                    !PinnedDisplayAreas.Read().OrderBy(monitor => monitor.Id, StringComparer.Ordinal)
                        .SequenceEqual(adjustment.Monitors.OrderBy(monitor => monitor.Id, StringComparer.Ordinal))) CancelPinnedAdjustment();
                ReconcilePinnedPlacementEnvironment(current);
                _ = RefreshPinnedDisplayAsync(current);
            }
            catch (Exception error) { ReportFailure(error); }
        });
    }

    private void ReconcilePinnedPlacementEnvironment(PinnedSurface current)
    {
        // WM_DPICHANGED may already have resized the HWND before this queued
        // reconciliation runs. Reuse the last logical placement rather than
        // interpreting those new physical pixels with the preceding monitor DPI.
        var saved = current.LogicalPlacement ?? PinnedPlacementPolicy.Capture(current.Window.Bounds, current.Monitor, current.Limits,
            current.LayoutId, current.Window.OpacityPercent);
        var placement = PinnedPlacementPolicy.Resolve(PinnedDisplayAreas.Read(), saved, current.Limits);
        if (placement is null) { current.Window.Hide(); return; }
        current.Monitor = placement.Monitor;
        current.Window.Place(placement.Bounds);
        current.LogicalPlacement = PinnedPlacementPolicy.Capture(placement.Bounds, current.Monitor, current.Limits,
            current.LayoutId, current.Window.OpacityPercent);
    }
}
