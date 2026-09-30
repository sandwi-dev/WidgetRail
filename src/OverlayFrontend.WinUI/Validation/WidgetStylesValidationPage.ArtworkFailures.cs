using System.Runtime.InteropServices;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using WidgetRail.OverlayFrontend.WinUI.Presentation;
using WidgetRail.WidgetBridge;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

namespace WidgetRail.OverlayFrontend.WinUI.Validation;

internal sealed partial class WidgetStylesValidationPage
{
    private async Task ArtworkFailureIsolationAsync()
    {
        var page = new WidgetViewPresenter { Width = 320, Height = 200 };
        host.Children.Add(page);
        var errors = new List<Exception>();
        var requests = 0;
        var actions = 0;
        var bytes = await ArtworkPngAsync(120, 80);
        page.Failed = errors.Add;
        page.DispatchActionAsync = _ => { ++actions; return Task.CompletedTask; };
        page.ResolveArtworkAsync = (handle, _) =>
        {
            ++requests;
            return handle switch
            {
                "http400" => Task.FromException<WidgetEncodedArtwork?>(new COMException("Fixture HTTP 400", unchecked((int)0x80190190))),
                "missing" => Task.FromException<WidgetEncodedArtwork?>(new IOException("Fixture resource unavailable")),
                "retired-indexed" => Task.FromException<WidgetEncodedArtwork?>(new WidgetRail.WidgetPresentationSession.WidgetPresentationSessionException(
                    "request_failed", "Indexed artwork lease is unavailable.")),
                "unexpected" => Task.FromException<WidgetEncodedArtwork?>(new InvalidOperationException("Fixture host defect")),
                "corrupt" => Task.FromResult<WidgetEncodedArtwork?>(new(WidgetArtworkContentType.Png, new byte[] { 1, 2, 3, 4 })),
                _ => Task.FromResult<WidgetEncodedArtwork?>(new(WidgetArtworkContentType.Png, bytes)),
            };
        };
        try
        {
            Apply("ready");
            await Wait(() => Image() is { Source: BitmapSource { PixelWidth: > 0 } });
            var image = Image()!;
            var previous = image.Source;
            var button = Find<Button>("Widget.resource.button", page)!;
            button.Focus(FocusState.Keyboard);
            foreach (var handle in new[] { "http400", "missing", "corrupt", "retired-indexed" })
            {
                var failures = NativeArtworkCounters.Snapshot().Failed;
                Apply(handle);
                await Wait(() => NativeArtworkCounters.Snapshot().Failed > failures);
                Check(errors.Count == 0 && ReferenceEquals(previous, image.Source) && button.FocusState != FocusState.Unfocused,
                    handle + " artwork failure retains previous pixels and focused controls without widget recovery");
                var count = requests;
                Apply(handle);
                await Task.Delay(50);
                Check(requests == count, handle + " is not retried on every unrelated snapshot");
                var beforeAction = actions;
                button.Command!.Execute(null);
                await Wait(() => actions == beforeAction + 1);
            }
            Apply("recovered");
            await Wait(() => Image()?.Source is BitmapSource && !ReferenceEquals(previous, image.Source));
            Check(errors.Count == 0, "replacement artwork loads normally after a failed optional image");
            Apply("unexpected");
            await Wait(() => errors.Count == 1);
            Check(errors[0] is InvalidOperationException, "unexpected host defects still reach the normal failure boundary");
        }
        finally { await page.DisposeAsync(); host.Children.Remove(page); }

        WidgetArtworkView? Image() => Find<WidgetArtworkView>("Widget.resource.image", page);
        void Apply(string handle)
        {
            var root = new ViewNode { Id = "resource.root", Kind = ViewNodeKind.Stack, Children = (ViewNode[])[
                new() { Id = "resource.image", Kind = ViewNodeKind.Image, ArtworkHandle = handle, ImageFit = ImageFit.Cover, AccessibilityLabel = "Artwork" },
                new() { Id = "resource.button", Kind = ViewNodeKind.Button, Text = "Playback remains available", ActionId = "play" }] };
            page.Apply(CreateFrame(root, new Dictionary<string, BridgeNodeRenderStyles> {
                ["resource.image"] = Compute("image { width: 120px; height: 80px; }", "resource.image", "image") }));
        }
    }
}
