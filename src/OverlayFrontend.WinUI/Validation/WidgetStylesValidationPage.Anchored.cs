using System.Runtime.InteropServices.WindowsRuntime;
using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using WidgetRail.PlatformSettings;
using WidgetRail.WidgetBridge;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;
using WidgetRail.WidgetStyling;

namespace WidgetRail.OverlayFrontend.WinUI.Validation;

internal sealed partial class WidgetStylesValidationPage
{
    private async Task AnchoredPresentationsAsync(string directory)
    {
        Directory.CreateDirectory(directory);
        Exception? failure = null;
        var trace = new List<string>();
        void Trace(string value) { if (trace.Count < 160) trace.Add(value); }
        presenter.FocusTrace = Trace;
        try
        {
            var scale = XamlRoot.RasterizationScale;
            App.Window.AppWindow.Resize(new((int)(1080 * scale), (int)(850 * scale)));
            presenter.Height = 560;
            presenter.ApplyAppearance(AppearanceSettings.Default with { Motion = MotionPreference.Reduced }, false);
            var catalog = new ThemeCatalog(new PlatformSettingsPaths(directory));
            foreach (var themeId in new[] { ThemeIdentity.BuiltInMonoInk, ThemeIdentity.BuiltInMineralLight })
            {
                var package = WrssParser.Parse(".anchor-page { padding: 16px; background: var(--surface); } .anchor-button { height: 48px; } text { color: var(--text); } .anchor-minimum { min-width: 500px; min-height: 320px; }", "anchor-fixture.wrss");
                var compiled = ThemeLayerCompiler.Compile(catalog.BuiltInDefault.Package, new((WrssDocument[])[package.Document], []), catalog.Load(themeId, "1.0.0").Package);
                var theme = compiled.Theme ?? throw new InvalidOperationException("Invalid fixture theme");
                foreach (var width in new[] { 900, 420 })
                {
                    presenter.Width = width;
                    var baseView = new WidgetView(UI.Grid("anchor.page", (GridTrackDefinition[])[GridTrack.Pixel(40), GridTrack.Pixel(60), GridTrack.Star()],
                        (GridTrackDefinition[])[GridTrack.Pixel(140), GridTrack.Star(), GridTrack.Pixel(140)],
                        UI.Text("Choose a setting to inspect", "anchor.heading").InGrid(columnSpan: 3),
                        UI.Button("First option", "open.left", "anchor.left").Classes("anchor-button").InGrid(row: 1),
                        UI.Button("Last option", "open.right", "anchor.right").Classes("anchor-button").InGrid(row: 1, column: 2))
                        .InputScope("anchor.page").Classes("anchor-page"), "anchor.left");
                    var opened = false;
                    var anchored = true;
                    var removeRight = false;
                    var minimumSize = false;
                    string anchorId = "anchor.left";
                    var dispatched = new List<string>();
                    presenter.DispatchActionAsync = request =>
                    {
                        dispatched.Add(request.Action.ActionId);
                        if (request.Action.ActionId == "anchor.close") { opened = false; Publish(); }
                        return Task.CompletedTask;
                    };
                    Publish();
                    await Wait(() => Find<Button>("Widget.anchor.left") is { IsLoaded: true });
                    presenter.Enter(restoreNativeFocus: false);
                    foreach (var mode in new[] { true, false })
                    foreach (var id in new[] { "anchor.left", "anchor.right" })
                    {
                        anchored = mode;
                        anchorId = id;
                        var opener = Find<Button>("Widget." + id)!;
                        Check(opener.Focus(FocusState.Keyboard), "Opener accepts native focus before inspection");
                        Trace("before-open " + id + " " + presenter.FocusDiagnostics());
                        opened = true; Publish();
                        await Wait(() => Find<Button>("Widget.anchor.done") is { IsLoaded: true } done && ReferenceEquals(FocusManager.GetFocusedElement(XamlRoot), done));
                        var dialog = Find<FrameworkElement>("Widget.anchor.inspect")!;
                        await Wait(() => Inside(dialog));
                        var location = dialog.TransformToVisual(presenter).TransformPoint(new());
                        var origin = opener.TransformToVisual(presenter).TransformPoint(new());
                        if (anchored && width == 900)
                            Check(id == "anchor.left" ? location.X >= origin.X + opener.ActualWidth + 7 : location.X + dialog.ActualWidth <= origin.X - 7,
                                themeId + " places popover beside " + id);
                        Check(Inside(dialog), themeId + " keeps popover within " + width + " DIP widget");
                        Check(!opener.IsTabStop && !opener.IsHitTestVisible && opener.IsEnabled,
                            "Popover preserves background appearance while blocking its interaction");
                        opener.Focus(FocusState.Keyboard);
                        Check(!ReferenceEquals(FocusManager.GetFocusedElement(XamlRoot), opener), "Popover owns the active focus scope");
                        var close = Find<Button>("Widget.anchor.done")!;
                        var command = close.Command!;
                        if (anchored && width == 900 && id == "anchor.left") await Capture(Path.Combine(directory, themeId + ".png"));
                        if(Environment.GetCommandLineArgs().Contains("--native-modal-keyboard"))
                        {
                            NativeContextProbeInput.Escape();
                            await Wait(()=>!opened);
                            Check(true,"Native Escape dismisses the active "+(anchored?"anchored":"ordinary")+" modal");
                        }
                        else presenter.ActivateFocused();
                        Trace("after-close " + id + " " + presenter.FocusDiagnostics());
                        await Wait(() => ReferenceEquals(FocusManager.GetFocusedElement(XamlRoot), opener));
                        Check(true, (anchored ? "Anchored" : "Ordinary") + " dialog restores exact " + id + " opener without waiting for GotFocus");
                        Check(dispatched[^1] == "anchor.close", "Close dispatches through the existing modal action");
                        var count = dispatched.Count; command.Execute(null);
                        Check(dispatched.Count == count, "Retired popover action cannot dispatch");
                    }
                    if (Environment.GetCommandLineArgs().Contains("--native-modal-keyboard") && width == 900 &&
                        themeId == ThemeIdentity.BuiltInMonoInk)
                    {
                        var originalDispatch = presenter.DispatchActionAsync;
                        var firstDismissal = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                        var secondDismissal = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                        var attempts = 0;
                        presenter.DispatchActionAsync = request =>
                        {
                            Check(request.Action.ActionId == "anchor.close", "Pending Escape dispatch keeps the modal's declared dismiss action");
                            attempts++;
                            if (attempts == 1) return firstDismissal.Task;
                            if (attempts == 2) return secondDismissal.Task;
                            opened = false; Publish(); return Task.CompletedTask;
                        };
                        try
                        {
                            anchored = true; anchorId = "anchor.left";
                            Check(Find<Button>("Widget.anchor.left")!.Focus(FocusState.Keyboard),
                                "Pending Escape race establishes its actual left opener before opening");
                            opened = true; Publish();
                            await Wait(() => Find<Button>("Widget.anchor.done") is { } done && ReferenceEquals(FocusManager.GetFocusedElement(XamlRoot), done));
                            NativeContextProbeInput.Escape(); await Wait(() => attempts == 1);
                            Publish(); // A harmless snapshot must not reset same-modal coalescing.
                            NativeContextProbeInput.Escape(); await Task.Delay(80);
                            Check(attempts == 1, "Repeated Escape coalesces for the same pending modal across snapshots");
                            opened = false; Publish();
                            await Wait(() => ReferenceEquals(FocusManager.GetFocusedElement(XamlRoot), Find<Button>("Widget.anchor.left")));
                            opened = true; Publish(); // Reuse the ID, but create a distinct modal incarnation.
                            await Wait(() => Find<Button>("Widget.anchor.done") is { } done && ReferenceEquals(FocusManager.GetFocusedElement(XamlRoot), done));
                            NativeContextProbeInput.Escape(); await Wait(() => attempts == 2);
                            Check(true, "An old pending dismissal cannot suppress Escape in a reopened modal with the same ID");
                            firstDismissal.SetResult(); await Task.Delay(80);
                            NativeContextProbeInput.Escape(); await Task.Delay(80);
                            Check(attempts == 2, "Late completion of the old dismissal cannot clear the new modal's pending guard");
                            secondDismissal.SetResult(); await Task.Delay(80);
                            NativeContextProbeInput.Escape(); await Wait(() => attempts == 3 && !opened);
                            Check(true, "Current dismissal completion releases only its own Escape guard");
                            await Wait(() => ReferenceEquals(FocusManager.GetFocusedElement(XamlRoot), Find<Button>("Widget.anchor.left")));
                        }
                        finally
                        {
                            firstDismissal.TrySetResult(); secondDismissal.TrySetResult();
                            presenter.DispatchActionAsync = originalDispatch;
                        }
                    }
                    anchored = true; minimumSize = true; anchorId = "anchor.right"; opened = true; Publish();
                    await Wait(() => Find<Button>("Widget.anchor.done") is { IsLoaded: true } done && ReferenceEquals(FocusManager.GetFocusedElement(XamlRoot), done));
                    await Wait(() => Find<FrameworkElement>("Widget.anchor.inspect") is { } panel && panel.ActualWidth >= Math.Min(500, width - 32) - 1 && panel.ActualHeight >= 319 && Inside(panel));
                    Check(true, "Anchored placement respects authored minimum dimensions and widget bounds");
                    removeRight = true; Publish();
                    await Wait(() => Find<FrameworkElement>("Widget.anchor.inspect") is { ActualWidth: > 0 } dialog && Inside(dialog));
                    var fallback = Find<FrameworkElement>("Widget.anchor.inspect")!;
                    await Wait(() => Math.Abs(fallback.TransformToVisual(presenter).TransformPoint(new()).X - (presenter.ActualWidth - fallback.ActualWidth) / 2) < 1);
                    Check(true, "Missing anchor safely uses centered geometry");
                    presenter.Width = 240; presenter.Height = 180;
                    await Wait(() => Math.Abs(presenter.ActualWidth - 240) < 1 && Math.Abs(presenter.ActualHeight - 180) < 1 && Inside(fallback));
                    Check(fallback.ActualWidth <= 208 && fallback.ActualHeight <= 148, "Resize clamps the same open popover");
                    await presenter.SetPresentationActiveAsync(false);
                    await presenter.SetPresentationActiveAsync(true);
                    presenter.Enter(restoreNativeFocus: true);
                    await Wait(() => Find<Button>("Widget.anchor.done")?.IsLoaded == true);
                    Check(Inside(fallback), "Hide/reopen keeps the popover bounded");
                    opened = false; Publish();
                    await Wait(() => ReferenceEquals(FocusManager.GetFocusedElement(XamlRoot), Find<Button>("Widget.anchor.left")));
                    Check(true, "Removed opener returns to valid page fallback");
                    presenter.Height = 560;

                    void Publish()
                    {
                        var background = removeRight ? baseView with { Root = (ContainerElement)baseView.Root with
                            { Children = ((ContainerElement)baseView.Root).Children.Where(child => child.Id != "anchor.right").ToArray() } } : baseView;
                        var view = opened ? background.WithModal(new("anchor.inspect", "Inspect option",
                            UI.Stack("anchor.details", UI.Text("Widget-authored details stay beside the option being inspected.", "anchor.description"),
                                UI.Button("Done", "anchor.close", "anchor.done")), "anchor.done", "anchor.close")
                            { AnchoredPresentation = anchored ? new(new(anchorId)) : null }) : background;
                        var snapshot = view.CreateSnapshot("anchor.instance", ++sequence);
                        if (opened && minimumSize)
                        {
                            var panel = snapshot.Root.Children[1];
                            snapshot = snapshot with { Root = snapshot.Root with { Children = (ViewNode[])[snapshot.Root.Children[0],
                                panel with { StyleClasses = (string[])[.. panel.StyleClasses, "anchor-minimum"] }] } };
                        }
                        var styles = new Dictionary<string, BridgeNodeRenderStyles>();
                        void Visit(ViewNode node)
                        {
                            styles[node.Id] = Compute(theme, node);
                            foreach (var child in node.Children) Visit(child);
                        }
                        Visit(snapshot.Root);
                        var descriptor = new BridgeWidgetDescriptor { Id = "anchor", Name = "Anchor fixture", InstanceId = snapshot.WidgetInstanceId,
                            RuntimeGeneration = "anchor", PresentationGeneration = "anchor", Icon = WidgetGlyph.Settings, PackageContentDigest = "" };
                        presenter.Apply(new(new(descriptor.Id, descriptor.RuntimeGeneration, descriptor.PresentationGeneration, 1,
                            snapshot.WidgetInstanceId, snapshot.Sequence, snapshot.ActiveInputScopeId), descriptor, snapshot, styles));
                        presenter.UpdateLayout();
                    }
                }
            }
        }
        catch (Exception error) { failure = error; Trace("failure " + presenter.FocusDiagnostics()); }
        presenter.FocusTrace = null;
        await File.WriteAllTextAsync(Path.Combine(directory, "result.json"), JsonSerializer.Serialize(new { passed = failure is null, checks, trace, error = failure?.ToString() }));
        if (failure is not null) throw failure;

        bool Inside(FrameworkElement element)
        {
            var p = element.TransformToVisual(presenter).TransformPoint(new());
            return p.X >= 15 && p.Y >= 15 && p.X + element.ActualWidth <= presenter.ActualWidth - 15 && p.Y + element.ActualHeight <= presenter.ActualHeight - 15;
        }
        async Task Capture(string path)
        {
            var bitmap = new Microsoft.UI.Xaml.Media.Imaging.RenderTargetBitmap();
            await bitmap.RenderAsync(presenter);
            using var stream = new Windows.Storage.Streams.InMemoryRandomAccessStream();
            var encoder = await Windows.Graphics.Imaging.BitmapEncoder.CreateAsync(Windows.Graphics.Imaging.BitmapEncoder.PngEncoderId, stream);
            encoder.SetPixelData(Windows.Graphics.Imaging.BitmapPixelFormat.Bgra8, Windows.Graphics.Imaging.BitmapAlphaMode.Premultiplied,
                (uint)bitmap.PixelWidth, (uint)bitmap.PixelHeight, 96, 96, (await bitmap.GetPixelsAsync()).ToArray());
            await encoder.FlushAsync(); stream.Seek(0);
            using var reader = new Windows.Storage.Streams.DataReader(stream.GetInputStreamAt(0));
            await reader.LoadAsync((uint)stream.Size);
            var bytes = new byte[(int)stream.Size]; reader.ReadBytes(bytes); await File.WriteAllBytesAsync(path, bytes);
        }
    }
}
