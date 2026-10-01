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
    private HostChoiceDialog? hostChoiceDialog;
    private bool HostChoiceActive => hostChoiceDialog is not null;
    internal Func<Uri, CancellationToken, Task<bool>>? OpenExternalWebPageRequested { get; set; }

    private async Task InvokeIntentAsync(WidgetActionRequest request)
    {
        if (intentCancellation is not null || HostChoiceActive || LocalInstallActive || owner is null) return;
        var session = owner.Session;
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        intentCancellation = cancellation;
        WidgetIntentPreparation? prepared = null;
        shellOwnedReleases.Add(ControllerButton.A);
        try
        {
            prepared = await session.PrepareIntentAsync(request.Displayed, request.Action, cancellation.Token);
            if (prepared.Kind is WidgetIntentLaunchKind.Rejected or WidgetIntentLaunchKind.Unavailable)
            {
                await ShowHostChoiceAsync("Cannot open this item", "No compatible widget is available for this action.", [], cancellation.Token);
                return;
            }
            WidgetPresentationFrame? target = null;
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
                await SelectAsync(selected, preserveIntent: true);
                cancellation.Token.ThrowIfCancellationRequested();
                if (retired || !visible || !foreground || activeWidget != selected || surface?.CurrentBinding is not { } destination)
                    return;
                target = destination.Frame;
            }
            var completion = await session.CommitIntentAsync(prepared, target, cancellation.Token);
            var external = completion.ExternalUrl;
            if (!completion.Accepted)
            {
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
                if (!await open(new Uri(external), lifetime.Token) && !retired && visible && foreground)
                    await ShowHostChoiceAsync("Could not open browser", "Windows could not open this page. Please try again.", [], lifetime.Token);
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
            if (ReferenceEquals(intentCancellation, cancellation)) intentCancellation = null;
            PublishControllerGuide();
        }
    }

    private async Task<string?> ShowHostChoiceAsync(string title, string message, IReadOnlyList<HostDialogChoice> choices, CancellationToken token)
    {
        if (HostChoiceActive || LocalInstallActive || retired || !visible || !foreground) return null;
        var original = surface;
        var originalFocus = FocusManager.GetFocusedElement(XamlRoot) as Control;
        var originalState = original?.CapturePresentationState();
        var dialog = new HostChoiceDialog(title, message, choices) { XamlRoot = XamlRoot };
        hostChoiceDialog = dialog;
        original?.SetPresentationInputEnabled(false);
        original?.SetAutomaticFocusEnabled(false);
        ResetInputPresentation(); PublishControllerGuide();
        try
        {
            using var theme = NativePopupTheme.Dialog(dialog, original ?? (FrameworkElement)this);
            using var dismiss = token.Register(() => DispatcherQueue.TryEnqueue(() => dialog.Hide()));
            await dialog.ShowAsync();
            token.ThrowIfCancellationRequested();
            return dialog.SelectedId;
        }
        finally
        {
            hostChoiceDialog = null;
            ResetInputPresentation(); PublishControllerGuide();
            if (!retired && visible && foreground && ReferenceEquals(surface, original))
            {
                if (originalState is not null) original?.RestorePresentationState(originalState);
                original?.SetPresentationInputEnabled(!switching && activeWidget == requestedWidget);
                original?.SetAutomaticFocusEnabled(MainFocusEnabled);
                if (originalFocus is { IsLoaded: true, IsEnabled: true } && WithinOriginal(originalFocus))
                    originalFocus.Focus(FocusState.Keyboard);
                // Native dialog teardown can finish focus restoration after
                // ShowAsync completes. Re-enter through the presenter's logical
                // memory instead of relying on a possibly stale Control reference.
                original?.Enter(restoreNativeFocus: true);
            }
        }

        bool WithinOriginal(DependencyObject node)
        {
            for (var current = node; current is not null; current = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetParent(current))
                if (ReferenceEquals(current, original)) return true;
            return false;
        }
    }

    private bool ReceiveHostChoice(ControllerFrame frame)
    {
        if (hostChoiceDialog is not { } dialog) return false;
        heldAction.Reset(); rightStick.Reset();
        foreach (var (mask, button) in Buttons)
            if ((frame.ReleasedButtons & mask) != 0) shellOwnedReleases.Remove(button);
        if (!foreground || frame.Connected == 0 || frame.Primed != 0) return true;
        if (frame.State.Buttons == 0 && frame.State.LeftTrigger == 0 && frame.State.RightTrigger == 0) dialog.ControllerArmed = true;
        if (!dialog.ControllerArmed) return true;
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
