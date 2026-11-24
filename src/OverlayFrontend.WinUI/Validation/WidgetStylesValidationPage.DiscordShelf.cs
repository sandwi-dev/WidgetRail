using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using WidgetRail.OverlayFrontend.WinUI.Presentation;
using WidgetRail.PlatformSettings;
using WidgetRail.WidgetBridge;
using WidgetRail.WidgetPresentationSession;
using WidgetRail.WidgetProtocol;

namespace WidgetRail.OverlayFrontend.WinUI.Validation;

internal sealed partial class WidgetStylesValidationPage
{
    private async Task DiscordShelfAsync(string directory)
    {
        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        var descriptor = new BridgeWidgetDescriptor { Id = "playnite", Name = "Playnite", InstanceId = "launcher.test",
            RuntimeGeneration = "fixture", PresentationGeneration = "fixture", Icon = WidgetGlyph.Play, PackageContentDigest = "" };
        async Task Apply(string name)
        {
            using var file = JsonDocument.Parse(await File.ReadAllBytesAsync(Path.Combine(directory, name + ".json")));
            var snapshot = SnapshotJson.Deserialize(System.Text.Encoding.UTF8.GetBytes(file.RootElement.GetProperty("snapshot").GetRawText())) with { Sequence = ++sequence };
            var styles = file.RootElement.GetProperty("renderStyles").Deserialize<Dictionary<string, BridgeNodeRenderStyles>>(options)!;
            // Other Home sections use a live Bridge range source. This fixture drives the authored shelf and modal only.
            snapshot = snapshot with { Root = OmitIndexedSections(snapshot.Root) };
            presenter.Apply(new(new(descriptor.Id, descriptor.RuntimeGeneration, descriptor.PresentationGeneration, 1,
                descriptor.InstanceId, snapshot.Sequence, snapshot.ActiveInputScopeId), descriptor, snapshot, styles));
            presenter.UpdateLayout();
        }
        static ViewNode OmitIndexedSections(ViewNode node) => node.Kind == ViewNodeKind.IndexedCollection
            ? new() { Id = node.Id, Kind = ViewNodeKind.Stack }
            : node with { Children = node.Children.Select(OmitIndexedSections).ToArray() };
        const string card = "Widget.playnite.playing.saved-00000.discord";
        var iconPath = Shell.FrontendArguments.Value(Environment.GetCommandLineArgs(), "--discord-shelf-icon");
        if (iconPath is not null) presenter.ResolvePackageIconAsync = (asset, _) =>
        {
            if (asset.AssetId != "playnite.discord") throw new IOException("Fixture icon unavailable");
            var bytes = File.ReadAllBytes(iconPath);
            return Task.FromResult(new WidgetPresentationPackageIcon(asset.AssetId,
                Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes)), bytes));
        };
        App.Window.AppWindow.ResizeClient(new(1250, 840));
        presenter.Width = 930; presenter.Height = 620;
        try
        {
            await Apply("two");
            await Wait(() => Find<Control>(card) is { ActualWidth: > 0 });
            var original = Find<Control>(card)!;
            await Task.Delay(450);
            Check(original.Focus(FocusState.Keyboard), "Focus enters the Discord card");
            await Task.Delay(300);
            Check(ReferenceEquals(FocusManager.GetFocusedElement(XamlRoot), original), "Native focus settled on Discord before the update");
            await CaptureEditorBitmapAsync(original, Path.Combine(directory,"compact.png"));
            var width = original.ActualWidth;
            await Apply("3");
            await Wait(() => original.ActualWidth > width);
            Check(ReferenceEquals(original, Find<Control>(card)), "A third friend expands the same native control");
            await Wait(() => ReferenceEquals(FocusManager.GetFocusedElement(XamlRoot), original));
            Check(true, "Discord retains focus after the roster update");
            await CaptureEditorBitmapAsync(original, Path.Combine(directory,"wide.png"));
            Check(presenter.MoveFocus(FocusNavigationDirection.Right), "Right moves to the next shelf card");
            var neighbor = FocusManager.GetFocusedElement(XamlRoot);
            Check(neighbor is Control && !ReferenceEquals(neighbor, original), "The neighboring card receives focus");
            await Apply("2");
            await Wait(() => original.ActualWidth == width);
            Check(ReferenceEquals(neighbor, FocusManager.GetFocusedElement(XamlRoot)), "The neighboring card retains focus during shrink");
            await Apply("call");
            await Task.Delay(300);
            var adjacent = (Control)neighbor!;
            var position = adjacent.TransformToVisual(presenter).TransformPoint(default);
            Check(ReferenceEquals(adjacent, FocusManager.GetFocusedElement(XamlRoot)) && position.X < presenter.ActualWidth && position.X + adjacent.ActualWidth > 0,
                "Expanding beside the focused end card keeps it in the shelf viewport");
            Check(original.Focus(FocusState.Keyboard), "Focus returns to Discord for the call");
            await Task.Delay(350);
            await CaptureEditorBitmapAsync(original, Path.Combine(directory,"call.png"));
            await Apply("panel");
            await Wait(() => Find<Control>(card + ".row.100") is { ActualWidth: > 0 });
            var panel = Find<FrameworkElement>(card + ".panel")!;
            await Task.Delay(350);
            await CaptureEditorBitmapAsync(panel, Path.Combine(directory,"panel.png"));
            await Apply("call");
            await Wait(() => original.FocusState != FocusState.Unfocused);
            Check(ReferenceEquals(original, Find<Control>(card)), "Dismissing the panel restores the original shelf control");
            presenter.ApplyAppearance(AppearanceSettings.Default with { TextScale = 1.5, Motion = MotionPreference.Reduced }, false);
            WidgetViewPresenter.SetHighContrastStyleOverride(true);
            presenter.Width = 560;
            await Apply("long-panel");
            await Wait(() => Find<Control>(card + ".close") is { IsLoaded: true, ActualWidth: > 0 });
            var footer = Find<Control>(card + ".close")!;
            Check(footer.Focus(FocusState.Keyboard), "Close remains focusable at 150% text");
            await Task.Delay(250);
            await CaptureEditorBitmapAsync(Find<FrameworkElement>(card + ".panel")!, Path.Combine(directory,"large-text.png"));
            await Apply("empty");
            await Apply("empty-panel");
            await Wait(() => Find<Control>(card + ".close")?.FocusState != FocusState.Unfocused && Find<Control>(card + ".close") is not null);
            Check(true, "An empty activity panel enters Close");
            WidgetViewPresenter.SetHighContrastStyleOverride(null);
            await File.WriteAllTextAsync(Path.Combine(directory,"native-result.json"),JsonSerializer.Serialize(new { passed = true, checks }));
        }
        catch (Exception error)
        {
            await File.WriteAllTextAsync(Path.Combine(directory,"native-result.json"),JsonSerializer.Serialize(new { passed = false, checks, error = error.ToString() }));
            throw;
        }
    }
}
