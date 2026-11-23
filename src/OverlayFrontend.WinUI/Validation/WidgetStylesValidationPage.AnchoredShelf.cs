using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using WidgetRail.PlatformSettings;
using WidgetRail.WidgetBridge;
using WidgetRail.WidgetProtocol;

namespace WidgetRail.OverlayFrontend.WinUI.Validation;

internal sealed partial class WidgetStylesValidationPage
{
    private async Task AnchoredShelfAsync(string directory)
    {
        Directory.CreateDirectory(directory);
        Exception? failure = null;
        var samples = new List<object>();
        try
        {
            presenter.Width = 600;
            presenter.Height = 270;
            presenter.ApplyAppearance(AppearanceSettings.Default with { Motion = MotionPreference.Reduced }, false);
            ViewNode Button(string id) => new() { Kind = ViewNodeKind.Button, Id = id, ActionId = id, Text = id };
            var ids = Enumerable.Range(0, 9).Select(index => "card" + index).ToArray();
            var styles = new Dictionary<string, BridgeNodeRenderStyles>();
            foreach (var id in ids.Append("incoming"))
            {
                var width = id is "card2" or "card5" ? 224 : 148;
                styles[id] = Compute($"#x {{ width: {width}px; height: 84px; }}", "x", "button");
            }
            void Publish(IEnumerable<string> cards)
            {
                var tiles = cards.Select(id => Button(id) with { Focus = new(Up: "header", Down: "posters") }).ToArray();
                var row = new ViewNode { Id = "cards", Kind = ViewNodeKind.Row, Children = tiles };
                var shelf = new ViewNode { Id = "shelf", Kind = ViewNodeKind.Scroll, ScrollAxis = ScrollAxis.Horizontal,
                    DirectionalAnchor = new(FocusAnchorPoint.Leading, 10), Children = (ViewNode[])[row] };
                presenter.Apply(CreateFrame(new() { Id = "home", Kind = ViewNodeKind.Stack,
                    Children = (ViewNode[])[Button("header"), shelf, Button("posters")] }, styles));
            }
            bool Focused(string id) => Find<Button>("Widget." + id) is { } control &&
                ReferenceEquals(FocusManager.GetFocusedElement(XamlRoot), control);
            Publish(ids.Take(1));
            await Wait(() => Find<Button>("Widget.card0") is { IsLoaded: true, ActualWidth: > 0 });
            var viewport = Find<ScrollViewer>("Widget.shelf")!;
            Check(Find<Button>("Widget.card0")!.Focus(FocusState.Keyboard) && viewport.ScrollableWidth == 0,
                "One shelf card remains reachable without horizontal overflow");
            Check(presenter.MoveFocus(FocusNavigationDirection.Up) && Focused("header"), "Up from the only card reaches the header");

            Publish(ids);
            await Wait(() => viewport.ScrollableWidth > 500);
            Find<Button>("Widget.card0")!.Focus(FocusState.Keyboard);
            for (var index = 1; index <= 3; index++)
            {
                presenter.MoveFocus(FocusNavigationDirection.Right, isRepeat: index > 1);
                var expected = ids[index];
                await Wait(() => Focused(expected));
            }
            var selected = Find<Button>("Widget.card3")!;
            double Leading() => selected.TransformToVisual(viewport).TransformPoint(new()).X;
            for (var sample = 0; sample < 12; sample++)
            {
                samples.Add(new { sample, leading = Leading(), viewport.HorizontalOffset, viewport.ViewportWidth, viewport.ScrollableWidth });
                await Task.Delay(25);
            }
            await Wait(() => Math.Abs(Leading() - viewport.ViewportWidth * .1) < 3);
            Check(true, "Held Right advances through mixed-width cards to the leading inset");
            Publish(ids.Prepend("incoming"));
            await Wait(() => Find<Button>("Widget.incoming") is { IsLoaded: true, ActualWidth: > 0 });
            Check(Focused("card3") && ReferenceEquals(selected, Find<Button>("Widget.card3")),
                "Earlier arriving card preserves the focused card and native control");
            var before = viewport.HorizontalOffset;
            using (var wheel = new NativeWheelProbe(viewport))
            {
                App.Window.Activate();
                wheel.Send(120, horizontal: true);
                await Wait(() => Math.Abs(viewport.HorizontalOffset - before) > 2);
            }
            await Task.Delay(350);
            Check(Focused("card3") && Math.Abs(viewport.HorizontalOffset - before) > 2,
                "Horizontal mouse wheel moves the shelf freely while preserving keyboard focus");
            Check(presenter.MoveFocus(FocusNavigationDirection.Down) && Focused("posters"), "Down from the scrolled shelf reaches posters");
        }
        catch (Exception error) { failure = error; }
        await File.WriteAllTextAsync(Path.Combine(directory, "result.json"),
            System.Text.Json.JsonSerializer.Serialize(new { passed = failure is null, checks, samples, error = failure?.ToString() }));
        if (failure is not null) throw new InvalidOperationException("Anchored shelf validation failed.", failure);
    }
}
