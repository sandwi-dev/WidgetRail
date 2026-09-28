using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using WidgetRail.OverlayFrontend.WinUI.Collections;
using WidgetRail.OverlayFrontend.WinUI.Presentation;
using WidgetRail.PlatformSettings;
using WidgetRail.WidgetBridge;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetStyling;

namespace WidgetRail.OverlayFrontend.WinUI.Validation;

internal sealed partial class IndexedWidgetValidationPage
{
    private async Task ProbeStylesAsync(string settingsRoot)
    {
        var checks = new List<string>();
        var stage = "initial presentation";
        Func<object>? details = null;
        try
        {
            await Until(() => presenter.CurrentBinding is not null && View() is not null);
            var initial = session!.GetState("indexed-owned")!.LastGood!;
            stage = "surface publication";
            await session.SendActionAsync(initial.Authority, new("surfaces", "surfaces", InputScopeId: "root"), lifetime.Token);
            await Until(() => FindNode(presenter.CurrentBinding!.Frame.Snapshot.Root, "summary-surface") is not null &&
                View() is { IsLoaded: true, Items.Count: > 75 });
            var view = View()!;
            stage = "deep row realization";
            await session.SendActionAsync(session.GetState("indexed-owned")!.LastGood!.Authority,
                new("focus-exact", "root", InputScopeId: "root"), lifetime.Token);
            await Until(() => view.ContainerFromIndex(75) is Control ready &&
                ReferenceEquals(Microsoft.UI.Xaml.Input.FocusManager.GetFocusedElement(XamlRoot), ready) &&
                view.Items.Count > 75 && view.Items[75] is IndexedItem<WidgetIndexedRow> { Value: not null } &&
                Descendants(ready).OfType<TextBlock>().Any(text => text.Text == "Item 75"));
            var container = (Control)view.ContainerFromIndex(75);
            var item = ((IndexedItem<WidgetIndexedRow>)view.Items[75]).Value!;
            var title = Descendants(container).OfType<TextBlock>().Single(text => text.Text == "Item 75");
            stage = "focus fragment";
            await Until(() => title.Foreground is SolidColorBrush && Descendants(presenter).OfType<TextBlock>().Any(text => text.Text == "Summary 100:75"));
            var summary = Descendants(presenter).OfType<TextBlock>().Single(text => text.Text == "Summary 100:75");
            var oldColor = ((SolidColorBrush)title.Foreground).Color;
            var oldSummaryColor = ((SolidColorBrush)summary.Foreground).Color;
            var oldFrame = presenter.CurrentBinding!.Frame;
            var slot = view.Items[75];
            var source = (IndexedItemsSource<WidgetIndexedRow>)view.ItemsSource;
            // Allow native viewport/prefetch admission to settle before the theme operation.
            await Task.Delay(400, lifetime.Token);
            var loads = source.CompletedLoads;
            var scroll = Descendants(view).OfType<ScrollViewer>().First();
            var offset = scroll.VerticalOffset;
            var settings = new PlatformSettingsStore(new(Path.GetFullPath(settingsRoot)));
            stage = "theme publication and native colors";
            details = () => new { oldRevision = oldFrame.AppearanceRevision, frameRevision = presenter.CurrentBinding?.Frame.AppearanceRevision,
                leaseRevision = item.Lease.AppearanceRevision, notification = session.LatestAppearanceNotificationRevision,
                oldColor = oldColor.ToString(), color = (title.Foreground as SolidColorBrush)?.Color.ToString(),
                oldSummaryColor = oldSummaryColor.ToString(), summaryColor = (summary.Foreground as SolidColorBrush)?.Color.ToString(),
                diagnostics = session.Diagnostics.Select(value => new { value.Code, value.Message }).ToArray() };
            await settings.UpdateAsync(value => value with { Appearance = value.Appearance with
                { ThemeId = ThemeIdentity.BuiltInCoolSlate, ThemeVersion = ThemeIdentity.BuiltInCoolSlateVersion } }, lifetime.Token);
            await Until(() => presenter.CurrentBinding!.Frame.AppearanceRevision > oldFrame.AppearanceRevision &&
                item.Lease.AppearanceRevision == presenter.CurrentBinding.Frame.AppearanceRevision &&
                title.Foreground is SolidColorBrush color && color.Color != oldColor &&
                summary.Foreground is SolidColorBrush summaryColor && summaryColor.Color != oldSummaryColor);
            Check(ReferenceEquals(presenter.CurrentBinding!.Frame.Snapshot, oldFrame.Snapshot), "ordinary styles preserve the exact widget snapshot");
            Check(ReferenceEquals(presenter.CurrentBinding.Frame.Authority, oldFrame.Authority), "ordinary styles preserve displayed action authority");
            Check(ReferenceEquals(view.Items[75], slot), "theme change preserves logical native slot identity");
            Check(ReferenceEquals(((IndexedItem<WidgetIndexedRow>)view.Items[75]).Value, item) && item.Lease.IsCurrent, "theme change retains the existing item and action lease");
            Check(ReferenceEquals(view.ContainerFromIndex(75), container), "theme change retains the realized container");
            Check(ReferenceEquals(Microsoft.UI.Xaml.Input.FocusManager.GetFocusedElement(XamlRoot), container), "deep focused item remains focused");
            Check(Math.Abs(scroll.VerticalOffset - offset) <= 1, "deep viewport anchor does not move on a palette change");
            Check(source.CompletedLoads == loads, "palette update does not reacquire provider pages");
            Check(((SolidColorBrush)title.Foreground).Color != oldColor, "retained row text receives the new compiled theme");
            Check(((SolidColorBrush)summary.Foreground).Color != oldSummaryColor, "retained focus fragment receives the new compiled theme");
            var revisedColor = ((SolidColorBrush)title.Foreground).Color;
            stage = "newly realized colors";
            view.ScrollIntoView(view.Items[0], ScrollIntoViewAlignment.Leading);
            await Until(() => view.ContainerFromIndex(0) is Control && ((IndexedItem<WidgetIndexedRow>)view.Items[0]).Value is not null);
            ((Control)view.ContainerFromIndex(0)).Focus(FocusState.Keyboard);
            await Until(() => Descendants((Control)view.ContainerFromIndex(0)).OfType<TextBlock>()
                .Any(text => text.Text == "Item 0" && text.Foreground is SolidColorBrush color && color.Color == revisedColor));
            Check(true, "newly realized rows use the same latest theme");

            stage = "appearance-only grid padding";
            await session.SendActionAsync(presenter.CurrentBinding!.Frame.Authority,
                new("grid", "grid", InputScopeId: "root"), lifetime.Token);
            await Until(() => View() is GridView { ItemsPanelRoot: ItemsWrapGrid { ItemWidth: > 0 } });
            var nativeGrid = View()!;
            var nativePanel = (ItemsWrapGrid)nativeGrid.ItemsPanelRoot;
            var gridScroller = Descendants(nativeGrid).OfType<ScrollViewer>().First();
            await Until(() => nativeGrid.ContainerFromIndex(0) is Control &&
                nativeGrid.Items.Count > 0 && nativeGrid.Items[0] is IndexedItem<WidgetIndexedRow> { Value: not null });
            var firstGridItem = (Control)nativeGrid.ContainerFromIndex(0);
            firstGridItem.Focus(FocusState.Keyboard);
            await Task.Delay(150, lifetime.Token);
            var gridSource = (IndexedItemsSource<WidgetIndexedRow>)nativeGrid.ItemsSource;
            var gridLoads = gridSource.CompletedLoads;
            var gridOffset = gridScroller.VerticalOffset;
            var beforePadding = (nativeGrid.ActualWidth, nativeGrid.ActualHeight, nativePanel.MaximumRowsOrColumns, nativePanel.ItemWidth);
            var gridFrame = presenter.CurrentBinding!.Frame;
            // The following segment isolates native geometry with a host-created
            // style map. Live worker publications must not replace that map while
            // it is being measured; provider requests and leases remain active.
            session.PresentationChanged -= Changed;
            try
            {
                var styles = new Dictionary<string, BridgeNodeRenderStyles>(gridFrame.RenderStyles);
                var oldGridStyle = styles["items"];
                IReadOnlyDictionary<string, BridgeComputedStyleValue> Padded(IReadOnlyDictionary<string, BridgeComputedStyleValue> state) =>
                    new Dictionary<string, BridgeComputedStyleValue>(state)
                    {
                        ["padding"] = new() { Kind = WrssValueKind.LengthList, Text = "0px 128px" },
                    };
                styles["items"] = new() { Base = Padded(oldGridStyle.Base), Focused = Padded(oldGridStyle.Focused), Pressed = Padded(oldGridStyle.Pressed) };
                // Host-controlled rendering probe: same declaration/authority, only
                // style geometry changes. Do not dispatch widget actions through this copy.
                presenter.Apply(gridFrame with { AppearanceRevision = gridFrame.AppearanceRevision + 1, RenderStyles = styles });
                nativeGrid.UpdateLayout();
                var layout = FindNode(gridFrame.Snapshot.Root, "items")!.CollectionLayout!;
                int ExpectedColumns() => Math.Clamp((int)((gridScroller.ViewportWidth - nativeGrid.Padding.Left - nativeGrid.Padding.Right) /
                    (layout.MinimumColumnWidth ?? 160)), 1, layout.MaximumColumns ?? int.MaxValue);
                details = () => new { before = new { beforePadding.ActualWidth, beforePadding.ActualHeight,
                        Columns = beforePadding.Item3, ItemWidth = beforePadding.ItemWidth }, nativeGrid.ActualWidth, nativeGrid.ActualHeight,
                    nativeGrid.Padding, nativePanel.ItemWidth, nativePanel.MaximumRowsOrColumns, expectedColumns = ExpectedColumns() };
                await Until(() => nativePanel.MaximumRowsOrColumns == ExpectedColumns());
                var expectedWidth = Math.Floor((gridScroller.ViewportWidth - nativeGrid.Padding.Left - nativeGrid.Padding.Right) *
                    nativeGrid.XamlRoot.RasterizationScale / ExpectedColumns()) / nativeGrid.XamlRoot.RasterizationScale;
                Check(beforePadding.Item3 != nativePanel.MaximumRowsOrColumns, "appearance-only padding recomputes native grid column count");
                Check(Math.Abs(nativePanel.ItemWidth - expectedWidth) < .01, "appearance-only padding recomputes physical-pixel-aligned cell width");
                Check(Math.Abs(nativeGrid.ActualWidth - beforePadding.ActualWidth) < .01 &&
                    Math.Abs(nativeGrid.ActualHeight - beforePadding.ActualHeight) < .01, "padding correction does not depend on outer collection SizeChanged");
                Check(ReferenceEquals(nativeGrid.ItemsSource, gridSource) && gridSource.CompletedLoads == gridLoads,
                    "padding-only grid reflow retains its source without reloading provider pages");
                Check(ReferenceEquals(nativeGrid.ContainerFromIndex(0), firstGridItem) && firstGridItem.FocusState != FocusState.Unfocused &&
                    Math.Abs(gridScroller.VerticalOffset - gridOffset) < 1, "padding-only grid reflow retains native focus and the top viewport anchor");
                var downIndex = ExpectedColumns();
                presenter.MoveFocus(Microsoft.UI.Xaml.Input.FocusNavigationDirection.Down);
                await Until(() => nativeGrid.ContainerFromIndex(downIndex) is Control { FocusState: not FocusState.Unfocused });
                var nextRow = (Control)nativeGrid.ContainerFromIndex(downIndex);
                Check(nextRow.TransformToVisual(nativeGrid).TransformPoint(default).Y >
                    firstGridItem.TransformToVisual(nativeGrid).TransformPoint(default).Y,
                    "controller Down uses the same recalculated columns as native row layout");
            }
            finally { session.PresentationChanged += Changed; }

            stage = "failed provider page";
            await session.SendActionAsync(session.GetState("indexed-owned")!.LastGood!.Authority,
                new("failure-mode", "root", InputScopeId: "root"), lifetime.Token);
            await Until(() => View()?.ItemsSource is IndexedItemsSource<WidgetIndexedRow> failed &&
                !ReferenceEquals(failed, source) && failed.FailedLoads > 0);
            var failedSource = (IndexedItemsSource<WidgetIndexedRow>)View()!.ItemsSource;
            await Task.Delay(300, lifetime.Token);
            var failures = failedSource.FailedLoads;
            var failedFrame = presenter.CurrentBinding!.Frame;
            stage = "appearance must not retry failed pages";
            await settings.UpdateAsync(value => value with { Appearance = value.Appearance with
                { ThemeId = ThemeIdentity.BuiltInNeonCircuit, ThemeVersion = ThemeIdentity.BuiltInNeonCircuitVersion } }, lifetime.Token);
            await Until(() => presenter.CurrentBinding!.Frame.AppearanceRevision > failedFrame.AppearanceRevision);
            await Task.Delay(500, lifetime.Token);
            Check(ReferenceEquals(View()!.ItemsSource, failedSource), "appearance refresh retains a failed-page query source");
            Check(failedSource.FailedLoads == failures, "appearance refresh does not retry failed semantic provider pages");
            Check(ReferenceEquals(presenter.CurrentBinding!.Frame.Snapshot, failedFrame.Snapshot), "failed-page appearance update keeps its exact declaration snapshot");
            stage = "explicit semantic update can retry";
            await session.SendActionAsync(presenter.CurrentBinding.Frame.Authority,
                new("parent", "parent", InputScopeId: "root"), lifetime.Token);
            await Until(() => failedSource.FailedLoads > failures);
            Check(true, "ordinary semantic publication retains the explicit failed-page retry behavior");
            status.Text = "Passed " + checks.Count + " native theme-refresh checks";
            await Save(null);
        }
        catch (Exception error)
        {
            status.Text = "Theme refresh failed: " + error.Message;
            await Save(error.ToString());
        }
        void Check(bool passed, string message) { if (!passed) throw new InvalidOperationException(message); checks.Add(message); }
        async Task Until(Func<bool> condition)
        {
            var deadline = Environment.TickCount64 + 15000;
            while (!condition()) { if (Environment.TickCount64 > deadline) throw new TimeoutException("Native theme refresh did not settle."); await Task.Delay(30, lifetime.Token); }
        }
        Task Save(string? error)
        {
            var directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WidgetRail", "WinUI", "diagnostics");
            Directory.CreateDirectory(directory);
            return File.WriteAllTextAsync(Path.Combine(directory, "indexed-theme-result.json"), JsonSerializer.Serialize(new
                { pid = Environment.ProcessId, passed = error is null, checks, error, stage, details = details?.Invoke() }));
        }
    }
}
