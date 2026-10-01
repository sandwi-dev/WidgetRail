using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using WidgetRail.OverlayFrontend.WinUI.Collections;
using WidgetRail.WidgetProtocol;

namespace WidgetRail.OverlayFrontend.WinUI.Shell;

internal sealed partial class OverlayShellPage
{
    // Only the two credential-free reference packages. Navigation and refresh
    // exercise genuine worker input; no provider/system command is permitted.
    internal void EnableReferenceWorkflowValidation(string path)
    {
        Loaded += async (_, _) =>
        {
            const string clock = "widgetrail.samples.clock";
            const string library = "widgetrail.samples.full-application";
            const string listId = "Widget.full-app.document-list.Items";
            var checks = new List<string>();
            var observations = new List<object>();
            try
            {
                var output = Path.GetDirectoryName(Path.GetFullPath(path))! + Path.DirectorySeparatorChar;
                if (!Path.GetFullPath(options.SettingsRoot).StartsWith(output, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("Reference checks require an isolated settings profile beneath the evidence directory.");
                if (startup is not null) await startup;
                await SelectAsync(clock, true);
                await Until(() => MainFocusEnabled && Find<Control>("Widget.refresh") is { IsEnabled: true } && FocusId() == "Widget.refresh");
                var refresh = Find<Control>("Widget.refresh");
                var sequence = surface!.CurrentBinding!.Frame.Authority.SnapshotSequence;
                Check(Find<TextBlock>("Widget.clock-time")?.Text.Length > 0 && Find<TextBlock>("Widget.clock-date")?.Text.Length > 0,
                    "real Clock worker publishes native time/date and initial Refresh focus");
                await Button(ControllerButton.X);
                await Until(() => surface!.CurrentBinding!.Frame.Authority.SnapshotSequence > sequence);
                Check(ReferenceEquals(refresh, Find<Control>("Widget.refresh")) && FocusId() == "Widget.refresh",
                    "Clock X shortcut refreshes through the broker while retaining the native control and focus");

                await SelectAsync(library, true);
                await Until(() => MainFocusEnabled && Find<ListViewBase>(listId)?.Items.Count == 10000);
                var firstList = Find<ListViewBase>(listId)!;
                Check(firstList is ListView && Walk(firstList).OfType<SelectorItem>().Count() < 128,
                    "real reference worker keeps ten thousand records behind a bounded native virtualized list");
                foreach (var index in new[] { 0, 75, 9999 })
                {
                    var list = Find<ListViewBase>(listId)!;
                    // Native ScrollIntoView establishes a deep viewport without
                    // manufacturing worker rows, keys or indexed action receipts.
                    list.ScrollIntoView(list.Items[index], ScrollIntoViewAlignment.Leading);
                    await Until(() => CurrentRow(list, index) && list.ContainerFromIndex(index) is Control { IsLoaded: true, IsEnabled: true });
                    ((Control)list.ContainerFromIndex(index)).Focus(FocusState.Keyboard);
                    var focused = $"Widget.full-app.document-list.Item.{index}";
                    await Until(() => FocusId() == focused);
                    await Button(ControllerButton.A);
                    await Until(() => Find<TextBlock>("Widget.full-app.details.title")?.Text == $"Document {index:D5}" &&
                        FocusId() == "Widget.full-app.details.back");
                    Check(true, "leased native row " + index + " opens the matching document and focuses Back");
                    await Button(ControllerButton.B);
                    await Until(() => Find<ListViewBase>(listId) is { } returned && CurrentRow(returned, index) && FocusId() == focused);
                    list = Find<ListViewBase>(listId)!;
                    var item = (Control)list.ContainerFromIndex(index);
                    var bounds = item.TransformToVisual(list).TransformBounds(new(0, 0, item.ActualWidth, item.ActualHeight));
                    Check(bounds.Bottom > 0 && bounds.Top < list.ActualHeight,
                        "reference Back restores visible keyed row " + index + " through the actual worker navigation contract");
                    observations.Add(new { index, realized = Walk(list).OfType<SelectorItem>().Count(), viewport = list.ActualHeight });
                }

                var beforeRefresh = Find<ListViewBase>(listId)!;
                var lease = ((IndexedItem<WidgetIndexedRow>)beforeRefresh.Items[9999]).Value!.Lease;
                Find<Control>("Widget.full-app.refresh")!.Focus(FocusState.Keyboard);
                await Button(ControllerButton.A);
                await Until(() => CurrentRow(beforeRefresh, 9999) &&
                    !ReferenceEquals(((IndexedItem<WidgetIndexedRow>)beforeRefresh.Items[9999]).Value!.Lease, lease));
                Check(ReferenceEquals(beforeRefresh, Find<ListViewBase>(listId)) && beforeRefresh.Items.Count == 10000,
                    "reference Refresh replaces row leases without replacing the native collection or membership");
                ((Control)beforeRefresh.ContainerFromIndex(9999)).Focus(FocusState.Keyboard);
                await Until(() => FocusId() == "Widget.full-app.document-list.Item.9999");
                foreach (var peer in new[] { clock, "settings", "widgetrail.samples.sdk-gallery" })
                    await SelectAsync(peer, true);
                Check(!retainedSurfaces.ContainsKey(library), "three peer widgets evict the reference native presenter");
                await SelectAsync(library, true);
                await Until(() => MainFocusEnabled && FocusId() == "Widget.full-app.document-list.Item.9999");
                Check(!ReferenceEquals(beforeRefresh, Find<ListViewBase>(listId)),
                    "reference reentry recreates an evicted native list and restores the deep logical focus");
                SetVisible(false); await Task.Delay(100, lifetime.Token); SetVisible(true);
                await Until(() => MainFocusEnabled && FocusId() == "Widget.full-app.document-list.Item.9999");
                Check(true, "reference hide/reopen preserves focus through deactivation content refresh");
                Write(true, null);
            }
            catch (Exception error) { Write(false, error.ToString()); }

            async Task Button(ControllerButton button)
            {
                await RouteButtonAsync(button, ControllerEventPhase.Pressed);
                await RouteButtonAsync(button, ControllerEventPhase.Released);
            }
            static bool CurrentRow(ListViewBase list, int index) => list.Items[index] is
                IndexedItem<WidgetIndexedRow> { Value.Lease.IsCurrent: true };
            string? FocusId() => FocusManager.GetFocusedElement(XamlRoot) is DependencyObject focused ? AutomationProperties.GetAutomationId(focused) : null;
            T? Find<T>(string id) where T : FrameworkElement => surface is null ? null : Walk(surface).OfType<T>()
                .FirstOrDefault(element => AutomationProperties.GetAutomationId(element) == id);
            async Task Until(Func<bool> condition)
            {
                var deadline = Environment.TickCount64 + 15000;
                while (!condition())
                {
                    if (RecoveryVisible || validationFailure is not null) throw new InvalidOperationException("Reference workflow entered recovery.", validationFailure);
                    if (Environment.TickCount64 > deadline) throw new TimeoutException("Reference workflow did not settle; " +
                        JsonSerializer.Serialize(new { focused = FocusId(), visible, foreground, interactive, switching,
                            collections = surface is null ? [] : Walk(surface).OfType<Presentation.WidgetIndexedCollectionView>()
                                .Select(collection => collection.NavigationDiagnosticState()).ToArray() }));
                    await Task.Delay(20, lifetime.Token);
                }
            }
            void Check(bool value, string name) { if (!value) throw new InvalidOperationException(name); checks.Add(name); }
            void Write(bool passed, string? error)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
                File.WriteAllText(path, JsonSerializer.Serialize(new { passed, checks, observations, error }));
            }
        };
        static IEnumerable<DependencyObject> Walk(DependencyObject root)
        {
            yield return root;
            for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); ++index)
                foreach (var child in Walk(VisualTreeHelper.GetChild(root, index))) yield return child;
        }
    }
}
