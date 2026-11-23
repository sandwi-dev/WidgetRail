using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using WidgetRail.PlatformSettings;
using WidgetRail.WidgetBridge;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetStyling;

namespace WidgetRail.OverlayFrontend.WinUI.Validation;

internal sealed partial class WidgetStylesValidationPage
{
    private async Task CaptureSelectionAsync(string directory)
    {
        Exception? failure = null;
        try
        {
            var snapshot = SnapshotJson.Deserialize(File.ReadAllBytes(Path.Combine(directory, "selection.json")));
            // Exercise the actual action subtree and its production styles. The
            // standalone fixture has no indexed bridge session for gallery rows.
            var actions = Nodes(snapshot.Root).Single(node => node.Id == "capture-library.selection.actions");
            snapshot = snapshot with { Root = snapshot.Root with { Children = (ViewNode[])[actions], Transition = null },
                InitialFocusId = "capture-library.batch.share" };
            var catalog = new ThemeCatalog(new PlatformSettingsPaths(directory));
            var package = WrssParser.Parse(File.ReadAllText(Path.Combine(directory, "capture.wrss")), "capture.wrss");
            host.Children.Clear();
            var layout = new Grid { Margin = new(16) };
            layout.RowDefinitions.Add(new() { Height = GridLength.Auto });
            layout.RowDefinitions.Add(new() { Height = new(1, GridUnitType.Star) });
            layout.Children.Add(status); layout.Children.Add(presenter); Grid.SetRow(presenter, 1); Content = layout;
            App.Window.AppWindow.ResizeClient(new(1200, 1000));
            var descriptor = new BridgeWidgetDescriptor
            {
                Id = "capture.selection", Name = "Capture selection", InstanceId = snapshot.WidgetInstanceId,
                RuntimeGeneration = "batch-fixture", PresentationGeneration = "batch-fixture", Icon = WidgetGlyph.Fullscreen, PackageContentDigest = "",
            };
            string? action = null;
            presenter.DispatchActionAsync = request => { action = request.Action.ActionId; return Task.CompletedTask; };
            foreach (var (width, textScale) in new[] { (1040d, 1d), (420d, 1d), (420d, 1.5d) })
            {
                presenter.Width = width; presenter.Height = 680;
                presenter.ApplyAppearance(AppearanceSettings.Default with { TextScale = textScale, Motion = MotionPreference.Reduced }, false);
                var theme = ThemeLayerCompiler.Compile(catalog.BuiltInDefault.Package,
                    new((WrssDocument[])[package.Document], []), catalog.Load(ThemeIdentity.BuiltInMonoInk, "1.0.0").Package).Theme!;
                var styles = new Dictionary<string, BridgeNodeRenderStyles>();
                void Visit(ViewNode node) { styles[node.Id] = Compute(theme, node); foreach (var child in node.Children) Visit(child); }
                Visit(snapshot.Root);
                snapshot = snapshot with { Sequence = ++sequence };
                presenter.Apply(new(new(descriptor.Id, descriptor.RuntimeGeneration, descriptor.PresentationGeneration, 1,
                    descriptor.InstanceId, snapshot.Sequence, snapshot.ActiveInputScopeId), descriptor, snapshot, styles));
                await Wait(() => Find<Button>("Widget.capture-library.batch.share") is { IsLoaded: true, ActualWidth: > 0 });
                await Task.Delay(100);
                var share = Find<Button>("Widget.capture-library.batch.share")!;
                var favorite = Find<Button>("Widget.capture-library.batch.favorite")!;
                var viewport = Find<ScrollViewer>("Widget.capture-library.selection.actions")!;
                Check(viewport.ActualHeight <= Math.Max(share.ActualHeight, favorite.ActualHeight) + 18 &&
                    favorite.ActualHeight <= 64 * textScale, "Batch toolbar stays content-height in a tall viewport");
                Check(share.ActualWidth < 80 * textScale, "Share stays icon-sized at " + width + " DIPs / " + textScale + " text scale");
                var gap = favorite.TransformToVisual(viewport).TransformPoint(new(0, 0)).X -
                    share.TransformToVisual(viewport).TransformPoint(new(share.ActualWidth, 0)).X;
                Check(gap is >= 0 and <= 20, "No empty grid column after Share");
                Check(share.Focus(FocusState.Keyboard), "Share is a native focus target");
                foreach (var id in new[] { "favorite", "copy", "hide", "recycle" })
                {
                    var expected = Find<Button>("Widget.capture-library.batch." + id)!;
                    // Horizontal scrolling may reveal before moving focus, as in production.
                    for (var move = 0; move < 25 && !ReferenceEquals(FocusManager.GetFocusedElement(presenter.XamlRoot), expected); move++)
                    { presenter.MoveFocus(FocusNavigationDirection.Right, isRepeat: move > 0); await Task.Delay(40); }
                    Check(ReferenceEquals(FocusManager.GetFocusedElement(presenter.XamlRoot), expected), "Selection navigation reaches " + id);
                    var bounds = expected.TransformToVisual(viewport).TransformBounds(new(0, 0, expected.ActualWidth, expected.ActualHeight));
                    Check(bounds.Left >= -1 && bounds.Right <= viewport.ViewportWidth + 1, "Focused " + id + " is revealed");
                }
                action = null;
                await presenter.HandleControllerButtonAsync(ControllerButton.A);
                await Wait(() => action == "capture-library.batch.recycle");
                await presenter.HandleControllerButtonAsync(ControllerButton.A, ControllerEventPhase.Released);
                Check(true, "Delete dispatches the review action without touching files");
            }
        }
        catch (Exception error) { failure = error; }
        await File.WriteAllTextAsync(Path.Combine(directory, "native.json"), JsonSerializer.Serialize(new { passed = failure is null, checks, error = failure?.ToString() }));
        if (failure is not null) throw failure;

        static IEnumerable<ViewNode> Nodes(ViewNode node)
        {
            yield return node;
            foreach (var child in node.Children)
                foreach (var descendant in Nodes(child)) yield return descendant;
        }
    }
}
