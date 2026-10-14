using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using WidgetRail.OverlayFrontend.WinUI.Presentation;
using WidgetRail.OverlayPlatformClient;
using WidgetRail.WidgetBridge;
using WidgetRail.WidgetPresentationSession;
using WidgetRail.WidgetProtocol;

namespace WidgetRail.OverlayFrontend.WinUI.Shell;

internal sealed partial class OverlayShellPage
{
    private CancellationTokenSource? intentCancellation;
    private PinnedSurface? intentPinnedTarget;
    private TaskCompletionSource? intentForegroundAcquired;
    private bool browserExternalPending;
    private HostChoiceDialog? hostChoiceDialog;
    private PinnedSurface? hostChoicePin;
    private bool HostChoiceActive => hostChoiceDialog is not null;
    private bool HostChoiceHasForeground => hostChoicePin is { } pin
        ? ReferenceEquals(pinned, pin) && pin.IsCurrent && PinnedInputActive : foreground;
    internal Func<Uri, CancellationToken, Task<bool>>? OpenExternalWebPageRequested { get; set; }

    private async Task InvokePinnedIntentAsync(PinnedSurface pin, WidgetActionRequest request)
    {
        if (retired || !visible || !ReferenceEquals(pinned, pin) || !pin.IsCurrent ||
            !pin.HasForeground || !pin.Window.Interactive || !ReferenceEquals(request.PinnedSelection, pin.Selection)) return;
        await InvokeIntentAsync(request);
    }

    private async Task InvokeIntentAsync(WidgetActionRequest request)
    {
        if (intentCancellation is not null || HostChoiceActive || LocalInstallActive || SystemFilePickerOpen || owner is null) return;
        var session = owner.Session;
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        intentCancellation = cancellation;
        WidgetIntentPreparation? prepared = null;
        shellOwnedReleases.Add(ControllerButton.A);
        try
        {
            prepared = request.IndexedLease is { } lease && request.IndexedItemKey is { } key
                ? await session.PrepareIndexedIntentAsync(lease, request.Displayed, key, cancellation.Token)
                : request.PinnedSelection is { } sourceSelection && request.PinnedProjection is { } sourceProjection
                    ? await session.PreparePinnedIntentAsync(sourceSelection, sourceProjection, request.Action, cancellation.Token)
                    : await session.PrepareIntentAsync(request.Displayed, request.Action, cancellation.Token);
            var sourcePin = request.PinnedSelection is not null ? pinned : null;
            if (request.PinnedSelection is not null && (sourcePin is null || !sourcePin.IsCurrent || !sourcePin.HasForeground ||
                !ReferenceEquals(sourcePin.Selection, request.PinnedSelection))) return;
            for (var attempt = 0; attempt < 2; attempt++)
            {
                var staysInSourcePin = sourcePin is not null && prepared.Kind == WidgetIntentLaunchKind.Widget &&
                    prepared.Presentation == WidgetIntentPresentation.PreferExistingSurface &&
                    prepared.Destinations.Single() is { SupportsPassiveDelivery: true } only && only.WidgetId == sourcePin.WidgetId;
                if (sourcePin is not null && !staysInSourcePin) await ReturnIntentToMainAsync(cancellation.Token);
                if (prepared.Kind is WidgetIntentLaunchKind.Rejected or WidgetIntentLaunchKind.Unavailable)
                {
                    await ShowHostChoiceAsync("Cannot open this item", "No compatible widget is available for this action.", [], cancellation.Token);
                    return;
                }
                WidgetPresentationFrame? target = null;
                WidgetIntentCompletion? completion = null;
                if (prepared.Kind is WidgetIntentLaunchKind.Widget or WidgetIntentLaunchKind.ChooseHandler)
                {
                    var selected = prepared.Destinations[0].WidgetId;
                    if (prepared.Kind == WidgetIntentLaunchKind.ChooseHandler)
                    {
                        selected = await ShowHostChoiceAsync("Open with", "Choose a widget for this item.",
                            prepared.Destinations.Select(item => new HostDialogChoice(item.WidgetId, item.Name)).ToArray(), cancellation.Token);
                        if (selected is null) return;
                    }
                    cancellation.Token.ThrowIfCancellationRequested();
                    if (prepared.Presentation == WidgetIntentPresentation.PreferExistingSurface &&
                        prepared.Destinations.Single(item => item.WidgetId == selected).SupportsPassiveDelivery &&
                        pinned is { IsCurrent: true, Window.IsVisible: true, Selection: { } selection, Presenter.CurrentBinding: { } binding } pin &&
                        pin.WidgetId == selected)
                    {
                        intentPinnedTarget = pin;
                        var projection = session.ResolvePinnedProjection(binding.Frame, pin.LayoutId);
                        completion = await session.CommitPinnedIntentAsync(prepared, selection, projection, cancellation.Token);
                        cancellation.Token.ThrowIfCancellationRequested();
                        if (retired || !visible || !(foreground || staysInSourcePin && PinnedInputActive) || !ReferenceEquals(pinned, pin) || !pin.IsCurrent || !pin.Window.IsVisible) return;
                        if (completion.RequiresInteraction)
                        {
                            // The receiver already stored this request. Open its
                            // normal presentation without delivering it a second time.
                            if (staysInSourcePin) await ReturnIntentToMainAsync(cancellation.Token);
                            await SelectAsync(selected, preserveIntent: true);
                            cancellation.Token.ThrowIfCancellationRequested();
                        }
                    }
                    else if (prepared.Presentation == WidgetIntentPresentation.PreferExistingSurface &&
                        prepared.Destinations.Single(item => item.WidgetId == selected).SupportsPassiveDelivery &&
                        pinned is { IsCurrent: true, Window.IsVisible: true, Media: { } compact } mediaPin && mediaPin.WidgetId == selected)
                    {
                        intentPinnedTarget = mediaPin;
                        completion = await session.CommitCompactMediaIntentAsync(prepared, compact, cancellation.Token);
                        cancellation.Token.ThrowIfCancellationRequested();
                        if (retired || !visible || !foreground || !ReferenceEquals(pinned, mediaPin)) return;
                        if (completion.RequiresInteraction) await SelectAsync(selected, preserveIntent: true);
                    }
                    else
                    {
                        await SelectAsync(selected, preserveIntent: true);
                        cancellation.Token.ThrowIfCancellationRequested();
                        if (retired || !visible || !foreground || activeWidget != selected || surface?.CurrentBinding is not { } destination)
                            return;
                        target = destination.Frame;
                    }
                }
                completion ??= await session.CommitIntentAsync(prepared, target, cancellation.Token);
                if (completion.FollowUp is { } followUp)
                {
                    prepared = followUp;
                    sourcePin = null;
                    intentPinnedTarget = null;
                    continue;
                }
                var external = completion.ExternalUrl;
                if (!completion.Accepted)
                {
                    if (staysInSourcePin) await ReturnIntentToMainAsync(cancellation.Token);
                    if (completion.BrowserFallbackUrl is { } fallback)
                    {
                        if (await ShowHostChoiceAsync("Could not open the link", "The selected widget could not open this page.",
                            (HostDialogChoice[])[new("browser", "Open in default browser")], cancellation.Token) == "browser") external = fallback;
                    }
                    else await ShowHostChoiceAsync("Cannot open this item", "The destination is no longer available. Please try again.", [], cancellation.Token);
                }
                if (external is not null && OpenExternalWebPageRequested is { } open)
                {
                    cancellation.Token.ThrowIfCancellationRequested();
                    // The release-aware native handoff now owns visibility and cancellation.
                    // Its intentional hide must not cancel itself through SetVisible(false).
                    intentCancellation = null;
                    intentPinnedTarget = null;
                    if (!await open(new Uri(external), lifetime.Token) && !retired && visible && foreground)
                        await ShowHostChoiceAsync("Could not open browser", "Windows could not open this page. Please try again.", [], lifetime.Token);
                }
                break;
            }
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested || retired) { }
        catch (Exception error)
        {
            Diagnostics.FrontendFailureLog.Current.Write("widget-intent", error);
            if (!retired && visible && foreground && !cancellation.IsCancellationRequested)
                await ShowHostChoiceAsync("Cannot open this item", "The request could not complete. Please try again.", [], cancellation.Token);
        }
        finally
        {
            if (prepared is not null) await session.CancelIntentAsync(prepared);
            if (ReferenceEquals(intentCancellation, cancellation))
            { intentCancellation = null; intentPinnedTarget = null; }
            PublishControllerGuide();
        }
    }

    private async Task ReturnIntentToMainAsync(CancellationToken token)
    {
        var acquired = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        intentForegroundAcquired = acquired;
        try
        {
            ExitPinnedInteraction(restoreMain: true);
            if (!foreground) await acquired.Task.WaitAsync(TimeSpan.FromSeconds(2), token);
            token.ThrowIfCancellationRequested();
        }
        finally { if (ReferenceEquals(intentForegroundAcquired, acquired)) intentForegroundAcquired = null; }
    }

    private async Task<bool> OpenBrowserExternalAsync(Uri uri, CancellationToken token)
    {
        if (browserExternalPending || retired || !visible || OpenExternalWebPageRequested is not { } open) return false;
        browserExternalPending = true;
        try
        {
            if (PinnedInputActive) await ReturnIntentToMainAsync(token);
            return foreground && await open(uri, token);
        }
        finally { browserExternalPending = false; }
    }

    private async Task<string?> ShowHostChoiceAsync(string title, string message, IReadOnlyList<HostDialogChoice> choices, CancellationToken token, string? primaryButtonText = null, bool directDecisionButtons = false, PinnedSurface? pinnedOwner = null,
        Action<HostChoiceDialog>? configure = null)
    {
        var original = pinnedOwner is null ? surface : pinnedOwner.Presenter;
        bool Current() => pinnedOwner is null ? foreground && ReferenceEquals(surface, original) :
            ReferenceEquals(pinned, pinnedOwner) && pinnedOwner.IsCurrent && PinnedInputActive;
        if (HostChoiceActive || LocalInstallActive || SystemFilePickerOpen || retired || !visible || !Current()) return null;
        var root = pinnedOwner?.Window.AutomationRoot.XamlRoot ?? XamlRoot;
        var originalFocus = FocusManager.GetFocusedElement(root) as Control;
        var originalState = original?.CapturePresentationState();
        var dialog = new HostChoiceDialog(title, message, choices, directDecisionButtons) { XamlRoot = root };
        dialog.ConstrainMessageViewport(root.Size.Height);
        if (primaryButtonText is not null) dialog.PrimaryButtonText = primaryButtonText;
        hostChoiceDialog = dialog;
        configure?.Invoke(dialog);
        hostChoicePin = pinnedOwner;
        pinnedOwner?.SetInput(false);
        original?.SetPresentationInputEnabled(false);
        original?.SetAutomaticFocusEnabled(false);
        ResetInputPresentation(); PublishControllerGuide();
        try
        {
            using var theme = NativePopupTheme.Dialog(dialog, original ?? pinnedOwner?.Window.AutomationRoot ?? (FrameworkElement)this);
            using var dismiss = token.Register(() => DispatcherQueue.TryEnqueue(() => dialog.Hide()));
            await dialog.ShowAsync();
            token.ThrowIfCancellationRequested();
            return dialog.SelectedId;
        }
        finally
        {
            hostChoiceDialog = null;
            hostChoicePin = null;
            ResetInputPresentation(); PublishControllerGuide();
            if (!retired && visible && Current())
            {
                var enabled = pinnedOwner is not null || MainFocusEnabled;
                if (originalState is not null) original?.RestorePresentationState(originalState);
                pinnedOwner?.SetInput(enabled);
                original?.SetPresentationInputEnabled(enabled);
                original?.SetAutomaticFocusEnabled(enabled);
                if (enabled && originalFocus is { IsLoaded: true, IsEnabled: true } && WithinOriginal(originalFocus))
                    originalFocus.Focus(FocusState.Keyboard);
                // Native dialog teardown can finish focus restoration after
                // ShowAsync completes. Re-enter through the presenter's logical
                // memory instead of relying on a possibly stale Control reference.
                if (expandedBrowser is not null) fullscreenLayer.FocusContent();
                else if (pinnedOwner is not null) pinnedOwner.Enter();
                else if (MainFocusEnabled) original?.Enter(restoreNativeFocus: true);
                else if (!interactive)
                {
                    if (ReferenceEquals(originalFocus, notificationBell)) notificationBell.Focus(FocusState.Keyboard);
                    else FocusTray();
                }
            }
        }

        bool WithinOriginal(DependencyObject node)
        {
            for (var current = node; current is not null; current = Presentation.PresentationLayers.LogicalParent(current))
                if (ReferenceEquals(current, original) || pinnedOwner is not null && ReferenceEquals(current, pinnedOwner.Window.AutomationRoot)) return true;
            return false;
        }
    }

    private bool ReceiveHostChoice(ControllerFrame frame)
    {
        if (hostChoiceDialog is not { } dialog) return false;
        heldAction.Reset(); rightStick.Reset();
        foreach (var (mask, button) in Buttons)
            if ((frame.ReleasedButtons & mask) != 0) shellOwnedReleases.Remove(button);
        if (!visible || !HostChoiceHasForeground || frame.Connected == 0 || frame.Primed != 0)
        { dialog.ResetController(); return true; }
        if (frame.State.Buttons == 0 && frame.State.LeftTrigger == 0 && frame.State.RightTrigger == 0) dialog.ControllerArmed = true;
        if (!dialog.ControllerArmed) return true;
        dialog.SampleMessageScroll(frame.State.RightThumbX, frame.State.RightThumbY, Environment.TickCount64);
        var direction = frame.DpadNavigation.Phase != NavigationPhase.None ? frame.DpadNavigation : frame.StickNavigation;
        var next = direction.Direction switch
        {
            NavigationDirection.Left => FocusNavigationDirection.Left, NavigationDirection.Right => FocusNavigationDirection.Right,
            NavigationDirection.Up => FocusNavigationDirection.Up, NavigationDirection.Down => FocusNavigationDirection.Down,
            _ => FocusNavigationDirection.None,
        };
        if (direction.Phase != NavigationPhase.None && next != FocusNavigationDirection.None) dialog.MoveFocus(next);
        foreach (var (mask, button) in Buttons)
            if ((frame.PressedButtons & mask) != 0) { shellOwnedReleases.Add(button); dialog.Handle(button, ControllerEventPhase.Pressed); }
        return true;
    }
}
