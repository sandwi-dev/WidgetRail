using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using WidgetRail.OverlayFrontend.WinUI.Presentation;
using WidgetRail.OverlayPlatformClient;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

namespace WidgetRail.OverlayFrontend.WinUI.Shell;

internal sealed partial class OverlayShellPage
{
    private sealed record PinnedAdjustment(PinnedSurface Surface, PinnedPlacementAdjustment Placement, bool OpacityOnly,
        long Selection, long VisibleSession);
    private PinnedAdjustment? pinnedAdjustment;
    private bool savingPinnedAdjustment;
    private bool restorePinnedAdjustmentFocus;
    private bool PinnedAdjustmentActive => pinnedAdjustment is not null || savingPinnedAdjustment;
    private readonly PinnedPlacementInput pinnedPlacementInput = new();
    private RawControllerState lastPlacementController;

    private void InitializePinnedPlacement() => PreviewKeyDown += (_, args) =>
    {
        if (pinnedAdjustment is null && !savingPinnedAdjustment) return;
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
            if (pinnedAdjustment?.OpacityOnly == true) StepPinnedOpacity(direction);
            else StepPinnedPlacement(direction, resize);
        }
        else if (args.Key == Windows.System.VirtualKey.Enter) _ = FinishPinnedAdjustmentAsync(save: true);
        else if (args.Key == Windows.System.VirtualKey.Escape) CancelPinnedAdjustment();
    };

    private Task BeginPinnedAdjustmentAsync() => BeginPinnedAdjustmentAsync(opacityOnly: false);

    private Task BeginPinnedAdjustmentAsync(bool opacityOnly)
    {
        if (retired || !visible || !foreground || switching || IsMediaFullscreen || savingPinnedAdjustment ||
            pinned is not { } current || !current.IsCurrent) return Task.CompletedTask;
        CancelPinnedAdjustment();
        ExitPinnedInteraction(restoreMain: false);
        ReconcilePinnedPlacementEnvironment(current);
        if (!current.Window.IsVisible) return Task.CompletedTask;
        var placement = new PinnedPlacementAdjustment(current.Window.Bounds, current.Monitor, current.Limits,
            current.LayoutId, current.Window.OpacityPercent);
        pinnedAdjustment = new(current, placement, opacityOnly, selectionVersion, visibleSince);
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
        WidgetHost.IsHitTestVisible = Tray.IsHitTestVisible = false;
        RefreshRadialChooser(); UpdateTrayHelp(); UpdateDiagnostics();
        return Task.CompletedTask;
    }

    private IReadOnlyList<ControllerGuideHint> PinnedPlacementHints() => pinnedAdjustment?.OpacityOnly == true ?
    (ControllerGuideHint[])[
        new(ControllerPrompt.DPadHorizontal, $"Opacity: {pinnedAdjustment.Placement.OpacityPercent}%", Required: true),
        new(ControllerPrompt.LeftStickMove, "Adjust", Required: true),
        new(ControllerPrompt.A, "Save", ControllerButton.A, Required: true),
        new(ControllerPrompt.B, "Cancel", ControllerButton.B, Required: true),
    ] :
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
        if (retired || !visible || !foreground || frame.Connected == 0 ||
            pinnedAdjustment is { } pending && (!ReferenceEquals(pinned, pending.Surface) || !pending.Surface.IsCurrent))
        {
            if (frame.Connected == 0) { shellOwnedReleases.Clear(); trayHold.Reset(); radialInput.Reset(); }
            CancelPinnedAdjustment(); return true;
        }
        foreach (var (mask, button) in Buttons)
        {
            if ((frame.PressedButtons & mask) != 0) shellOwnedReleases.Add(button);
            if ((frame.ReleasedButtons & mask) != 0) shellOwnedReleases.Remove(button);
        }
        if (frame.LeftTriggerPressed != 0) shellOwnedReleases.Add(ControllerButton.LeftTrigger);
        if (frame.RightTriggerPressed != 0) shellOwnedReleases.Add(ControllerButton.RightTrigger);
        if (frame.LeftTriggerReleased != 0) shellOwnedReleases.Remove(ControllerButton.LeftTrigger);
        if (frame.RightTriggerReleased != 0) shellOwnedReleases.Remove(ControllerButton.RightTrigger);
        if (savingPinnedAdjustment) return true;
        var sample = frame.State;
        var changes = pinnedPlacementInput.Sample(sample.Buttons, sample.LeftThumbX, sample.LeftThumbY,
            sample.RightThumbX, sample.RightThumbY, Environment.TickCount64);
        if (pinnedAdjustment?.OpacityOnly == true)
        {
            StepPinnedOpacity(changes.Dpad);
            StepPinnedOpacity(changes.Move);
        }
        else
        {
            StepPinnedPlacement(changes.Dpad, resize: false);
            StepPinnedPlacement(changes.Move, resize: false);
            StepPinnedPlacement(changes.Resize, resize: true);
        }
        if ((frame.PressedButtons & 0x1000) != 0) _ = FinishPinnedAdjustmentAsync(save: true);
        else if ((frame.PressedButtons & 0x2000) != 0) CancelPinnedAdjustment();
        return true;
    }

    private async Task<bool> RoutePinnedPlacementButtonAsync(ControllerButton button, ControllerEventPhase phase)
    {
        if (pinnedAdjustment is null && !savingPinnedAdjustment) return false;
        if (phase != ControllerEventPhase.Pressed) return true;
        shellOwnedReleases.Add(button);
        if (savingPinnedAdjustment) return true;
        if (button == ControllerButton.A) await FinishPinnedAdjustmentAsync(save: true);
        else if (button == ControllerButton.B) CancelPinnedAdjustment();
        return true;
    }

    private void StepPinnedPlacement(PinnedPlacementDirection direction, bool resize)
    {
        if (pinnedAdjustment is not { } adjustment || !ReferenceEquals(pinned, adjustment.Surface) ||
            !adjustment.Surface.IsCurrent || !adjustment.Placement.Step(direction, resize)) return;
        adjustment.Surface.Window.Place(adjustment.Placement.Current);
        adjustment.Surface.LogicalPlacement = PinnedPlacementPolicy.Capture(adjustment.Placement.Current, adjustment.Surface.Monitor,
            adjustment.Surface.Limits, adjustment.Surface.LayoutId, adjustment.Surface.Window.OpacityPercent);
        UpdateDiagnostics();
    }

    private void StepPinnedOpacity(PinnedPlacementDirection direction)
    {
        if (pinnedAdjustment is not { OpacityOnly: true } adjustment || !ReferenceEquals(pinned, adjustment.Surface) ||
            !adjustment.Surface.IsCurrent || !adjustment.Placement.StepOpacity(direction)) return;
        var current = adjustment.Surface;
        current.Window.SetOpacity(adjustment.Placement.OpacityPercent);
        current.LogicalPlacement = PinnedPlacementPolicy.Capture(current.Window.Bounds, current.Monitor,
            current.Limits, current.LayoutId, current.Window.OpacityPercent);
        UpdateTrayHelp(); UpdateDiagnostics();
    }

    private async Task FinishPinnedAdjustmentAsync(bool save)
    {
        if (!save) { CancelPinnedAdjustment(); return; }
        if (pinnedAdjustment is not { } adjustment || savingPinnedAdjustment) return;
        savingPinnedAdjustment = true;
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
            await SavePinnedAsync(adjustment.Surface, required: true);
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
        }
        WidgetHost.IsHitTestVisible = Tray.IsHitTestVisible = true;
        surface?.SetPresentationInputEnabled(!retired && visible && foreground && !switching && activeWidget == requestedWidget);
        surface?.SetAutomaticFocusEnabled(MainFocusEnabled);
        rightStick.Reset();
        RefreshRadialChooser();
        if (!restorePinnedAdjustmentFocus || retired || !visible || !foreground ||
            adjustment.Selection != selectionVersion || adjustment.VisibleSession != visibleSince) return;
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
            if (retired || !ReferenceEquals(pinned, current)) return;
            try { CancelPinnedAdjustment(); ReconcilePinnedPlacementEnvironment(current); _ = RefreshPinnedDisplayAsync(current); }
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
