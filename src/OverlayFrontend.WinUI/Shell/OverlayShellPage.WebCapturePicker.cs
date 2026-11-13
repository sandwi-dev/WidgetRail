using Microsoft.UI.Xaml.Controls;
using WidgetRail.OverlayFrontend.WinUI.Browser;
using WidgetRail.OverlayFrontend.WinUI.Browser.Capture;
using WidgetRail.WidgetProtocol;

namespace WidgetRail.OverlayFrontend.WinUI.Shell;

internal sealed partial class OverlayShellPage
{
    private async Task<WebCaptureSourceSelection?> ChooseWebCaptureSourceAsync(
        BrowserSurface browser, WebCaptureStreamOptions options, IReadOnlyList<WebCaptureStreamOptions>? permitted, bool fixedContentPreference, CancellationToken token)
    {
        if (!options.IsWellFormed() || retired || !visible || switching || intentCancellation is not null || HostChoiceActive ||
            LocalInstallActive || SystemFilePickerOpen || owner is null || !browser.SupportsNativeCapture)
            return null;
        // The provider owns its restrictions; this host only intersects them
        // with the local capture transport. Omitted policy keeps legacy choices.
        var qualities=LiveCaptureQuality.Allowed(permitted);
        var initial=qualities.SingleOrDefault(quality=>quality.Options.IsSameQuality(options));
        if(initial is null)return null;
        var requestOwner = owner;
        var source = requestOwner.Session.GetState(browser.WidgetId)?.LastGood;
        if (source is null) return null;
        var authority = source.Authority;
        var sourcePin = PinnedInputActive && pinned?.WidgetId == browser.WidgetId ? pinned : null;
        var visibleSession = visibleSince;
        var selected = selectionVersion;
        bool Current() => !retired && visible && ReferenceEquals(owner, requestOwner) && visibleSince == visibleSession &&
            selectionVersion == selected && !switching &&
            (sourcePin is null ? foreground && interactive && activeWidget == browser.WidgetId && requestedWidget == browser.WidgetId :
                ReferenceEquals(pinned, sourcePin) && sourcePin.IsCurrent && sourcePin.HasForeground && sourcePin.Window.Interactive) &&
            requestOwner.Session.GetState(browser.WidgetId)?.LastGood is { } latest &&
            latest.Authority.WidgetInstanceId == authority.WidgetInstanceId && latest.Authority.RuntimeGeneration == authority.RuntimeGeneration &&
            latest.Authority.SessionGeneration == authority.SessionGeneration && latest.Authority.WorkerRun == authority.WorkerRun;
        if (!Current()) return null;
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(token, lifetime.Token);
        using var document = browser.CaptureDocumentLease(cancellation.Token);
        if (document is null) return null;
        intentCancellation = cancellation;
        intentPinnedTarget = sourcePin;
        try
        {
            var windows = await TaskWindows.GetWindowsAsync(document.Token);
            if (!Current() || !document.IsCurrent) return null;
            var candidates = WebCaptureSourcePolicy.Candidates(windows, (uint)Environment.ProcessId);
            CheckBox? audio = null;
            var initialQuality = initial.Id;
            Func<string> selectedQuality = () => initialQuality;
            var initialContent=options.ContentPreference==WebCaptureContentPreference.Motion?"motion":"detail";
            Func<string> selectedContent = () => initialContent;
            var choice = await ShowHostChoiceAsync("Share application",
                candidates.Count == 0 ? "No available application windows. Open or restore an application and try again." :
                    "Choose an application to start sharing. Application audio can include other windows of the same app.",
                candidates.Select(candidate => new HostDialogChoice(candidate.Id, candidate.Label)).ToArray(),
                document.Token, pinnedOwner: sourcePin, configure: dialog =>
                {
                    if (candidates.Count == 0) return;
                    selectedQuality = dialog.AddSelectionOption("Quality", "Host.Capture.Quality",
                        qualities.Select(quality => new HostChoiceSelection(quality.Id, quality.Label)).ToArray(), initialQuality);
                    if(!fixedContentPreference)
                        selectedContent = dialog.AddSelectionOption("Content", "Host.Capture.Content",
                            (HostChoiceSelection[])[new("motion","Prefer smoothness"),new("detail","Prefer clarity")],initialContent);
                    audio = dialog.AddBooleanOption("Share application audio", "Host.Capture.ApplicationAudio", beforeChoices: true);
                    dialog.ConfigurePreferenceList(closeLabel: "Cancel");
                });
            if (choice is null || !Current() || !document.IsCurrent) return null;
            var target = candidates.FirstOrDefault(candidate => candidate.Id == choice);
            // Only this immutable, explicitly selected target escapes presentation ownership.
            // Capture independently revalidates its native identity before starting.
            return target is null ? null : new(target.Target, target.ApplicationName, audio?.IsChecked == true,
                qualities.Single(quality => quality.Id == selectedQuality()).Options with
                {ContentPreference=selectedContent()=="motion"?WebCaptureContentPreference.Motion:WebCaptureContentPreference.Detail});
        }
        catch (OperationCanceledException) when (document.Token.IsCancellationRequested) { return null; }
        finally
        {
            if (ReferenceEquals(intentCancellation, cancellation)) { intentCancellation = null; intentPinnedTarget = null; }
        }
    }
}
