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
    private async Task ValidateContinuityAsync(string path)
    {
        var checks = new List<string>();
        var trace = new List<string>();
        void Trace(string value)
        {
            if (trace.Count < 240 && (trace.Count == 0 || trace[^1] != value)) trace.Add(value);
        }
        try
        {
            await Wait(() => View() is { IsLoaded: true } && Parent() is { IsLoaded: true });
            Collection().NavigationTrace = Trace;
            Parent().Focus(FocusState.Keyboard);
            // Repeat bounded local-only transitions after native geometry settles.
            // Capturing immediately after Focus() races its BringIntoView/layout.
            for (var pass = 0; pass < 3; ++pass)
            {
                await Send("continuity-start");
                await FocusItem(75);
                var top = await SettledPosition(75);
                Trace($"pass={pass} before insertion expected={top} scroll={Scroll().VerticalOffset}");
                await Send("continuity-insert");
                await Wait(() => FocusedKey() == "item.75" && Current(76));
                await Wait(() => Math.Abs(Position(76) - top) < 2);
                var afterInsert = await SettledPosition(76);
                Check(Math.Abs(afterInsert - top) < 2, $"Pass {pass + 1}: insertion preserves focused occurrence and settled pixel offset");
                await Send("continuity-remove-before");
                await Wait(() => FocusedKey() == "item.75" && Current(74));
                await Wait(() => Math.Abs(Position(74) - top) < 2);
                var afterRemove = await SettledPosition(74);
                Check(Math.Abs(afterRemove - top) < 2, $"Pass {pass + 1}: removal before focus preserves settled offset");
                await Send("continuity-shuffle");
                await Wait(() => FocusedKey() == "item.75" && Current(24));
                await Wait(() => Math.Abs(Position(24) - top) < 2);
                var afterShuffle = await SettledPosition(24);
                Check(Math.Abs(afterShuffle - top) < 2, $"Pass {pass + 1}: shuffle preserves focused occurrence and settled offset");
            }
            await Send("continuity-remove-focused");
            await Wait(() => FocusedKey() == "item.74");
            Check(true, "Focused removal follows next surviving occurrence in old order");

            Parent().Focus(FocusState.Keyboard);
            var collection = Collection();
            var anchor = collection.CaptureViewport(new("root", "items", ViewNodeKind.IndexedCollection, ""))!;
            await Send("continuity-start");
            await Wait(() => Current(anchor.Index + 1) || Current(anchor.Index));
            await Task.Delay(180, lifetime.Token);
            Check(FocusedParent() && Scroll().VerticalOffset > 100, "Unfocused update preserves viewport without moving toolbar focus");

            await FocusItem(75);
            await Send("continuity-delayed");
            await Task.Delay(120, lifetime.Token);
            Check(View()!.Items[75] is IndexedItem<WidgetIndexedRow> { Key: "item.75", Value: not null },
                "Delayed replacement retains resident content");
            var retainedTop = await SettledPosition(75);
            var retainedOffset = Scroll().VerticalOffset;
            // Emulate native realization/BringIntoView changing scroll geometry
            // while the current-query lease is still pending. This is deliberately
            // not a user scroll: user navigation must cancel restoration instead.
            Scroll().ChangeView(null, retainedOffset + 16, null, true);
            await Wait(() => Math.Abs(Position(75) - retainedTop) < 2);
            var reconciledTop = await SettledPosition(75);
            Check(Math.Abs(reconciledTop - retainedTop) < 2, "Native offset interruption during delayed loading is reconciled");
            Parent().Focus(FocusState.Keyboard);
            presenter.ScrollBy(0, -80);
            await Send("activation-release");
            await Task.Delay(350, lifetime.Token);
            Check(FocusedParent(), "Late replacement never steals focus after user departure");
            await Send("continuity-empty");
            await Wait(() => View()!.Items.Count == 0);
            Check(FocusedParent(), "Empty replacement preserves external focus");
            Check(failure is null, "Native presenter reported no continuity errors");
            Write(true, null);
        }
        catch (Exception error)
        {
            Trace($"failure positions75={Position(75)} 76={Position(76)} scroll={Scroll().VerticalOffset}");
            Write(false, error.ToString());
        }
        finally { if (View() is not null) Collection().NavigationTrace = null; }

        WidgetIndexedCollectionView Collection() => Descendants(presenter).OfType<WidgetIndexedCollectionView>().Single();
        Button Parent() => Descendants(presenter).OfType<Button>().Single(b => AutomationProperties.GetAutomationId(b) == "Widget.parent");
        bool FocusedParent() => ReferenceEquals(FocusManager.GetFocusedElement(XamlRoot), Parent());
        string? FocusedKey() => Collection().CaptureFocusedItem()?.ItemKey;
        ScrollViewer Scroll() => Descendants(View()!).OfType<ScrollViewer>().First();
        bool Current(int index) => index >= 0 && index < View()!.Items.Count && View()!.Items[index] is IndexedItem<WidgetIndexedRow> { Value.Lease.IsCurrent: true };
        double Position(int index) => (View()!.ContainerFromIndex(index) as FrameworkElement)?.TransformToVisual(Scroll()).TransformPoint(new(0, 0)).Y ?? double.NaN;
        async Task FocusItem(int index)
        {
            var query = FindNode(session!.GetState("indexed-owned")!.LastGood!.Snapshot.Root, "items")!.IndexedCollection!;
            Collection().Enter(new("items", query.SourceId, query.QueryGeneration, "item." + index, index));
            await Wait(() => FocusedKey() == "item." + index && Current(index));
        }
        async Task<double> SettledPosition(int index)
        {
            var deadline = Environment.TickCount64 + 5000;
            var previous = double.NaN;
            var previousOffset = double.NaN;
            var stable = 0;
            while (stable < 5)
            {
                if (Environment.TickCount64 > deadline) throw new TimeoutException("Native geometry never settled.");
                await Task.Delay(40, lifetime.Token);
                var position = Position(index);
                var offset = Scroll().VerticalOffset;
                stable = double.IsFinite(position) && Math.Abs(position - previous) < 0.1 && Math.Abs(offset - previousOffset) < 0.1 ? stable + 1 : 0;
                previous = position; previousOffset = offset;
            }
            return previous;
        }
        async Task Send(string action)
        {
            var frame = session!.GetState("indexed-owned")!.LastGood!;
            await session.SendActionAsync(frame.Authority, new(action, "root", InputScopeId: "root"), lifetime.Token);
            await Wait(() => Descendants(presenter).OfType<TextBlock>().Any(t => AutomationProperties.GetAutomationId(t) == "Widget.status" && t.Text == action));
        }
        async Task Wait(Func<bool> condition)
        {
            var deadline = Environment.TickCount64 + 8000;
            while (!condition())
            {
                if (Environment.TickCount64 > deadline) throw new TimeoutException("Indexed continuity did not settle: " + presenter.FocusDiagnostics());
                await Task.Delay(20, lifetime.Token);
            }
        }
        void Check(bool passed, string name)
        { if (!passed) throw new InvalidOperationException(name); checks.Add(name); }
        void Write(bool passed, string? error)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
            File.WriteAllText(path, JsonSerializer.Serialize(new { passed, checks, trace, error }, new JsonSerializerOptions { WriteIndented = true }));
        }
    }
}
