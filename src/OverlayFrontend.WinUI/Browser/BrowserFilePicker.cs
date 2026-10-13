using Microsoft.UI;
using Microsoft.Windows.Storage.Pickers;

namespace WidgetRail.OverlayFrontend.WinUI.Browser;

/// <summary>Windows chooses paths; only the owning native browser receives them.</summary>
internal static class BrowserFilePicker
{
    internal static async Task<string[]> PickAsync(WindowId windowId, BrowserFileRequest request, CancellationToken token)
    {
        if (request.Save)
        {
            var picker = new FileSavePicker(windowId) { SuggestedFileName = request.SuggestedName,
                SuggestedStartLocation = PickerLocationId.Downloads, CommitButtonText = "Save download" };
            var extension = Path.GetExtension(request.SuggestedName);
            if (extension.Length > 1 && extension.Length <= 20 && extension.Skip(1).All(char.IsLetterOrDigit))
                picker.FileTypeChoices.Add("Download", (string[])[extension]);
            else picker.FileTypeChoices.Add("Download", (string[])[".bin"]);
            var chosen = await Shell.NativeFilePickerLifetime.RunAsync(windowId, () => picker.PickSaveFileAsync().AsTask(), token);
            return chosen is null ? [] : [chosen.Path];
        }
        var openPicker = new FileOpenPicker(windowId) { SuggestedStartLocation = PickerLocationId.DocumentsLibrary, CommitButtonText = "Select for upload" };
        openPicker.FileTypeFilter.Add("*");
        if (request.Multiple)
        {
            var chosen = await Shell.NativeFilePickerLifetime.RunAsync(windowId, () => openPicker.PickMultipleFilesAsync().AsTask(), token);
            return chosen is null ? [] : chosen.Select(file => file.Path).ToArray();
        }
        var single = await Shell.NativeFilePickerLifetime.RunAsync(windowId, () => openPicker.PickSingleFileAsync().AsTask(), token);
        return single is null ? [] : [single.Path];
    }
}
