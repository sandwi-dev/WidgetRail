using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.Windows.Storage.Pickers;
using WidgetRail.OverlayFrontend.WinUI.Presentation;
using WidgetRail.OverlayPlatformClient;
using WidgetRail.WidgetPresentationSession;
using WidgetRail.WidgetProtocol;

namespace WidgetRail.OverlayFrontend.WinUI.Shell;

internal sealed partial class OverlayShellPage
{
    private CancellationTokenSource? localInstallCancellation;
    private LocalWidgetInstallDialog? localInstallDialog;
    internal bool SystemFilePickerOpen { get; private set; }
    private bool LocalInstallActive => localInstallCancellation is not null;

    private async Task InstallLocalWidgetAsync(WidgetActionRequest request)
    {
        if (LocalInstallActive || owner is null) return;
        var session = owner.Session;
        session.ValidateLocalWidgetInstall(request.Displayed, request.Action);
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        localInstallCancellation = cancellation;
        var originalSurface = surface;
        var originalFocus = FocusManager.GetFocusedElement(XamlRoot) as Control;
        originalSurface?.SetAutomaticFocusEnabled(false);
        originalSurface?.SetPresentationInputEnabled(false);
        shellOwnedReleases.Add(ControllerButton.A);
        ResetInputPresentation();
        try
        {
            SystemFilePickerOpen = true;
            PublishControllerGuide();
            var picker = new FileOpenPicker(Win32Interop.GetWindowIdFromWindow((nint)hostWindow))
            { SuggestedStartLocation = PickerLocationId.Downloads, CommitButtonText = "Select widget" };
            picker.FileTypeFilter.Add(".wrwidget");
            var file = await picker.PickSingleFileAsync().AsTask(cancellation.Token);
            SystemFilePickerOpen = false;
            if (file is null || cancellation.IsCancellationRequested || retired || !visible || !foreground || !ReferenceEquals(surface, originalSurface)) return;
            session.ValidateLocalWidgetInstall(request.Displayed, request.Action);
            var dialog = new LocalWidgetInstallDialog { XamlRoot = XamlRoot };
            localInstallDialog = dialog;
            dialog.StateChanged += PublishControllerGuide;
            PublishControllerGuide();
            using var theme = NativePopupTheme.Dialog(dialog, originalSurface ?? (FrameworkElement)this);
            using var dismiss = cancellation.Token.Register(() => DispatcherQueue.TryEnqueue(() => dialog.Hide()));
            dialog.Closed += (_, _) => { if (!dialog.Finished) cancellation.Cancel(); };
            var showing = dialog.ShowAsync().AsTask();
            try
            {
                var result = await session.InstallLocalWidgetAsync(request.Displayed, request.Action, file.Path,
                    dialog.ReviewAsync, cancellation.Token);
                if (!cancellation.IsCancellationRequested) dialog.Complete(result);
            }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { dialog.Hide(); }
            catch (Exception error)
            {
                Diagnostics.FrontendFailureLog.Current.Write("local-widget-install", error);
                dialog.Complete(new("failed", "", "", "The widget could not be installed. Please choose the package again."));
            }
            await showing;
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
        catch (Exception error)
        {
            Diagnostics.FrontendFailureLog.Current.Write("local-widget-picker", error);
            if (!retired && visible && foreground && !cancellation.IsCancellationRequested)
            {
                SystemFilePickerOpen = false;
                var dialog = new LocalWidgetInstallDialog { XamlRoot = XamlRoot };
                localInstallDialog = dialog;
                dialog.StateChanged += PublishControllerGuide;
                PublishControllerGuide();
                using var theme = NativePopupTheme.Dialog(dialog, originalSurface ?? (FrameworkElement)this);
                dialog.Complete(new("failed", "", "", "The package picker could not complete. Return to Widgets and try again."));
                using var dismiss = cancellation.Token.Register(() => DispatcherQueue.TryEnqueue(() => dialog.Hide()));
                await dialog.ShowAsync();
            }
        }
        finally
        {
            SystemFilePickerOpen = false;
            localInstallDialog = null;
            localInstallCancellation = null;
            PublishControllerGuide();
            ResetInputPresentation();
            if (!retired && visible && foreground && ReferenceEquals(surface, originalSurface))
            {
                originalSurface?.SetPresentationInputEnabled(!switching && activeWidget == requestedWidget);
                originalSurface?.SetAutomaticFocusEnabled(MainFocusEnabled);
                if (originalFocus is { IsLoaded: true, IsEnabled: true }) originalFocus.Focus(FocusState.Keyboard);
                else QueueEntryFocus();
            }
        }
    }

    private bool ReceiveLocalInstall(ControllerFrame frame)
    {
        if (!LocalInstallActive) return false;
        heldAction.Reset(); rightStick.Reset();
        foreach (var (mask, button) in Buttons)
            if ((frame.ReleasedButtons & mask) != 0) shellOwnedReleases.Remove(button);
        if (localInstallDialog is not { } dialog || !foreground || frame.Connected == 0 || frame.Primed != 0) return true;
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
