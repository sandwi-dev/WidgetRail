using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using WidgetRail.PlatformSettings;
using WidgetRail.WidgetBridge;
using WidgetRail.WidgetProtocol;

namespace WidgetRail.OverlayFrontend.WinUI.Validation;

internal sealed partial class WidgetStylesValidationPage
{
    private async Task PlayniteAwardAsync(string path)
    {
        Exception? failure = null;
        try
        {
            using var file = JsonDocument.Parse(await File.ReadAllBytesAsync(path));
            var snapshot = SnapshotJson.Deserialize(System.Text.Encoding.UTF8.GetBytes(file.RootElement.GetProperty("snapshot").GetRawText()));
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
            var styles = file.RootElement.GetProperty("renderStyles").Deserialize<Dictionary<string, BridgeNodeRenderStyles>>(options)!;
            // Keep the real shelf card and its context affordance. Its vertical
            // navigation targets belong to the surrounding Home fixture.
            var card = HeaderFixtureNodes(snapshot.Root).Single(node => node.Kind == ViewNodeKind.ActionSurface &&
                node.StyleClasses.Contains("playing-trophy-case")) with { Focus = null };
            var root = new ViewNode { Id = "award-test", Kind = ViewNodeKind.Row, Children = (ViewNode[])[card] };
            foreach (var scale in new[] { 1d, 1.5d })
            {
                presenter.ApplyAppearance(AppearanceSettings.Default with { TextScale = scale, Motion = MotionPreference.Reduced }, false);
                presenter.Apply(CreateFrame(root, styles));
                await Wait(() => Find<Control>("Widget." + card.Id) is { IsLoaded: true, ActualWidth: > 0 });
                var tile = Find<Control>("Widget." + card.Id)!;
                Check(tile.Focus(FocusState.Keyboard), $"Zero-unlock card accepts focus at {scale:P0}");
                var label = Microsoft.UI.Xaml.Automation.AutomationProperties.GetName(tile);
                Check(label.Contains("0 of 1 unlocked") && label.Contains("No unlocks yet"), $"Accessible name includes zero-unlock count at {scale:P0}");
                await Task.Delay(180);
                await CaptureEditorBitmapAsync(tile, Path.ChangeExtension(path, scale == 1 ? "normal.png" : "large.png"));
            }
        }
        catch (Exception error) { failure = error; }
        await File.WriteAllTextAsync(Path.ChangeExtension(path, "result.json"),
            JsonSerializer.Serialize(new { passed = failure is null, checks, error = failure?.ToString() }));
        if (failure is not null) throw new InvalidOperationException("Achievement card validation failed.", failure);

    }

    private async Task PlayniteHeaderAsync(string path)
    {
        Exception? failure = null;
        try
        {
            using var file = JsonDocument.Parse(await File.ReadAllBytesAsync(path));
            var snapshot = SnapshotJson.Deserialize(System.Text.Encoding.UTF8.GetBytes(file.RootElement.GetProperty("snapshot").GetRawText()));
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
            var styles = file.RootElement.GetProperty("renderStyles").Deserialize<Dictionary<string, BridgeNodeRenderStyles>>(options)!;
            App.Window.AppWindow.ResizeClient(new(1400, 1000));
            host.Children.Clear();
            var layout = new Grid { Margin = new(16) };
            layout.RowDefinitions.Add(new() { Height = GridLength.Auto });
            layout.RowDefinitions.Add(new() { Height = new(1, GridUnitType.Star) });
            layout.Children.Add(status); layout.Children.Add(presenter); Grid.SetRow(presenter, 1); Content = layout;
            presenter.ApplyAppearance(AppearanceSettings.Default, false);
            var descriptor = new BridgeWidgetDescriptor
            {
                Id = "playnite.header", Name = "Playnite header", InstanceId = snapshot.WidgetInstanceId,
                RuntimeGeneration = "header-fixture", PresentationGeneration = "header-fixture", Icon = WidgetGlyph.Play, PackageContentDigest = "",
            };
            string? action = null;
            presenter.DispatchActionAsync = request => { action = request.Action.ActionId; return Task.CompletedTask; };
            presenter.Apply(new(new(descriptor.Id, descriptor.RuntimeGeneration, descriptor.PresentationGeneration, 1,
                descriptor.InstanceId, snapshot.Sequence, snapshot.ActiveInputScopeId), descriptor, snapshot, styles));
            var captureNode = HeaderFixtureNodes(snapshot.Root).Single(node => node.Text == "Capture settings");
            var tab = Find<Button>("Widget." + captureNode.Focus!.Left);
            await Wait(() => Find<Button>("Widget." + captureNode.Id) is { IsLoaded: true, ActualWidth: > 0 });
            tab = Find<Button>("Widget." + captureNode.Focus.Left)!;
            var capture = Find<Button>("Widget." + captureNode.Id)!;
            var refresh = Find<Button>("Widget." + captureNode.Focus.Right)!;
            Check(presenter.FocusHostTarget("playnite-library.achievement-hub.search"), "Search is a native focus target");
            presenter.MoveFocus(FocusNavigationDirection.Up);
            await Wait(() => ReferenceEquals(FocusManager.GetFocusedElement(presenter.XamlRoot), tab));
            Check(true, "Up from achievement search returns to the Achievements tab");
            foreach (var (direction, expected) in new[]
            {
                (FocusNavigationDirection.Right, capture), (FocusNavigationDirection.Right, refresh),
                (FocusNavigationDirection.Left, capture), (FocusNavigationDirection.Left, tab),
            })
            {
                presenter.MoveFocus(direction);
                await Wait(() => ReferenceEquals(FocusManager.GetFocusedElement(presenter.XamlRoot), expected));
                Check(true, "Native header traversal reaches " + expected.Content);
            }
            presenter.MoveFocus(FocusNavigationDirection.Right);
            await presenter.HandleControllerButtonAsync(ControllerButton.A, origin: WidgetRail.WidgetSdk.ControllerInputOrigin.AccessibilityAutomation);
            await Wait(() => action == captureNode.ActionId);
            await presenter.HandleControllerButtonAsync(ControllerButton.A, ControllerEventPhase.Released, WidgetRail.WidgetSdk.ControllerInputOrigin.AccessibilityAutomation);
            Check(true, "A activates the actual Capture settings action");
        }
        catch (Exception error) { failure = error; }
        await File.WriteAllTextAsync(path + ".native.json", JsonSerializer.Serialize(new { passed = failure is null, checks, error = failure?.ToString() }));
        if (failure is not null) throw failure;

    }
    private static IEnumerable<ViewNode> HeaderFixtureNodes(ViewNode node)
    {
        yield return node;
        foreach (var child in node.Children)
            foreach (var descendant in HeaderFixtureNodes(child)) yield return descendant;
    }
}
