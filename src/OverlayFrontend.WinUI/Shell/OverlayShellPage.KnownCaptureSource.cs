using WidgetRail.WidgetProtocol;

namespace WidgetRail.OverlayFrontend.WinUI.Shell;

internal sealed partial class OverlayShellPage
{
    private async Task<string?> ChooseKnownCaptureSourceAsync(CaptureLibraryKnownLocation location,
        Func<CancellationToken, Task<bool>> current, CancellationToken token)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(TimeSpan.FromSeconds(115));
        token = deadline.Token;
        if (location != CaptureLibraryKnownLocation.WindowsGameCaptures || !await current(token) ||
            !foreground || !interactive || HostChoiceActive || LocalInstallActive || SystemFilePickerOpen) return null;
        string? folder = null;
        try
        {
            // Windows resolves the current redirected Captures folder. Do not guess
            // Videos\\Captures or inspect undocumented Game DVR registry settings.
            var path = Windows.Storage.KnownFolders.AppCaptures.Path;
            if (System.IO.Path.IsPathFullyQualified(path) && System.IO.Directory.Exists(path)) folder = path;
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        { Diagnostics.FrontendFailureLog.Current.Write("windows-captures-folder", error); }
        if (!await current(token)) return null;
        if (folder is null) return await PickCaptureCopyDestinationAsync(current, token, "Link Windows captures");

        var choice = await ShowHostChoiceAsync("Link Windows captures?",
            $"{folder}\n\nBrowse screenshots and recordings here without moving or copying the originals.",
            (HostDialogChoice[])[new("link", "Link folder"), new("choose", "Choose another folder…")], token, directDecisionButtons: true);
        if (!await current(token)) return null;
        return choice == "link" ? folder : choice == "choose"
            ? await PickCaptureCopyDestinationAsync(current, token, "Link this folder") : null;
    }
}
