using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using WidgetRail.OverlayFrontend.WinUI.Collections;
using WidgetRail.WidgetProtocol;

namespace WidgetRail.OverlayFrontend.WinUI.Validation;

internal sealed partial class IndexedWidgetValidationPage
{
    private async Task ValidateEntryReplacementAsync(string path)
    {
        var checks = new List<string>();
        var observations = new List<object>();
        var trace = new List<string>();
        presenter.FocusTrace = message => { if (trace.Count < 500) trace.Add(message); };
        try
        {
            await Wait(() => (App.Window is MainWindow { ValidationOwnsForeground: true }));
            for (var round = 0; round < 6; ++round)
            {
                var frame = session!.GetState("indexed-owned")!.LastGood!;
                var previousGeneration = FindNode(frame.Snapshot.Root, "items")?.IndexedCollection?.QueryGeneration;
                await session.SendActionAsync(frame.Authority, new("activation-mode", "root", InputScopeId: "root"), lifetime.Token);
                await Wait(() => View()?.Items.Count > 75 &&
                    View()!.Items[75] is IndexedItem<WidgetIndexedRow> { Value: { Lease.IsCurrent: true } row } &&
                    row.Lease.Range.Source.QueryGeneration != previousGeneration && FocusId() == "Widget.items.Item.75");
                var expected = FocusManager.GetFocusedElement(XamlRoot);
                var wrong = new List<string>();
                void ObserveFocus(object? sender, object args)
                {
                    if (!ReferenceEquals(FocusManager.GetFocusedElement(XamlRoot), expected) && wrong.Count < 8)
                        wrong.Add(FocusId());
                }
                CompositionTarget.Rendering += ObserveFocus;
                try { await Task.Delay(300, lifetime.Token); }
                finally { CompositionTarget.Rendering -= ObserveFocus; }
                observations.Add(new { round, focus = FocusId(), foreground = (App.Window is MainWindow { ValidationOwnsForeground: true }), unexpected = wrong.ToArray() });
                Check((App.Window is MainWindow { ValidationOwnsForeground: true }), "query replacement " + round + " observed in the foreground fixture");
                Check(wrong.Count == 0 && FocusId() == "Widget.items.Item.75",
                    "query replacement " + round + " retains the authored row after native layout settles");
                // The broader activation incident followed a content refresh.
                // Exercise that publication boundary without any item action.
                var refreshed = session!.GetState("indexed-owned")!.LastGood!;
                var previousLease = ((IndexedItem<WidgetIndexedRow>)View()!.Items[75]).Value!.Lease;
                await session.SendActionAsync(refreshed.Authority, new("content", "root", InputScopeId: "root"), lifetime.Token);
                await Wait(() => View()!.Items[75] is IndexedItem<WidgetIndexedRow> { Value: { Lease.IsCurrent: true } value } &&
                    !ReferenceEquals(previousLease, value.Lease));
                Check(FocusId() == "Widget.items.Item.75", "content refresh retains the focused row before replacement " + round);
                await Send("activation-refresh");
                await Wait(() => View()!.Items[75] is IndexedItem<WidgetIndexedRow> { Value.Lease.IsCurrent: false });
                await presenter.HandleControllerButtonAsync(ControllerButton.A);
                await presenter.HandleControllerButtonAsync(ControllerButton.A, ControllerEventPhase.Repeated);
                await presenter.HandleControllerButtonAsync(ControllerButton.A, ControllerEventPhase.Released);
                await presenter.HandleControllerButtonAsync(ControllerButton.A);
                await Send("activation-release");
                await Wait(() => FindNode(session!.GetState("indexed-owned")!.LastGood!.Snapshot.Root, "status")?.Text == "row:301:75:open");
                await Task.Delay(120, lifetime.Token);
                Check(FocusId() == "Widget.items.Item.75", "deferred activation retains row before replacement " + round);
            }

            // A superseding ordinary focus request must still win. No reset
            // completion is allowed to pull focus back into the collection.
            var parent = Descendants(presenter).OfType<Button>().Single(button => AutomationProperties.GetAutomationId(button) == "Widget.parent");
            parent.Focus(FocusState.Keyboard);
            await Task.Delay(200, lifetime.Token);
            Check(ReferenceEquals(FocusManager.GetFocusedElement(XamlRoot), parent), "explicit focus outside the collection is not reclaimed");
            Write(true, null);
        }
        catch (Exception error) { Write(false, error.ToString()); }
        finally { presenter.FocusTrace = null; }

        string FocusId() => FocusManager.GetFocusedElement(XamlRoot) is DependencyObject focused ? AutomationProperties.GetAutomationId(focused) : "none";
        async Task Send(string action)
        {
            var frame = session!.GetState("indexed-owned")!.LastGood!;
            await session.SendActionAsync(frame.Authority, new(action, "root", InputScopeId: "root"), lifetime.Token);
            await Wait(() => session.GetState("indexed-owned")!.LastGood!.Authority.SnapshotSequence > frame.Authority.SnapshotSequence);
        }
        async Task Wait(Func<bool> condition)
        {
            var deadline = Environment.TickCount64 + 7000;
            while (!condition())
            {
                if (Environment.TickCount64 > deadline) throw new TimeoutException("Query entry did not settle; focus=" + FocusId() + "; " + presenter.FocusDiagnostics());
                await Task.Delay(20, lifetime.Token);
            }
        }
        void Check(bool passed, string name)
        { if (!passed) throw new InvalidOperationException(name); checks.Add(name); }
        void Write(bool passed, string? error)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
            File.WriteAllText(path, JsonSerializer.Serialize(new { passed, checks, observations, trace, error }, new JsonSerializerOptions { WriteIndented = true }));
        }
    }
}
