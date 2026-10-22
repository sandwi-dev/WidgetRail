using System.Runtime.InteropServices.WindowsRuntime;
using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using WidgetRail.OverlayFrontend.WinUI.Presentation;
using WidgetRail.PlatformSettings;
using WidgetRail.WidgetBridge;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetStyling;

namespace WidgetRail.OverlayFrontend.WinUI.Validation;

internal sealed partial class WidgetStylesValidationPage
{
    private async Task CaptureDeleteAsync(string directory)
    {
        Exception? failure = null;
        var root = new Grid(); var page = new WidgetViewPresenter(); root.Children.Add(page);
        PresentationLayers.Attach(root); presenter.Visibility = Visibility.Collapsed; host.Children.Add(root);
        var catalog = new ThemeCatalog(new PlatformSettingsPaths(directory));
        var package = WrssParser.Parse(File.ReadAllText(Path.Combine(directory, "capture.wrss")), "capture.wrss");
        var ready = SnapshotJson.Deserialize(File.ReadAllBytes(Path.Combine(directory, "confirmation.json")));
        var busy = SnapshotJson.Deserialize(File.ReadAllBytes(Path.Combine(directory, "deleting.json")));
        // Preserve the real dialog; substitute a harmless background for the media
        // player. All actions below are observed locally and never delete a file.
        var background = ready.Root.Children[0] with { Children = (ViewNode[])[new ViewNode { Id = "capture-library.delete", Kind = ViewNodeKind.Button, Text = "Delete capture", ActionId = "capture-library.delete" }], Transition = null };
        ready = ready with { Root = ready.Root with { Children = (ViewNode[])[background, ready.Root.Children[1]] } };
        busy = busy with { Root = busy.Root with { Children = (ViewNode[])[background, busy.Root.Children[1]] } };
        var scale = XamlRoot.RasterizationScale;
        App.Window.AppWindow.Resize(new((int)(900 * scale), (int)(760 * scale)));
        try
        {
            foreach (var themeId in new[] { ThemeIdentity.BuiltInMonoInk, ThemeIdentity.BuiltInMineralLight })
            foreach (var textScale in new[] { 1d, 1.5d })
            {
                root.Width = page.Width = textScale == 1 ? 800 : 480; root.Height = page.Height = 560;
                page.ApplyAppearance(AppearanceSettings.Default with { TextScale = textScale, Motion = MotionPreference.Reduced }, false);
                var theme = ThemeLayerCompiler.Compile(catalog.BuiltInDefault.Package,
                    new((WrssDocument[])[package.Document], []), catalog.Load(themeId, "1.0.0").Package).Theme!;
                var current = ready; var deleted = 0; var cancelled = 0;
                page.DispatchActionAsync = request =>
                {
                    if (request.Action.ActionId == "capture-library.delete.confirm") ++deleted;
                    if (request.Action.ActionId == "capture-library.delete.cancel")
                    {
                        ++cancelled;
                        current = ready with { Root = background, ActiveInputScopeId = background.InputScopeId ?? background.Id, InitialFocusId = "capture-library.delete" };
                        Publish();
                    }
                    return Task.CompletedTask;
                };
                Publish();
                await Wait(() => Find<Button>("Widget.capture-library.delete.cancel", root) is { IsLoaded: true, ActualWidth: > 0 });
                var cancel = Find<Button>("Widget.capture-library.delete.cancel", root)!;
                var delete = Find<Button>("Widget.capture-library.delete.confirm", root)!;
                await Wait(() => ReferenceEquals(FocusManager.GetFocusedElement(XamlRoot), cancel));
                var dialog = Find<FrameworkElement>("Widget.capture-library.confirm", root)!;
                Check(dialog.ActualWidth <= 561 && dialog.ActualHeight <= 321, "Capture confirmation remains compact and viewport bounded");
                Check(Find<Button>("Widget.capture-library.confirm.close", root) is null, "No duplicate Close action");
                var footer = delete.TransformToVisual(dialog).TransformPoint(new(0, 0));
                Check(footer.Y >= 0 && footer.Y + delete.ActualHeight <= dialog.ActualHeight + 1, "Delete footer stays inside the dialog at large text");
                if (themeId == ThemeIdentity.BuiltInMonoInk && textScale == 1) await Capture();
                page.MoveFocus(FocusNavigationDirection.Right);
                await Wait(() => ReferenceEquals(FocusManager.GetFocusedElement(XamlRoot), delete));
                await page.HandleControllerButtonAsync(ControllerButton.A);
                Check(deleted == 1, "Right then A reaches Delete exactly once");
                current = busy; Publish();
                await page.HandleControllerButtonAsync(ControllerButton.A);
                Check(deleted == 1, "Busy deletion rejects duplicate A");
                current = ready; Publish();
                await Wait(() => page.IsGuidePresentationReady(requireFocus: true));
                page.MoveFocus(FocusNavigationDirection.Left);
                await Wait(() => ReferenceEquals(FocusManager.GetFocusedElement(XamlRoot), cancel));
                await page.HandleControllerButtonAsync(ControllerButton.A);
                await Wait(() => Find<FrameworkElement>("Widget.capture-library.confirm", root) is null);
                Check(cancelled == 1, "Cancel dismisses without deleting");

                void Publish()
                {
                    var snapshot = current with { Sequence = ++sequence };
                    var styles = new Dictionary<string, BridgeNodeRenderStyles>();
                    void Visit(ViewNode node) { styles[node.Id] = Compute(theme, node); foreach (var child in node.Children) Visit(child); }
                    Visit(snapshot.Root);
                    foreach (var role in new[] { "body", "canvas", "panel", "hint", "tray-item" }) styles[role] = Compute(theme, role, role, []);
                    var descriptor = new BridgeWidgetDescriptor { Id = "capture-delete", Name = "Capture confirmation fixture", InstanceId = snapshot.WidgetInstanceId,
                        RuntimeGeneration = "capture-delete", PresentationGeneration = "capture-delete", Icon = WidgetGlyph.Fullscreen, PackageContentDigest = "" };
                    page.Apply(new(new(descriptor.Id, descriptor.RuntimeGeneration, descriptor.PresentationGeneration, 1,
                        snapshot.WidgetInstanceId, snapshot.Sequence, snapshot.ActiveInputScopeId ?? snapshot.Root.Id), descriptor, snapshot, styles));
                    page.UpdateLayout();
                }
            }
        }
        catch (Exception error) { failure = error; }
        finally { await page.DisposeAsync(); host.Children.Remove(root); }
        await File.WriteAllTextAsync(Path.Combine(directory, "native-result.json"), JsonSerializer.Serialize(new { passed = failure is null, checks, error = failure?.ToString() }));
        if (failure is not null) throw failure;
        async Task Capture()
        {
            var bitmap = new Microsoft.UI.Xaml.Media.Imaging.RenderTargetBitmap(); await bitmap.RenderAsync(root);
            using var stream = new Windows.Storage.Streams.InMemoryRandomAccessStream();
            var encoder = await Windows.Graphics.Imaging.BitmapEncoder.CreateAsync(Windows.Graphics.Imaging.BitmapEncoder.PngEncoderId, stream);
            encoder.SetPixelData(Windows.Graphics.Imaging.BitmapPixelFormat.Bgra8, Windows.Graphics.Imaging.BitmapAlphaMode.Premultiplied,
                (uint)bitmap.PixelWidth, (uint)bitmap.PixelHeight, 96, 96, (await bitmap.GetPixelsAsync()).ToArray());
            await encoder.FlushAsync(); stream.Seek(0);
            using var output = File.Create(Path.Combine(directory, "native-confirmation.png")); await stream.AsStreamForRead().CopyToAsync(output);
        }
    }
}
