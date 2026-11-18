using Microsoft.UI.Xaml;
using WidgetRail.OverlayFrontend.WinUI.Media.Editing;
using WidgetRail.PlatformBroker;

namespace WidgetRail.OverlayFrontend.WinUI.Shell;

internal sealed partial class OverlayShellPage
{
    private Task? editingCapture;

    private async Task EditCaptureAsync(HostCaptureEditRequest request, string? widgetId)
    {
        var selection = selectionVersion;
        var terminal = false;
        var submitting = false;
        HostChoiceDialog? dialog = null;
        Task? submission = null;
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        Task? monitoring = null;
        bool Current() => !retired && visible && foreground && interactive && activeWidget == widgetId && selectionVersion == selection;
        try
        {
            if (!Current() || HostChoiceActive || LocalInstallActive || SystemFilePickerOpen) return;
            var initial = await owner!.Session.GetCaptureEditStatusAsync(request.Id, cancellation.Token);
            if (!initial.Current || initial.Stage != HostCaptureEditStage.Editing || !request.Media.IsWellFormed()) return;
            using var pausedMedia = surface?.PauseMediaPlayersForEditor();
            using var content = new CaptureEditorContent(request.Media, request.PreviewUri, Current, XamlRoot.Size.Height, XamlRoot.Size.Width);
            await ShowHostChoiceAsync(request.Media.ContentType == "image/png" ? "Edit screenshot" : "Trim clip", "",
                (HostDialogChoice[])[new("save", "Save copy")], cancellation.Token, primaryButtonText: "Save copy", configure: value =>
                {
                    dialog = value;
                    value.DefaultButton = Microsoft.UI.Xaml.Controls.ContentDialogButton.Primary;
                    monitoring = MonitorAsync(content);
                    value.SetInteractiveContent(content, 800);
                    value.IsPrimaryButtonEnabled = content.CanSave;
                    content.Changed += () => { if (!submitting) value.IsPrimaryButtonEnabled = content.CanSave; };
                    value.KeepOpenOnChoice = id =>
                    {
                        if (id != "save") return false;
                        if (!submitting && content.CanSave && Current())
                        {
                            submitting = true;
                            value.IsPrimaryButtonEnabled = false;
                            value.CloseButtonText = "Cancel saving";
                            content.ShowSaving(null);
                            value.FocusCloseAction();
                            submission = SubmitAsync(content.Recipe);
                        }
                        return true;
                    };
                });
        }
        catch (OperationCanceledException) { }
        catch (Exception error) when (error is not OutOfMemoryException)
        { if (!retired) Diagnostics.FrontendFailureLog.Current.Write("capture-editor", error); }
        finally
        {
            cancellation.Cancel();
            if (monitoring is not null) try { await monitoring; } catch (OperationCanceledException) { }
            if (submission is not null) try { await submission; } catch (OperationCanceledException) { }
            if (!terminal)
            {
                try { if (!retired) await owner!.Session.CancelCaptureEditAsync(request.Id, lifetime.Token); }
                catch (Exception error) when (error is not OutOfMemoryException)
                { if (!retired) Diagnostics.FrontendFailureLog.Current.Write("capture-editor-cancel", error); }
            }
        }

        async Task SubmitAsync(HostCaptureEditRecipe recipe)
        {
            try
            {
                if (!Current() || !await owner!.Session.CompleteCaptureEditAsync(request.Id, recipe, cancellation.Token))
                    cancellation.Cancel();
            }
            catch (Exception error) when (error is not OutOfMemoryException)
            {
                if (error is not OperationCanceledException && !retired) Diagnostics.FrontendFailureLog.Current.Write("capture-editor-submit", error);
                // No automatic resubmit: the broker owns the single transaction and its commit receipt.
                cancellation.Cancel();
            }
        }
        async Task MonitorAsync(CaptureEditorContent content)
        {
            try
            {
                using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(250));
                while (await timer.WaitForNextTickAsync(cancellation.Token))
                {
                    var status = await owner!.Session.GetCaptureEditStatusAsync(request.Id, cancellation.Token);
                    if (status.Stage is HostCaptureEditStage.Saved or HostCaptureEditStage.Cancelled or HostCaptureEditStage.Failed)
                    { terminal = true; dialog?.Hide(); return; }
                    if (!Current() || !status.Current) { cancellation.Cancel(); return; }
                    if (status.Stage == HostCaptureEditStage.Saving) content.ShowSaving(status.Progress);
                }
            }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
            catch (Exception error) when (error is not OutOfMemoryException)
            { if (!retired) Diagnostics.FrontendFailureLog.Current.Write("capture-editor-status", error); cancellation.Cancel(); }
        }
    }
}
