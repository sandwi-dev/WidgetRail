using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using WidgetRail.OverlayFrontend.WinUI.Collections;
using WidgetRail.OverlayFrontend.WinUI.Presentation;
using WidgetRail.WidgetProtocol;

namespace WidgetRail.OverlayFrontend.WinUI.Validation;

internal sealed partial class IndexedWidgetValidationPage
{
    private async Task ValidateIndexedPreviewsAsync(string path)
    {
        var checks = new List<string>();
        var theme = new NativePopupTheme();
        theme.Attach(presenter);
        // Match the production overlay root: portals must not participate in
        // the fixture toolbar's Auto row measurement.
        var layout = (Grid)Content;
        Content = null;
        var overlayRoot = new Grid(); overlayRoot.Children.Add(layout);
        Content = overlayRoot;
        PresentationLayers.Attach(overlayRoot);
        try
        {
            await Wait(() => View() is { IsLoaded: true });
            await Send("preview-mode");
            await Wait(() => FocusId() == "Widget.items.Item.75" && presenter.IsFocusPreviewOpen && PreviewText() == "Summary 150:75");
            var source = Source();
            var collection = Collection();
            var target = FrameTarget();
            var row = (FrameworkElement)View()!.ContainerFromIndex(75);
            var payload = ((IndexedItem<WidgetIndexedRow>)View()!.Items[75]).Value!;
            Check(target.Index == 75 && target.ItemKey == payload.Item.Key && payload.Lease.IsCurrent,
                "Anchor uses worker-issued exact target and current resident lease");
            Check(ReferenceEquals(collection.ResolveAnchor(target), row), "Exact indexed anchor resolves its native occurrence");
            Check(PreviewText() == "Summary 150:75", "Passive preview renders the current indexed occurrence fragment");
            Check(FocusId() == "Widget.items.Item.75" && View()!.SelectedIndex == -1,
                "Passive preview preserves focus without selecting or activating its row");

            var offset = Scroll().VerticalOffset;
            var metrics = (source.IndexReads, source.ResidentSlots, source.CompletedLoads, source.FailedLoads);
            var focus = FocusManager.GetFocusedElement(XamlRoot);
            for (var index = 0; index < 64; ++index)
            {
                if (!ReferenceEquals(collection.ResolveAnchor(target), row) ||
                    collection.ResolveAnchor(target with { ItemKey = "wrong-key" }) is not null ||
                    collection.ResolveAnchor(target with { QueryGeneration = target.QueryGeneration + 1 }) is not null ||
                    collection.ResolveAnchor(target with { SourceId = "other-source" }) is not null ||
                    collection.ResolveAnchor(target with { Index = 100 }) is not null)
                    throw new InvalidOperationException("Indexed anchor accepted stale or mismatched occurrence authority.");
            }
            Check(true, "Repeated anchor resolution rejects wrong key, generation, source and out-of-range index");
            Check(metrics == (source.IndexReads, source.ResidentSlots, source.CompletedLoads, source.FailedLoads),
                "Resolving resident and rejected anchors does not read source indices, materialize slots or fetch");
            Check(Math.Abs(Scroll().VerticalOffset - offset) < 0.1 && ReferenceEquals(focus, FocusManager.GetFocusedElement(XamlRoot)),
                "Anchor resolution does not scroll or move focus");

            // Hover a resident occurrence while focus stays elsewhere, then recycle it.
            // This exercises stale pointer/native-container authority without controller focus auto-reveal.
            Parent().Focus(FocusState.Keyboard);
            await Wait(() => !presenter.IsFocusPreviewOpen);
            presenter.SetPreviewPointerTarget(row);
            await Wait(() => presenter.IsFocusPreviewOpen);
            Check(FocusId() == "Widget.parent", "Indexed hover preview does not steal toolbar focus");
            Scroll().ChangeView(null, 0, null, true);
            await Wait(() => Scroll().VerticalOffset < 1 && !presenter.IsFocusPreviewOpen);
            Check(collection.ResolveAnchor(target) is not { } oldAnchor || WidgetViewPresenter.VisibleAnchorBounds(oldAnchor, presenter) is null,
                "Recycled or offscreen occurrence loses visible anchor authority");
            Check(!presenter.IsFocusPreviewOpen, "Scrolling the hovered occurrence away removes its passive preview");

            await Send("focus-exact");
            await Wait(() => FocusId() == "Widget.items.Item.75");
            row = (FrameworkElement)View()!.ContainerFromIndex(75);
            payload = ((IndexedItem<WidgetIndexedRow>)View()!.Items[75]).Value!;
            Parent().Focus(FocusState.Keyboard);
            presenter.SetPreviewPointerTarget(row);
            await Wait(() => presenter.IsFocusPreviewOpen);
            await Send("preview-replace");
            await Wait(() => !ReferenceEquals(source, Source()) && !presenter.IsFocusPreviewOpen);
            Check(!payload.Lease.IsCurrent && Collection().ResolveAnchor(target) is null,
                "Query replacement revokes the old range lease and exact indexed anchor");
            Check(!presenter.IsFocusPreviewOpen && FocusId() == "Widget.parent",
                "Query replacement removes stale hovered preview without taking focus");
            await Send("focus-exact");
            await Wait(() => FocusId() == "Widget.items.Item.75" && presenter.IsFocusPreviewOpen && PreviewText() == "Summary 151:75");
            Check(PreviewText() == "Summary 151:75", "Fresh query can show its own preview after old occurrence retirement");
            Check(failure is null, "Native presenter reports no errors");
            Write(true, null);
        }
        catch (Exception error) { Write(false, error.ToString()); }

        WidgetIndexedCollectionView Collection() => Descendants(presenter).OfType<WidgetIndexedCollectionView>().Single();
        IndexedItemsSource<WidgetIndexedRow> Source() => (IndexedItemsSource<WidgetIndexedRow>)View()!.ItemsSource;
        ScrollViewer Scroll() => Descendants(View()!).OfType<ScrollViewer>().First();
        Button Parent() => Descendants(presenter).OfType<Button>().Single(b => AutomationProperties.GetAutomationId(b) == "Widget.parent");
        IndexedCollectionFocusTarget FrameTarget() => session!.GetState("indexed-owned")!.LastGood!.Snapshot.FocusGroupEntryRequest!.IndexedItem!;
        string? FocusId() => FocusManager.GetFocusedElement(XamlRoot) is DependencyObject focused ? AutomationProperties.GetAutomationId(focused) : null;
        string? PreviewText() => presenter.FocusPreviewChrome is { } chrome
            ? Descendants(chrome).OfType<TextBlock>().Select(t => t.Text).FirstOrDefault(t => t.StartsWith("Summary ", StringComparison.Ordinal)) : null;
        async Task Send(string action)
        {
            var frame = session!.GetState("indexed-owned")!.LastGood!;
            var previous = publication;
            await session.SendActionAsync(frame.Authority, new(action, "root", InputScopeId: "root"), lifetime.Token);
            await Wait(() => publication > previous && FindNode(session.GetState("indexed-owned")!.LastGood!.Snapshot.Root, "status")?.Text == action);
        }
        async Task Wait(Func<bool> condition)
        {
            var deadline = Environment.TickCount64 + 7000;
            while (!condition())
            {
                if (Environment.TickCount64 > deadline) throw new TimeoutException("Indexed preview did not settle: " + JsonSerializer.Serialize(presenter.FocusPreviewDiagnostics()));
                await Task.Delay(20, lifetime.Token);
            }
        }
        void Check(bool passed, string name)
        { if (!passed) throw new InvalidOperationException(name); checks.Add(name); }
        void Write(bool passed, string? error)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
            File.WriteAllText(path, JsonSerializer.Serialize(new { passed, checks, error, text = PreviewText(), preview = presenter.FocusPreviewDiagnostics() },
                new JsonSerializerOptions { WriteIndented = true }));
        }
    }
}
