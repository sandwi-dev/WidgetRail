using System.Runtime.InteropServices.WindowsRuntime;
using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using WidgetRail.OverlayFrontend.WinUI.Media.Editing;
using WidgetRail.OverlayFrontend.WinUI.Presentation;
using WidgetRail.OverlayFrontend.WinUI.Shell;
using WidgetRail.WidgetProtocol;

namespace WidgetRail.OverlayFrontend.WinUI.Validation;

internal sealed partial class WidgetStylesValidationPage
{
    private async Task CaptureEditorAsync(string directory)
    {
        if (Environment.GetCommandLineArgs().Contains("--capture-editor-playhead-only"))
        { await CaptureEditorPlayheadAsync(directory); return; }
        Directory.CreateDirectory(directory);
        Exception? failure = null;
        HostChoiceDialog? dialog = null;
        Task<ContentDialogResult>? showing = null;
        try
        {
            var scale = XamlRoot.RasterizationScale;
            App.Window.AppWindow.ResizeClient(new((int)(1120 * scale), (int)(900 * scale)));
            var path = Path.GetFullPath(Path.Combine(directory, "crop-source.png"));
            using (var output = File.Create(path))
            using (var random = output.AsRandomAccessStream())
            {
                var encoder = await Windows.Graphics.Imaging.BitmapEncoder.CreateAsync(Windows.Graphics.Imaging.BitmapEncoder.PngEncoderId, random);
                var pixels = new byte[640 * 360 * 4];
                for (var y = 0; y < 360; y++) for (var x = 0; x < 640; x++)
                {
                    var i = (y * 640 + x) * 4;
                    pixels[i] = (byte)(x * 255 / 640); pixels[i + 1] = (byte)(y * 255 / 360);
                    pixels[i + 2] = (byte)((x / 32 + y / 32) % 2 == 0 ? 200 : 80); pixels[i + 3] = 255;
                }
                encoder.SetPixelData(Windows.Graphics.Imaging.BitmapPixelFormat.Bgra8, Windows.Graphics.Imaging.BitmapAlphaMode.Straight, 640, 360, 96, 96, pixels);
                await encoder.FlushAsync();
            }
            var media = new CaptureAttachment("editor-fixture", "image/png", 640, 360, new FileInfo(path).Length, 0, DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeMilliseconds());
            using (var viewer = new Media.MediaPlayerView { Width = 800, Height = 420 })
            {
                host.Children.Add(viewer);
                try { await viewer.ValidateImageReplacementAsync(path, checks); }
                finally { host.Children.Remove(viewer); }
            }
            using var content = new CaptureEditorContent(media, new Uri(path).AbsoluteUri, () => true, 900, 1120);
            dialog = new("Edit screenshot", "", (HostDialogChoice[])[new("save", "Save copy")], true)
                { XamlRoot = XamlRoot, IsPrimaryButtonEnabled = false, DefaultButton = ContentDialogButton.Primary };
            dialog.SetInteractiveContent(content, 800);
            content.Changed += () => dialog.IsPrimaryButtonEnabled = content.CanSave;
            using var theme = NativePopupTheme.Dialog(dialog, this);
            showing = dialog.ShowAsync().AsTask();
            await Wait(() => dialog.IsOpenForValidation && Find<Button>("Capture.Edit.Crop", dialog) is { IsLoaded: true });
            await Task.Delay(300);
            var cropButton = Find<Button>("Capture.Edit.Crop", dialog)!;
            Check(!content.CanSave && !dialog.IsPrimaryButtonEnabled, "Unchanged image cannot save a duplicate");
            Invoke("Capture.Edit.Square");
            await Wait(() => content.CanSave);
            Check(content.Recipe.Image is { X: 140, Y: 0, Width: 360, Height: 360 }, "Square crop is centered in original pixels");
            cropButton.Focus(FocusState.Keyboard);
            dialog.Handle(ControllerButton.A, ControllerEventPhase.Pressed);
            Check(content.Guide!.All(h => h.Label != "Adjust crop"), "Opening controller press is ignored until neutral");
            dialog.ControllerAdmission.Observe(new() { Connected = 1 }, ownsInput: true);
            dialog.Handle(ControllerButton.A, ControllerEventPhase.Pressed);
            Check(content.Guide!.Any(h => h.Label == "Adjust crop"), "Controller A enters crop adjustment");
            var before = content.Recipe.Image!;
            dialog.MoveFocus(FocusNavigationDirection.Right);
            Check(content.Recipe.Image!.X > before.X && content.Recipe.Image.Width == before.Width, "D-pad moves crop without resizing it");
            var fineCropStep = content.Recipe.Image.X - before.X;
            before = content.Recipe.Image;
            dialog.MoveFocus(FocusNavigationDirection.Left);
            for (var i = 0; i < 15; ++i) { await Task.Delay(100); dialog.MoveFocus(FocusNavigationDirection.Left, repeated: true); }
            Check(before.X - content.Recipe.Image!.X > fineCropStep * 16, "Holding a crop direction accelerates beyond repeated fine steps");
            before = content.Recipe.Image;
            dialog.MoveFocus(FocusNavigationDirection.Right, repeated: true);
            Check(content.Recipe.Image!.X - before.X == fineCropStep, "Reversing the crop direction immediately restores fine adjustment");
            dialog.Handle(ControllerButton.B, ControllerEventPhase.Pressed);
            Check(dialog.IsOpenForValidation && content.Guide!.All(h => h.Label != "Adjust crop"), "First B exits adjustment and keeps editor open");
            NativeContextProbeInput.PressKey(Windows.System.VirtualKey.Enter);
            await Wait(() => content.Guide!.Any(h => h.Label == "Adjust crop"));
            before = content.Recipe.Image!;
            NativeContextProbeInput.PressKey(Windows.System.VirtualKey.Left);
            await Wait(() => content.Recipe.Image!.X == before.X - 1);
            Check(content.Recipe.Image!.X == before.X - 1, "Keyboard arrow makes an exact one-pixel crop adjustment");
            NativeContextProbeInput.PressKey(Windows.System.VirtualKey.Escape);
            await Task.Delay(100);
            Check(dialog.IsOpenForValidation, "Escape exits crop adjustment before dismissing editor");
            before = content.Recipe.Image!;
            await NativeContextProbeInput.PointerGestureAsync(cropButton, new(cropButton.ActualWidth / 2, cropButton.ActualHeight / 2), new(cropButton.ActualWidth / 2 + 30, cropButton.ActualHeight / 2));
            Check(content.Recipe.Image!.X > before.X, "Native mouse drag moves crop");
            before = content.Recipe.Image!;
            await NativeContextProbeInput.HoverAsync(cropButton, () => Task.Delay(100));
            Check(content.Recipe.Image == before, "Released crop drag does not follow unpressed pointer movement");
            Invoke("Capture.Edit.RotateRight");
            await Wait(() => content.Recipe.Image!.QuarterTurns == 1);
            Check(content.Recipe.Image!.QuarterTurns == 1, "Rotate action maps crop into rotated pixel coordinates");
            await CaptureEditorBitmapAsync(dialog, Path.Combine(directory, "image-editor.png"));
            App.Window.AppWindow.ResizeClient(new((int)(560 * scale), (int)(760 * scale)));
            await Task.Delay(200);
            var viewport = (ScrollViewer)content.View;
            Check(viewport.ExtentWidth <= viewport.ViewportWidth + 1, "Narrow editor reflows its content without horizontal clipping");
            var lastTool = Find<Button>("Capture.Edit.FreeCrop", dialog)!;
            lastTool.Focus(FocusState.Keyboard); lastTool.StartBringIntoView(); await Task.Delay(150);
            var bounds = lastTool.TransformToVisual(viewport).TransformBounds(new(0, 0, lastTool.ActualWidth, lastTool.ActualHeight));
            Check(bounds.Left >= -1 && bounds.Right <= viewport.ActualWidth + 1, "Last crop tool remains reachable within a narrow viewport");
            await CaptureEditorBitmapAsync(dialog, Path.Combine(directory, "image-editor-narrow.png"));
            content.ShowSaving(.45); dialog.IsPrimaryButtonEnabled = false; dialog.FocusCloseAction();
            Check(!content.View.IsHitTestVisible || content.View is Control { IsEnabled: false }, "Saving disables editing while keeping native Cancel outside it");
            dialog.Handle(ControllerButton.B, ControllerEventPhase.Pressed);
            await showing;
            Check(dialog.SelectedId is null, "Cancel saving does not synthesize a save choice");
            showing = null; dialog = null;
            App.Window.AppWindow.ResizeClient(new((int)(1120 * scale), (int)(900 * scale)));
            if (FrontendArguments.Value(Environment.GetCommandLineArgs(), "--capture-editor-video") is { } videoPath)
                await CaptureEditorVideoAsync(Path.GetFullPath(videoPath), directory);
        }
        catch (Exception error) { failure = error; }
        finally { dialog?.Hide(); if (showing is not null) await showing; }
        await File.WriteAllTextAsync(Path.Combine(directory, "result.json"), JsonSerializer.Serialize(new { passed = failure is null, checks, error = failure?.ToString() }));
        if (failure is not null) throw failure;

        void Invoke(string id)
        {
            var button = Find<Button>(id, dialog!) ?? throw new InvalidOperationException("Missing editor action " + id);
            (FrameworkElementAutomationPeer.CreatePeerForElement(button)?.GetPattern(PatternInterface.Invoke) as IInvokeProvider)?.Invoke();
        }
    }
    private async Task CaptureEditorVideoAsync(string path, string directory)
    {
        var players = Media.MediaPlayerView.ResidentPlayers;
        var media = new CaptureAttachment("editor-video-fixture", "video/mp4", 320, 240, new FileInfo(path).Length, 6,
            DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeMilliseconds());
        using var content = new CaptureEditorContent(media, new Uri(path).AbsoluteUri, () => true, 900, 1120);
        var dialog = new HostChoiceDialog("Trim clip", "", (HostDialogChoice[])[new("save", "Save copy")], true)
            { XamlRoot = XamlRoot, IsPrimaryButtonEnabled = false, DefaultButton = ContentDialogButton.Primary };
        dialog.SetInteractiveContent(content, 800);
        content.Changed += () => dialog.IsPrimaryButtonEnabled = content.CanSave;
        using var theme = NativePopupTheme.Dialog(dialog, this);
        var showing = dialog.ShowAsync().AsTask();
        try
        {
            await Wait(() => dialog.IsOpenForValidation && Find<Button>("MediaPlayer.Play", dialog) is { IsEnabled: true });
            Check(!content.CanSave, "Unchanged full clip cannot create a duplicate edit");
            var start = Find<Slider>("Capture.Edit.Start", dialog)!;
            var end = Find<Slider>("Capture.Edit.End", dialog)!;
            var preview=Find<Button>("Capture.Edit.Preview",dialog)!;
            var reset=Find<Button>("Capture.Edit.Reset",dialog)!;
            var editorPlayer=Find<Media.MediaPlayerView>("Capture.Edit.Player",dialog)!;
            var viewport=(ScrollViewer)content.View;
            await Wait(()=>start.ActualWidth>0&&reset.ActualWidth>0&&editorPlayer.ActualHeight>300);
            Check(Math.Abs(start.TransformToVisual(viewport).TransformPoint(new()).Y-end.TransformToVisual(viewport).TransformPoint(new()).Y)<1,
                "Wide trim editor pairs Start and End while giving video more than the old 300px stage");
            dialog.FocusCloseAction();
            var cancel=FocusManager.GetFocusedElement(XamlRoot);
            dialog.MoveFocus(FocusNavigationDirection.Up);
            await Wait(()=>ReferenceEquals(FocusManager.GetFocusedElement(XamlRoot),reset));
            Check(true,"Controller Up from Cancel reaches the nearest action row instead of skipping it for End");
            dialog.MoveFocus(FocusNavigationDirection.Left);
            Check(ReferenceEquals(FocusManager.GetFocusedElement(XamlRoot),preview),"Left reaches Play selection from Reset");
            dialog.MoveFocus(FocusNavigationDirection.Up);
            Check(ReferenceEquals(FocusManager.GetFocusedElement(XamlRoot),start),"Up from Play selection reaches Start");
            dialog.MoveFocus(FocusNavigationDirection.Right);
            Check(ReferenceEquals(FocusManager.GetFocusedElement(XamlRoot),end),"Right traverses trim handles without editing the range");
            dialog.MoveFocus(FocusNavigationDirection.Down);
            Check(ReferenceEquals(FocusManager.GetFocusedElement(XamlRoot),reset),"Down from End reaches Reset");
            dialog.MoveFocus(FocusNavigationDirection.Down);
            await Wait(()=>ReferenceEquals(FocusManager.GetFocusedElement(XamlRoot),cancel));
            Check(!content.CanSave,"Editor navigation returns to Cancel without changing the trim selection");
            start.Focus(FocusState.Keyboard); start.StartBringIntoView(); await Task.Delay(100);
            Press(ControllerButton.A);
            dialog.MoveFocus(FocusNavigationDirection.Right);
            Check(content.Recipe.Video!.StartTicks == TimeSpan.FromSeconds(.1).Ticks, "Controller start handle adjusts by a tenth of a second");
            for (var i = 0; i < 15; ++i) { await Task.Delay(100); dialog.MoveFocus(FocusNavigationDirection.Right, repeated: true); }
            Check(content.Recipe.Video!.StartTicks > TimeSpan.FromSeconds(1.6).Ticks, "Sustained controller trimming accelerates even for a short clip");
            var heldStart = content.Recipe.Video.StartTicks;
            dialog.MoveFocus(FocusNavigationDirection.Left, repeated: true);
            Check(Math.Abs(heldStart - content.Recipe.Video!.StartTicks - TimeSpan.FromSeconds(.1).Ticks) <= 1,
                "Reversing a trim handle returns to tenth-second precision (within one TimeSpan tick)");
            Press(ControllerButton.B);
            Check(dialog.IsOpenForValidation, "B exits timeline adjustment without closing the editor");
            end.Focus(FocusState.Keyboard); end.StartBringIntoView(); await Task.Delay(100);
            NativeContextProbeInput.PressKey(Windows.System.VirtualKey.Left);
            await Wait(() => content.Recipe.Video!.EndTicks < TimeSpan.FromSeconds(6).Ticks);
            Check(content.CanSave && content.Recipe.Video!.EndTicks > content.Recipe.Video.StartTicks, "Keyboard end handle keeps a valid ordered trim range");
            var volume = Find<Slider>("MediaPlayer.Volume", dialog)!;
            volume.Focus(FocusState.Keyboard); volume.StartBringIntoView(); await Task.Delay(100);
            Press(ControllerButton.A); Press(ControllerButton.B);
            Check(dialog.IsOpenForValidation, "Player volume adjustment consumes one B and receives its release");
            await CaptureEditorBitmapAsync(dialog, Path.Combine(directory, "video-editor.png"));
            var scale=XamlRoot.RasterizationScale;
            App.Window.AppWindow.ResizeClient(new((int)(560*scale),(int)(760*scale)));
            await Wait(()=>Find<Grid>("Capture.Edit.TrimControls",dialog)?.ColumnDefinitions.Count==1&&viewport.ActualWidth<500);
            start.Focus(FocusState.Keyboard);start.StartBringIntoView();
            dialog.MoveFocus(FocusNavigationDirection.Down);
            Check(ReferenceEquals(FocusManager.GetFocusedElement(XamlRoot),end),"Narrow trim layout stacks handles and keeps Down navigation in order");
            dialog.MoveFocus(FocusNavigationDirection.Down);
            Check(ReferenceEquals(FocusManager.GetFocusedElement(XamlRoot),reset),"Narrow End reaches the action row");
            dialog.MoveFocus(FocusNavigationDirection.Left);
            Check(ReferenceEquals(FocusManager.GetFocusedElement(XamlRoot),preview)&&viewport.ExtentWidth<=viewport.ViewportWidth+1,
                "Both narrow editor actions stay reachable without horizontal overflow");
            await CaptureEditorBitmapAsync(dialog,Path.Combine(directory,"video-editor-narrow.png"));
            App.Window.AppWindow.ResizeClient(new((int)(1120*scale),(int)(900*scale)));
            await Wait(()=>Find<Grid>("Capture.Edit.TrimControls",dialog)?.ColumnDefinitions.Count==2&&viewport.ActualWidth>500);
            if (Environment.GetCommandLineArgs().Contains("--capture-editor-inspect"))
            {
                await File.WriteAllTextAsync(Path.Combine(directory, "video-ready.json"), JsonSerializer.Serialize(new { pid = Environment.ProcessId }));
                using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(60));
                while (!File.Exists(Path.Combine(directory, "continue-video"))) await Task.Delay(100, deadline.Token);
            }
            start.Focus(FocusState.Keyboard); start.StartBringIntoView(); await Task.Delay(150);
            await NativeContextProbeInput.HoldSliderDragAsync(start, async () =>
            {
                dialog.Hide(); await showing;
                Check(!Captures(content.View), "Closing the editor during slider drag releases owned native pointer captures");
            }, () => Task.CompletedTask);
        }
        catch
        {
            var viewport=(ScrollViewer)content.View;
            var grid=Find<Grid>("Capture.Edit.TrimControls",dialog);
            await File.WriteAllTextAsync(Path.Combine(directory,"video-layout-failure.json"),JsonSerializer.Serialize(new
            {
                root=XamlRoot.Size,dialogWidth=dialog.ActualWidth,dialogHeight=dialog.ActualHeight,
                viewportWidth=viewport.ActualWidth,viewportHeight=viewport.ActualHeight,
                viewport.ExtentWidth,viewport.ExtentHeight,viewport.ViewportWidth,viewport.ViewportHeight,
                contentWidth=(viewport.Content as FrameworkElement)?.ActualWidth,
                trimWidth=grid?.ActualWidth,columns=grid?.ColumnDefinitions.Count,
            }));
            await CaptureEditorBitmapAsync(dialog,Path.Combine(directory,"video-editor-failure.png"));
            throw;
        }
        finally { dialog.Hide(); await showing; content.Dispose(); }
        Check(Media.MediaPlayerView.ResidentPlayers == players, "Editor teardown releases the media player decoder slot");
        using (var background = new Media.MediaPlayerView { Width = 800, Height = 300 })
        {
            host.Children.Add(background);
            try { await background.ValidateEditorPauseAsync(path, checks); }
            finally { host.Children.Remove(background); }
        }
        void Press(ControllerButton button)
        {
            dialog.ControllerAdmission.Observe(new() { Connected = 1 }, ownsInput: true);
            dialog.Handle(button, ControllerEventPhase.Pressed); dialog.Handle(button, ControllerEventPhase.Released);
        }
        static bool Captures(DependencyObject node)
        {
            if (node is UIElement { PointerCaptures.Count: > 0 }) return true;
            for (var i = 0; i < Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChildrenCount(node); i++)
                if (Captures(Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChild(node, i))) return true;
            return false;
        }
    }
    private static async Task CaptureEditorBitmapAsync(FrameworkElement element, string path)
    {
        await Task.Delay(200);
        var bitmap = new Microsoft.UI.Xaml.Media.Imaging.RenderTargetBitmap(); await bitmap.RenderAsync(element);
        using var stream = new Windows.Storage.Streams.InMemoryRandomAccessStream();
        var encoder = await Windows.Graphics.Imaging.BitmapEncoder.CreateAsync(Windows.Graphics.Imaging.BitmapEncoder.PngEncoderId, stream);
        encoder.SetPixelData(Windows.Graphics.Imaging.BitmapPixelFormat.Bgra8, Windows.Graphics.Imaging.BitmapAlphaMode.Premultiplied,
            (uint)bitmap.PixelWidth, (uint)bitmap.PixelHeight, 96, 96, (await bitmap.GetPixelsAsync()).ToArray());
        await encoder.FlushAsync(); stream.Seek(0);
        using var output = File.Create(path); await stream.AsStreamForRead().CopyToAsync(output);
    }
}
