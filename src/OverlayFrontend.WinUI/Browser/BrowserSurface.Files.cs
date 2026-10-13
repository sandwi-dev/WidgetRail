using System.Text.Json;
using Microsoft.Web.WebView2.Core;
using WidgetRail.OverlayFrontend.WinUI.Shell;

namespace WidgetRail.OverlayFrontend.WinUI.Browser;

internal sealed record BrowserFileRequest(bool Save, bool Multiple = false, string SuggestedName = "download");

internal sealed partial class BrowserSurface
{
    internal Func<BrowserSurface, BrowserFileRequest, CancellationToken, Task<string[]>>? ChooseFiles { get; set; }
    private CoreWebView2DevToolsProtocolEventReceiver? fileChooserReceiver;
    private readonly Dictionary<CoreWebView2DownloadOperation, DownloadWatch> downloads = [];
#if ENABLE_WIDGET_VALIDATION
    internal List<string> FixtureDownloadTrace { get; } = [];
    internal int FixtureDownloadCount => downloads.Count;
#endif
    private sealed class DownloadWatch : IDisposable
    {
        private readonly CoreWebView2DownloadOperation operation;
        private readonly Action<CoreWebView2DownloadOperation> changed;
        internal DownloadWatch(CoreWebView2DownloadOperation operation, Action<CoreWebView2DownloadOperation> changed)
        { this.operation = operation; this.changed = changed; operation.StateChanged += Changed; }
        private void Changed(CoreWebView2DownloadOperation sender, object args) => changed(operation);
        public void Dispose()
        {
            try { operation.StateChanged -= Changed; }
            catch (Exception error) when (error is System.Runtime.InteropServices.COMException or ObjectDisposedException) { }
        }
    }

    private async Task InitializeFilesAsync()
    {
        fileChooserReceiver = core!.GetDevToolsProtocolEventReceiver("Page.fileChooserOpened");
        fileChooserReceiver.DevToolsProtocolEventReceived += FileChooserOpened;
        await core.CallDevToolsProtocolMethodAsync("Page.enable", "{}");
        await core.CallDevToolsProtocolMethodAsync("Page.setInterceptFileChooserDialog", "{\"enabled\":true}");
    }

    private void FileChooserOpened(CoreWebView2 sender, CoreWebView2DevToolsProtocolEventReceivedEventArgs args)
    {
        try { HandleFileChooser(args); }
        catch (Exception error) when (error is JsonException or InvalidOperationException or KeyNotFoundException)
        { Diagnostics.FrontendFailureLog.Current.Write("browser-file-request", error); }
    }

    private void HandleFileChooser(CoreWebView2DevToolsProtocolEventReceivedEventArgs args)
    {
        if (!CanRequestSiteDecision || HasDialog || core is not { } current || Choose is null || ChooseFiles is null) return;
        if (args.ParameterObjectAsJson.Length > 8192) return;
        using var data = JsonDocument.Parse(args.ParameterObjectAsJson);
        if (!data.RootElement.TryGetProperty("backendNodeId", out var node) || !node.TryGetInt32(out var nodeId) || nodeId <= 0 ||
            !data.RootElement.TryGetProperty("frameId", out var frame)) return;
        var frameId = frame.GetString();
        var multiple = data.RootElement.GetProperty("mode").GetString() == "selectMultiple";
        var epoch = pageEpoch;
        var token = (siteDecisions ??= CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token)).Token;
        webDialogPending = true; CancelPointer();
        RunSiteDecision(async () =>
        {
            try
            {
                using var tree = JsonDocument.Parse(await current.CallDevToolsProtocolMethodAsync("Page.getFrameTree", "{}"));
                var origin = FindFrameOrigin(tree.RootElement.GetProperty("frameTree"), frameId);
                if (origin is null || !Current()) return;
                if (await Choose!(this, "Choose files to upload?", $"{document.AccessibleName}\n{origin}\n\nThis website can read the files you select in the Windows picker.",
                    (HostDialogChoice[])[new("choose", "Choose files")], token) != "choose" || !Current(requireInput: false)) return;
                var paths = await ChooseFiles!(this, new(false, multiple), token);
                if (paths.Length == 0 || !Current(requireInput: false)) return;
                if (paths.Length > (multiple ? 32 : 1)) { ShowStatus(multiple ? "Choose no more than 32 files for one upload." : "Choose one file for this upload."); return; }
                // A native backend node identifies the exact file input, even if
                // the page rerenders an element with the same HTML id meanwhile.
                using var description = JsonDocument.Parse(await current.CallDevToolsProtocolMethodAsync("DOM.describeNode",
                    JsonSerializer.Serialize(new BrowserBackendNode(nodeId), BrowserJsonContext.Default.BrowserBackendNode)));
                var inputNode = description.RootElement.GetProperty("node");
                if (inputNode.GetProperty("nodeName").GetString() != "INPUT") return;
                var attributes = inputNode.GetProperty("attributes").EnumerateArray().Select(item => item.GetString()).ToArray();
                var fileInput = false;
                for (var i = 0; i + 1 < attributes.Length; i += 2)
                    if (attributes[i] == "type" && string.Equals(attributes[i + 1], "file", StringComparison.OrdinalIgnoreCase)) fileInput = true;
                if (!fileInput || !Current(requireInput: false)) return;
                using var resolved = JsonDocument.Parse(await current.CallDevToolsProtocolMethodAsync("DOM.resolveNode",
                    JsonSerializer.Serialize(new BrowserBackendNode(nodeId), BrowserJsonContext.Default.BrowserBackendNode)));
                var reference = resolved.RootElement.GetProperty("object").GetProperty("objectId").GetString()!;
                try
                {
                    using var connected = JsonDocument.Parse(await current.CallDevToolsProtocolMethodAsync("Runtime.callFunctionOn",
                        JsonSerializer.Serialize(new BrowserFieldCall(reference, "function(){return this.isConnected && this.type === 'file' && !this.disabled;}"), BrowserJsonContext.Default.BrowserFieldCall)));
                    if (!Current(requireInput: false) || !connected.RootElement.GetProperty("result").GetProperty("value").GetBoolean()) return;
                    await current.CallDevToolsProtocolMethodAsync("DOM.setFileInputFiles",
                        JsonSerializer.Serialize(new BrowserSelectedFiles(paths, nodeId), BrowserJsonContext.Default.BrowserSelectedFiles));
                }
                finally
                {
                    if (!retired && ReferenceEquals(core, current))
                        await current.CallDevToolsProtocolMethodAsync("Runtime.releaseObject", JsonSerializer.Serialize(new BrowserObject(reference), BrowserJsonContext.Default.BrowserObject));
                }
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { }
            finally { webDialogPending = false; InteractionChanged?.Invoke(); }
            bool Current(bool requireInput = true) => !token.IsCancellationRequested &&
                (requireInput ? CanRequestSiteDecision : CanCompleteSiteDecision) && ReferenceEquals(core, current) && pageEpoch == epoch;
        });
    }

    private static string? FindFrameOrigin(JsonElement tree, string? id)
    {
        var frame = tree.GetProperty("frame");
        if (frame.GetProperty("id").GetString() == id) return BrowserSiteIdentity.Origin(frame.GetProperty("url").GetString() ?? "", requireHttps: false);
        if (tree.TryGetProperty("childFrames", out var children))
            foreach (var child in children.EnumerateArray()) if (FindFrameOrigin(child, id) is { } origin) return origin;
        return null;
    }

    private void DownloadStarting(CoreWebView2 owner, CoreWebView2DownloadStartingEventArgs args)
    {
        args.Handled = true;
        if (!CanRequestSiteDecision || HasDialog || Choose is null || ChooseFiles is null) { args.Cancel = true; return; }
        if (downloads.Count >= 2)
        { args.Cancel = true; ShowStatus("Finish or cancel a running download in Website controls before starting another."); return; }
        var origin = BrowserSiteIdentity.Origin(owner.Source, requireHttps: false);
        if (origin is null) { args.Cancel = true; return; }
        var token = (siteDecisions ??= CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token)).Token;
        var deferral = args.GetDeferral();
        webDialogPending = true;
        RunSiteDecision(async () =>
        {
            var accepted = false;
            try
            {
                var name = Path.GetFileName(args.ResultFilePath);
                if (string.IsNullOrWhiteSpace(name)) name = "download";
                name = BoundWebsiteText(name, 200);
                if (await Choose!(this, "Save this download?", $"{document.AccessibleName}\n{origin}\n\n{name}\nChoose a destination in the Windows picker. Downloaded files are never opened automatically.",
                    (HostDialogChoice[])[new("save", "Choose where to save")], token) != "save") return;
                var paths = await ChooseFiles!(this, new(true, SuggestedName: name), token);
                token.ThrowIfCancellationRequested();
                if (paths.Length != 1 || !CanCompleteSiteDecision || !ReferenceEquals(core, owner)) return;
                args.ResultFilePath = paths[0];
                var operation = args.DownloadOperation;
                downloads.Add(operation, new(operation, DownloadStateChanged));
                accepted = true;
                ShowStatus("Downloading file…");
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { }
            finally
            {
                webDialogPending = false;
                args.Cancel = !accepted;
#if ENABLE_WIDGET_VALIDATION
                FixtureDownloadTrace.Add($"accepted={accepted} state={args.DownloadOperation.State} reason={args.DownloadOperation.InterruptReason}");
#endif
                deferral.Complete(); InteractionChanged?.Invoke();
            }
        });
    }

    private void DownloadStateChanged(CoreWebView2DownloadOperation operation)
    {
#if ENABLE_WIDGET_VALIDATION
        FixtureDownloadTrace.Add($"state={operation.State} reason={operation.InterruptReason}");
#endif
        if (retired || !downloads.ContainsKey(operation)) return;
        if (operation.State == CoreWebView2DownloadState.Completed)
        { RemoveDownload(operation); ShowStatus("Download saved."); }
        else if (operation.State == CoreWebView2DownloadState.Interrupted)
        { RemoveDownload(operation); operation.Cancel(); ShowStatus("The download was interrupted. Retry the download on the website."); }
    }

    private void RemoveDownload(CoreWebView2DownloadOperation operation)
    { if (downloads.Remove(operation, out var watch)) watch.Dispose(); }

    private async Task ManageDownloadsAsync(CancellationToken token)
    {
        if (Choose is not { } choose || !CanCompleteSiteDecision) return;
        var active = downloads.Keys.ToArray();
        var choices = active.Select((operation, index) => new HostDialogChoice(index.ToString(System.Globalization.CultureInfo.InvariantCulture),
            "Cancel: " + BoundWebsiteText(Path.GetFileName(operation.ResultFilePath), 120))).ToArray();
        var selected = await choose(this, "Downloads", active.Length == 0 ? "No downloads are running." : "Select a download to cancel it.", choices, token);
        if (int.TryParse(selected, out var index) && index >= 0 && index < active.Length && downloads.ContainsKey(active[index]))
        { var operation = active[index]; RemoveDownload(operation); operation.Cancel(); ShowStatus("Download cancelled."); }
    }

    private void CancelDownloads()
    {
        foreach (var operation in downloads.Keys.ToArray())
        {
            RemoveDownload(operation);
            try { operation.Cancel(); }
            catch (Exception error) when (error is System.Runtime.InteropServices.COMException or ObjectDisposedException) { }
        }
    }

    private void CloseFiles()
    {
        if (fileChooserReceiver is { } receiver)
        {
            try { receiver.DevToolsProtocolEventReceived -= FileChooserOpened; }
            catch (Exception error) when (error is System.Runtime.InteropServices.COMException or ObjectDisposedException) { }
            fileChooserReceiver = null;
        }
        CancelDownloads(); ChooseFiles = null;
    }
}
