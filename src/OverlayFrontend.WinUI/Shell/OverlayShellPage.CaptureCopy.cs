using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.Windows.Storage.Pickers;
using WidgetRail.PlatformBroker;
using WidgetRail.WidgetProtocol;

namespace WidgetRail.OverlayFrontend.WinUI.Shell;

internal sealed partial class OverlayShellPage
{
    private async Task ChooseCaptureCopyAsync(HostCaptureCopyRequest request, string? widgetId)
    {
        string? destination = null;
        var selection = selectionVersion;
        try
        {
            if (activeWidget != widgetId || request.Count is < 1 or > CaptureLibraryLimits.MaximumCopyItems) return;
            destination = await PickCaptureCopyDestinationAsync(async token => !retired && visible && activeWidget == widgetId && selection == selectionVersion &&
                await owner!.Session.IsCaptureCopyCurrentAsync(request.Id, token), lifetime.Token);
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        { if (!retired) Diagnostics.FrontendFailureLog.Current.Write("capture-copy-picker", error); }
        finally
        {
            try { if (!retired) await owner!.Session.CompleteCaptureCopyAsync(request.Id, destination, lifetime.Token); }
            catch (Exception error) when (error is not OutOfMemoryException)
            { if (!retired) Diagnostics.FrontendFailureLog.Current.Write("capture-copy-completion", error); }
        }
    }

    private async Task<string?> PickCaptureCopyDestinationAsync(Func<CancellationToken, Task<bool>> current, CancellationToken token, string commitText = "Copy here")
    {
        if (retired || !visible || !foreground || !interactive || HostChoiceActive || LocalInstallActive || SystemFilePickerOpen) return null;
        var presenter = surface;
        var selection = selectionVersion;
        var focus = FocusManager.GetFocusedElement(XamlRoot) as Control;
        using var cancel = CancellationTokenSource.CreateLinkedTokenSource(token);
        cancel.CancelAfter(TimeSpan.FromSeconds(120));
        if (!await current(cancel.Token)) return null;
        SystemFilePickerOpen = true;
        presenter?.SetAutomaticFocusEnabled(false); presenter?.SetPresentationInputEnabled(false);
        shellGestures.Claim(ControllerButton.A); ResetInputPresentation(); PublishControllerGuide();
        var monitor = MonitorAsync();
        try
        {
            var window = XamlRoot.ContentIslandEnvironment.AppWindowId;
            var picker = new FolderPicker(window) { CommitButtonText = commitText, SuggestedStartLocation = PickerLocationId.PicturesLibrary };
            var chosen = await NativeFilePickerLifetime.RunAsync(window, () => picker.PickSingleFolderAsync().AsTask(), cancel.Token);
            return chosen is not null && await current(cancel.Token) ? chosen.Path : null;
        }
        catch (OperationCanceledException) { return null; }
        finally
        {
            cancel.Cancel();
            try { await monitor; } catch (OperationCanceledException) { }
            SystemFilePickerOpen = false; ResetInputPresentation(); PublishControllerGuide();
            if (!retired && visible && foreground && selection == selectionVersion && ReferenceEquals(presenter, surface))
            {
                presenter?.SetAutomaticFocusEnabled(MainFocusEnabled); presenter?.SetPresentationInputEnabled(MainFocusEnabled);
                if (focus is { IsLoaded: true, IsEnabled: true }) focus.Focus(FocusState.Keyboard); else QueueEntryFocus();
            }
        }
        async Task MonitorAsync()
        {
            try
            {
                using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(250));
                while (await timer.WaitForNextTickAsync(cancel.Token))
                    if (!await current(cancel.Token)) { cancel.Cancel(); break; }
            }
            catch (Exception error) when (error is not OutOfMemoryException) { cancel.Cancel(); }
        }
    }
}
