using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using WidgetRail.OverlayFrontend.WinUI.Media;
using WidgetRail.OverlayFrontend.WinUI.Media.Editing;
using WidgetRail.OverlayFrontend.WinUI.Presentation;
using WidgetRail.OverlayFrontend.WinUI.Shell;
using WidgetRail.WidgetProtocol;

namespace WidgetRail.OverlayFrontend.WinUI.Validation;

internal sealed partial class WidgetStylesValidationPage
{
    private async Task CaptureEditorPlayheadAsync(string directory)
    {
        Directory.CreateDirectory(directory);
        Exception? failure = null;
        var path = Path.GetFullPath(FrontendArguments.Value(Environment.GetCommandLineArgs(), "--capture-editor-video")
            ?? throw new ArgumentException("An owned editor video fixture is required."));
        var scale = XamlRoot.RasterizationScale;
        App.Window.AppWindow.ResizeClient(new((int)(1120 * scale), (int)(900 * scale)));
        var attachment = new CaptureAttachment("editor-playhead-fixture", "video/mp4", 320, 240, new FileInfo(path).Length, 6,
            DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeMilliseconds());
        using var content = new CaptureEditorContent(attachment, new Uri(path).AbsoluteUri, () => true, 900, 1120);
        var dialog = new HostChoiceDialog("Trim clip", "", (HostDialogChoice[])[new("save", "Save copy")], true)
            { XamlRoot = XamlRoot, IsPrimaryButtonEnabled = false };
        dialog.SetInteractiveContent(content, 800);
        using var theme = NativePopupTheme.Dialog(dialog, this);
        var showing = dialog.ShowAsync().AsTask();
        try
        {
            await Wait(() => dialog.IsOpenForValidation && Find<Button>("MediaPlayer.Play", dialog) is { IsEnabled: true });
            var editor = Find<MediaPlayerView>("Capture.Edit.Player", dialog)!;
            var start = Find<Slider>("Capture.Edit.Start", dialog)!;
            var end = Find<Slider>("Capture.Edit.End", dialog)!;
            var ownPlay = Environment.GetCommandLineArgs().Contains("--capture-editor-player-play");
            if (!ownPlay) { start.Value = .5; end.Value = 4.5; }
            var expected = content.Recipe;
            var play = Find<Button>(ownPlay ? "MediaPlayer.Play" : "Capture.Edit.Preview", dialog)!;
            await editor.ValidateEditorPlayheadAsync(async () =>
                {
                    if (ownPlay) await NativeContextProbeInput.LeftClickAsync(play, new(play.ActualWidth / 2, play.ActualHeight / 2));
                    else ((IInvokeProvider)FrameworkElementAutomationPeer.CreatePeerForElement(play).GetPattern(PatternInterface.Invoke)).Invoke();
                }, () => content.Recipe == expected, Path.Combine(directory, "playhead.json"), checks, selectionEnd: !ownPlay);
        }
        catch (Exception error) { failure = error; }
        finally { dialog.Hide(); await showing; }
        await File.WriteAllTextAsync(Path.Combine(directory, "result.json"), JsonSerializer.Serialize(new { passed = failure is null, checks, error = failure?.ToString() }));
        if (failure is not null) throw failure;
    }
}
