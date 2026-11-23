using Microsoft.UI.Xaml;
using Microsoft.Windows.Storage.Pickers;
using WidgetRail.OverlayFrontend.WinUI.Presentation;
using WidgetRail.OverlayFrontend.WinUI.Browser;
using WidgetRail.WidgetProtocol;

namespace WidgetRail.OverlayFrontend.WinUI.Shell;

internal sealed partial class OverlayShellPage
{
    private FileBrowserDialog? fileBrowser;
    private PinnedSurface? fileBrowserPin;
    private CancellationTokenSource? fileBrowserLifetime;

    private async Task<string[]> ChoosePathsAsync(XamlRoot root, PinnedSurface? pin, FileBrowserRequest request,
        Func<CancellationToken, Task<string[]>> windowsPicker, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (!SystemFilePickerOpen || fileBrowserLifetime is not null || retired || !visible)
            return [];
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(token, lifetime.Token);
        var presenter = pin?.Window.AutomationRoot ?? (FrameworkElement?)surface ?? this;
        var picker = new FileBrowserDialog(request) { XamlRoot = root };
        fileBrowserLifetime = cancellation;
        fileBrowserPin = pin;
        fileBrowser = picker;
        picker.GuideChanged += PublishControllerGuide;
        try
        {
            PublishControllerGuide();
            using var theme = NativePopupTheme.Dialog(picker, presenter);
            await picker.PresentAsync(presenter, cancellation.Token);
            cancellation.Token.ThrowIfCancellationRequested();
            if (pin is not null && (!ReferenceEquals(pin, pinned) || !PinnedInputActive) || !visible || retired)
                return [];
            if (!picker.UseWindows)
                return picker.Result;
            fileBrowser = null;
            PublishControllerGuide();
            return await windowsPicker(cancellation.Token);
        }
        finally
        {
            picker.GuideChanged -= PublishControllerGuide;
            fileBrowser = null;
            fileBrowserPin = null;
            fileBrowserLifetime = null;
            PublishControllerGuide();
        }
    }
    private Task<string[]> ChooseBrowserPathsAsync(XamlRoot root, PinnedSurface? pin, BrowserFileRequest browser, string? title, int maximum, CancellationToken token, string? commitLabel = null)
    {
        var mode = browser.Save ? FileBrowserMode.Save : browser.Multiple ? FileBrowserMode.Multiple : FileBrowserMode.Open;
        var suffix = Path.GetExtension(browser.SuggestedName);
        var extension = browser.Save ? suffix is { Length: > 1 and <= 20 } && suffix.Skip(1).All(char.IsLetterOrDigit) ? suffix : ".bin" : null;
        var request = new FileBrowserRequest(mode, title ?? (browser.Save ? "Save download" : "Choose files to upload"),
            browser.Save ? "Save" : commitLabel ?? "Choose", browser.Save ? DownloadLocation() : Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            extension, browser.Save ? browser.SuggestedName : null, maximum);
        return ChoosePathsAsync(root, pin, request, ct => BrowserFilePicker.PickAsync(root.ContentIslandEnvironment.AppWindowId, browser, ct, commitLabel), token);
    }
    private static string DownloadLocation()
    {
        try
        {
            if (Windows.Storage.UserDataPaths.GetDefault().Downloads is { Length: > 0 } path)
                return path;
        }
        catch (System.Runtime.InteropServices.COMException) { }
        return Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    }
    private async Task<string?> ChooseLocalPathAsync(XamlRoot root, PinnedSurface? pin, FileBrowserRequest request, PickerLocationId initial, CancellationToken token)
    {
        var selected = await ChoosePathsAsync(root, pin, request, async ct =>
        {
            var window = root.ContentIslandEnvironment.AppWindowId;
            if (request.Mode == FileBrowserMode.Folder)
            {
                var picker = new FolderPicker(window) { SuggestedStartLocation = initial, CommitButtonText = request.CommitLabel };
                var choice = await NativeFilePickerLifetime.RunAsync(window, () => picker.PickSingleFolderAsync().AsTask(), ct);
                return choice is null ? [] : [choice.Path];
            }
            var open = new FileOpenPicker(window) { SuggestedStartLocation = initial, CommitButtonText = request.CommitLabel };
            open.FileTypeFilter.Add(request.Extension ?? "*");
            var file = await NativeFilePickerLifetime.RunAsync(window, () => open.PickSingleFileAsync().AsTask(), ct);
            return file is null ? [] : [file.Path];
        }, token);
        return selected.Length == 1 ? selected[0] : null;
    }
}
