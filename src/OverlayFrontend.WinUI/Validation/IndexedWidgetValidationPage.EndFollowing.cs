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
    private async Task ValidateEndFollowingAsync(string path)
    {
        var checks = new List<string>();
        var trace = new List<string>();
        presenter.FocusTracePhasesOnly = true;
        presenter.FocusTrace = value =>
        {
            if (trace.Count < 256 && value.StartsWith("preview-", StringComparison.Ordinal) && (trace.Count == 0 || trace[^1] != value)) trace.Add(value);
        };
        // This lane now exercises passive/entered previews as well as scrolling.
        // Use the same production-style portal root as the existing preview lane.
        var theme = new NativePopupTheme();
        theme.Attach(presenter);
        var layout = (Grid)Content;
        Content = null;
        var overlayRoot = new Grid(); overlayRoot.Children.Add(layout);
        Content = overlayRoot;
        PresentationLayers.Attach(overlayRoot);

        try
        {
            await Wait(() => View() is { IsLoaded: true });
            Parent().Focus(FocusState.Keyboard);
            presenter.SetPresentationInputEnabled(false);
            await presenter.SetPresentationActiveAsync(false);
            await Send("end-following-unobserved-defer");await Send("end-following-unobserved-middle");
            Trace("unobserved-hidden-before-activation");
            await presenter.SetPresentationActiveAsync(true);
            await Wait(()=>View()?.Items.Count==20 && Current(7) && View()!.ContainerFromIndex(7) is FrameworkElement unobserved &&
                WidgetReadingScrollFocus.IsVisible(unobserved,Scroll()));
            Check(FocusedParent() && !presenter.IsHitTestVisible,"Callback-free hidden first publication reveals its non-tail target while input remains disabled");
            presenter.SetPresentationInputEnabled(true);
            Parent().Focus(FocusState.Keyboard);
            await presenter.SetPresentationActiveAsync(false);
            await Send("end-following-defer");await Send("end-following-defer-middle");
            Trace("hidden-first-before-activation");
            await presenter.SetPresentationActiveAsync(true);
            await Wait(()=>View()?.Items.Count==20 && Current(7) && View()!.ContainerFromIndex(7) is FrameworkElement target &&
                WidgetReadingScrollFocus.IsVisible(target,Scroll()));
            Check(FocusedParent(),"A new intent first published while preparing hidden completes on activation without replaying old consumed requests");
            Parent().Focus(FocusState.Keyboard);
            await Send("end-following-defer");await Wait(()=>View()?.Items.Count==0);
            var deferredId=session!.GetState("indexed-owned")!.LastGood!.Snapshot.ScrollRevealRequest!.RequestId;
            await Send("end-following-defer-complete");
            await Wait(()=>View()?.Items.Count==20 && Current(19) && AtEnd());
            Check(FocusedParent() && session.GetState("indexed-owned")!.LastGood!.Snapshot.ScrollRevealRequest!.RequestId==deferredId,
                "Deferred same-ID reveal completes after legitimate first data without changing focus");
            await Send("end-following-defer");await Wait(()=>View()?.Items.Count==0);
            var rendered=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            void Rendered(object? sender,object args)=>rendered.TrySetResult();
            Microsoft.UI.Xaml.Media.CompositionTarget.Rendering+=Rendered;
            try{await rendered.Task.WaitAsync(TimeSpan.FromSeconds(5),lifetime.Token);}
            finally{Microsoft.UI.Xaml.Media.CompositionTarget.Rendering-=Rendered;}
            var pointerTarget=Parent();
            var pointerBefore=pointerTarget.TransformToVisual(null).TransformPoint(new(0,0));
            var pressed=false;
            PointerEventHandler pointerObserved=(_,_)=>pressed=true;
            pointerTarget.AddHandler(UIElement.PointerPressedEvent,pointerObserved,true);
            try{await NativeContextProbeInput.LeftClickAsync(pointerTarget,new Windows.Foundation.Point(pointerTarget.ActualWidth/2,pointerTarget.ActualHeight/2));}
            finally{pointerTarget.RemoveHandler(UIElement.PointerPressedEvent,pointerObserved);}
            trace.Add($"pointer-input pressed={pressed} sameTarget={ReferenceEquals(pointerTarget,Parent())} loaded={pointerTarget.IsLoaded} enabled={pointerTarget.IsEnabled} hit={pointerTarget.IsHitTestVisible} presenterHit={presenter.IsHitTestVisible} before={pointerBefore} after={pointerTarget.TransformToVisual(null).TransformPoint(new(0,0))}");
            Check(pressed,"Injected pointer choice reaches the actual native parent before late completion");
            await Send("end-following-defer-complete");await Wait(()=>View()?.Items.Count==20 && Current(0));await SettleScroll();
            Check(Scroll().VerticalOffset<=1 && FocusedParent(),"Pointer choice consumes deferred reveal before late history completion");
            await Send("end-following-defer");await Wait(()=>View()?.Items.Count==0);
            var other=Descendants(presenter).OfType<Button>().Single(button=>AutomationProperties.GetAutomationId(button)=="Widget.content");
            other.Focus(FocusState.Programmatic);
            await Send("end-following-defer-complete");await Wait(()=>View()?.Items.Count==20 && Current(0));await SettleScroll();
            Check(Scroll().VerticalOffset<=1 && ReferenceEquals(FocusManager.GetFocusedElement(XamlRoot),other),
                "Explicit programmatic or UIA focus departure consumes the deferred reveal");
            Parent().Focus(FocusState.Keyboard);
            await Send("end-following-defer");await Wait(()=>View()?.Items.Count==0);
            await Send("end-following-defer-replace");await Send("end-following-defer-complete");
            await Wait(()=>View()?.Items.Count==20 && Current(0));await SettleScroll();
            Check(Scroll().VerticalOffset<=1,"A replacement semantic query cannot inherit an old deferred request ID");
            await Send("end-following-defer");await Wait(()=>View()?.Items.Count==0);
            await presenter.SetPresentationActiveAsync(false);
            await Send("end-following-defer-complete");await presenter.SetPresentationActiveAsync(true);
            await Wait(()=>View()?.Items.Count==20 && Current(0));await SettleScroll();
            Check(Scroll().VerticalOffset<=1,"Hidden presentation retires deferred reveal before late data and reactivation");
            await Send("end-following-empty");
            await Wait(() => View()?.Items.Count == 0);
            Parent().Focus(FocusState.Keyboard);
            await Send("end-following-long");
            await Wait(() => View()?.Items.Count == 20 && Current(19) && AtEnd());
            await SettleScroll();
            Check(AtEnd() && FocusedParent(), "Cold exact reveal waits for actual long-row geometry without stealing composer focus");
            Trace("cold-long-reveal");
            await Send("end-following-start");
            await Wait(() => View()?.Items.Count == 30);
            Parent().Focus(FocusState.Keyboard);
            await GoToEnd();
            Trace("initial-end");
            for (var append = 0; append < 3; append++)
            {
                await Send("end-following-append");
                await Wait(() => Current(View()!.Items.Count - 1) && AtEnd());
                Check(FocusedParent(), "Live append " + append + " follows end without moving toolbar focus");
            }
            var priorTailIndex = View()!.Items.Count - 1;
            var priorTail = DeclaredKeys()[^1];
            var priorTailSource = FindNode(session!.GetState("indexed-owned")!.LastGood!.Snapshot.Root, "items")!.IndexedCollection!;
            Collection().Enter(new("items", priorTailSource.SourceId, priorTailSource.QueryGeneration, priorTail, View()!.Items.Count - 1));
            await Wait(() => Collection().CaptureFocusedItem()?.ItemKey == priorTail);
            await GoToEnd();
            await Wait(() => presenter.IsFocusPreviewOpen && ReaderReported());
            var retainedPreview = presenter.FocusPreviewChrome;
            var retainedRow = ((IndexedItem<WidgetIndexedRow>)View()!.Items[priorTailIndex]).Value;
            var retainedLease = presenter.FocusPreviewLeaseForValidation!;
            trace.Add($"preview-before-storm previewCount={retainedLease.Range.Source.Count} rowCount={retainedRow!.Lease.Range.Source.Count} same={ReferenceEquals(retainedLease, retainedRow.Lease)}");
            var retainedY = Position(priorTailIndex);
            for (var arrival = 0; arrival < 45; arrival++) await Send("end-following-append");
            await SettleScroll();
            Check(Collection().CaptureFocusedItem()?.ItemKey == priorTail && Math.Abs(Position(priorTailIndex) - retainedY) < 2,
                "Focused reader retains its exact occurrence and pixels through 45 arrivals");
            trace.Add($"preview-after-storm heldCurrent={retainedLease.IsCurrent} same={ReferenceEquals(presenter.FocusPreviewLeaseForValidation, retainedLease)} previewCount={presenter.FocusPreviewLeaseForValidation?.Range.Source.Count}");
            Check(retainedLease.IsCurrent && ReferenceEquals(presenter.FocusPreviewLeaseForValidation, retainedLease) &&
                ((IndexedItem<WidgetIndexedRow>)View()!.Items[priorTailIndex]).Value is { Lease.IsCurrent: true } currentRow &&
                !ReferenceEquals(currentRow.Lease, retainedLease) && ReferenceEquals(presenter.FocusPreviewChrome, retainedPreview) && presenter.IsFocusPreviewOpen && ReaderReported(),
                "Unchanged appended prefix preserves the same admitted preview and reader observation");
            await presenter.HandleControllerButtonAsync(ControllerButton.X);
            var inspect = Descendants(presenter.FocusPreviewChrome!).OfType<Button>().Single();
            await Wait(() => ReferenceEquals(FocusManager.GetFocusedElement(XamlRoot), inspect));
            for (var arrival = 0; arrival < 3; arrival++) await Send("end-following-append");
            Check(ReferenceEquals(presenter.FocusPreviewChrome, retainedPreview) &&
                ReferenceEquals(FocusManager.GetFocusedElement(XamlRoot), inspect) && ReaderReported(),
                "Entered preview retains its actual native control and focus through later arrivals");
            await presenter.HandleControllerButtonAsync(ControllerButton.B);
            Parent().Focus(FocusState.Keyboard);
            await Wait(() => !presenter.IsFocusPreviewOpen && !retainedLease.IsCurrent);
            Check(true, "Closing a preview releases its retired partial-page semantic lease");
            var editQuery = FindNode(session!.GetState("indexed-owned")!.LastGood!.Snapshot.Root, "items")!.IndexedCollection!;
            Collection().Enter(new("items", editQuery.SourceId, editQuery.QueryGeneration, priorTail, priorTailIndex));
            await Wait(() => presenter.IsFocusPreviewOpen && presenter.FocusPreviewLeaseForValidation?.IsCurrent == true);
            var beforeEdit = presenter.FocusPreviewLeaseForValidation!;
            await Send("end-following-edit");
            await Wait(() => !beforeEdit.IsCurrent && presenter.IsFocusPreviewOpen && presenter.FocusPreviewLeaseForValidation?.IsCurrent == true);
            Check(!ReferenceEquals(beforeEdit, presenter.FocusPreviewLeaseForValidation), "Actual preview content edit revokes the old semantic lease");
            var beforeReset = presenter.FocusPreviewLeaseForValidation!;
            await Send("end-following-reset");
            await Wait(() => !beforeReset.IsCurrent);
            Check(true, "Semantic source reset revokes retained preview authority");
            Parent().Focus(FocusState.Keyboard);
            await Wait(() => !presenter.IsFocusPreviewOpen);
            await GoToEnd();
            await Send("end-following-grow");
            await Wait(() => Current(View()!.Items.Count - 1) && AtEnd());
            Check(FocusedParent(), "Growing existing message content preserves end and external focus");
            await Send("end-following-shrink");
            await Wait(() => Current(View()!.Items.Count - 1) && AtEnd());
            await SettleScroll();
            Check(FocusedParent(), "Shrinking existing content preserves end and external focus");
            presenter.ScrollBy(0, -180);
            await Wait(() => !AtEnd());
            await SettleScroll();
            var olderAnchor = Collection().CaptureViewport(new("root", "items", ViewNodeKind.IndexedCollection, ""))!;
            var olderPosition = Position(olderAnchor.Index);
            Trace("older-reader-before");
            await Send("end-following-append");
            await SettleScroll();
            Trace("older-reader-after");
            var olderIndex = DeclaredKeys().ToList().IndexOf(olderAnchor.Key);
            await Wait(() => Current(olderIndex) && Math.Abs(Position(olderIndex) - olderPosition) < 2);
            Check(!AtEnd(), "Older reader keeps exact occurrence and pixels while tail appends");
            Check(FocusedParent(), "Older reader retains toolbar focus");
            await GoToEnd();
            var query = session!.GetState("indexed-owned")!.LastGood!.Snapshot;
            var pending = session.SendActionAsync(session.GetState("indexed-owned")!.LastGood!.Authority,
                new("end-following-append", "root", InputScopeId: "root"), lifetime.Token);
            presenter.ScrollBy(0, -180);
            await pending;
            await Wait(() => session.GetState("indexed-owned")!.LastGood!.Snapshot != query);
            await Task.Delay(300, lifetime.Token);
            Check(!AtEnd() && FocusedParent(), "Explicit scroll departure wins over an in-flight append");
            await GoToEnd();
            await Send("end-following-reset");
            await Task.Delay(300, lifetime.Token);
            Check(FocusedParent(), "Semantic reset never transfers row focus");
            await GoToEnd();
            await Send("end-following-roll");
            await Wait(() => Current(View()!.Items.Count - 1) && AtEnd());
            Check(FocusedParent() && RecoveryCounts().Started == 0, "A wholly replaced forward window follows end without recovery or focus movement");
            presenter.ScrollBy(0, -180);
            await Wait(() => !AtEnd()); await SettleScroll();
            var anchor = Collection().CaptureViewport(new("root", "items", ViewNodeKind.IndexedCollection, ""))!;
            var position = Position(anchor.Index);
            await Send("end-following-roll");
            await Wait(() => RecoveryCounts().Finished == 1 && DeclaredKeys().Contains(anchor.Key));
            var repairedIndex = DeclaredKeys().ToList().IndexOf(anchor.Key);
            await Wait(() => Current(repairedIndex) && Math.Abs(Position(repairedIndex) - position) < 2);
            Check(FocusedParent() && RecoveryCounts().Committed == 1, "A departed reader recovers its exact anchor and pixels without stealing focus");
            await Send("end-following-append");
            await SettleScroll();
            Check(RecoveryCounts().Started == 1, "Ordinary later events do not repeat around-key recovery");
            await GoToEnd();
            presenter.ScrollBy(0, -180); await Wait(() => !AtEnd()); await SettleScroll();
            await Send("end-following-hold");
            await Send("end-following-roll");
            await Wait(() => RecoveryCounts().Started == 2);
            presenter.ScrollBy(0, -80);
            await SettleScroll();
            var movedAnchor = Collection().CaptureViewport(new("root", "items", ViewNodeKind.IndexedCollection, ""))!;
            var movedPosition = Position(movedAnchor.Index);
            trace.Add($"user-scroll-before-late-page key={movedAnchor.Key} y={movedPosition}");
            await Send("end-following-release");
            await Wait(() => RecoveryCounts().Finished == 2);
            await Wait(() => DeclaredKeys().Contains(movedAnchor.Key));
            var movedIndex = DeclaredKeys().ToList().IndexOf(movedAnchor.Key);
            await Wait(() => Current(movedIndex) && Math.Abs(Position(movedIndex) - movedPosition) < 2);
            trace.Add($"user-scroll-after-late-page key={movedAnchor.Key} y={Position(movedIndex)}");
            Check(FocusedParent(), "Late around data preserves the newer user anchor, pixels and focus");
            await Send("end-following-prepend");
            movedIndex = DeclaredKeys().ToList().IndexOf(movedAnchor.Key);
            await Wait(() => Current(movedIndex) && Math.Abs(Position(movedIndex) - movedPosition) < 2);
            Check(FocusedParent(), "Subsequent older publication preserves the recovered reader anchor");
            var focusedKeys = DeclaredKeys();
            var focusedKey = focusedKeys[5];
            var focusedSource = FindNode(session!.GetState("indexed-owned")!.LastGood!.Snapshot.Root, "items")!.IndexedCollection!;
            Collection().Enter(new("items", focusedSource.SourceId, focusedSource.QueryGeneration, focusedKey, 5));
            await Wait(() => Collection().CaptureFocusedItem()?.ItemKey == focusedKey && Current(5));
            await SettleScroll();
            var focusDepartureAnchor = Collection().CaptureViewport(new("root", "items", ViewNodeKind.IndexedCollection, ""))!;
            var focusDepartureY = Position(focusDepartureAnchor.Index);
            await Send("end-following-hold"); await Send("end-following-roll");
            await Wait(() => RecoveryCounts().Started == 3);
            Parent().Focus(FocusState.Keyboard);
            Check(FocusedParent(), "Explicit focus departure completes during held reader recovery");
            await Send("end-following-release");
            await Wait(() => RecoveryCounts().Finished == 3 && DeclaredKeys().Contains(focusDepartureAnchor.Key));
            var departureIndex = DeclaredKeys().ToList().IndexOf(focusDepartureAnchor.Key);
            await Wait(() => Current(departureIndex) && Math.Abs(Position(departureIndex) - focusDepartureY) < 2);
            Check(FocusedParent(), "Reader repair does not reclaim focus after explicit departure");
            await Send("end-following-hold"); await Send("end-following-roll");
            await Wait(() => RecoveryCounts().Started == 4);
            var commitsBeforeHide = RecoveryCounts().Committed;
            await presenter.SetPresentationActiveAsync(false);
            await session!.SetLifecycleAsync(session.GetTarget("indexed-owned"), WidgetRail.WidgetSdk.WidgetLifecycleState.Background, lifetime.Token);
            await session.EstablishPresentationAsync(session.GetTarget("indexed-owned"), WidgetRail.WidgetSdk.WidgetLifecycleState.Interactive, lifetime.Token);
            await Send("end-following-release");
            await Wait(() => RecoveryCounts().Finished == 4);
            Check(RecoveryCounts().Committed == commitsBeforeHide, "Lifecycle exit rejects a cancellation-ignoring late provider commit");
            await presenter.SetPresentationActiveAsync(true);
            await Send("end-following-reset");
            // A semantic reset intentionally withholds old-domain continuity.
            // Establish the new domain's complete keys before its recovery case.
            await Send("end-following-append");
            await Wait(() => DeclaredKeys().Count > 10);
            var deleteSource = FindNode(session.GetState("indexed-owned")!.LastGood!.Snapshot.Root, "items")!.IndexedCollection!;
            var deletedKey = DeclaredKeys()[10];
            var neighborKey = DeclaredKeys()[11];
            Collection().Enter(new("items", deleteSource.SourceId, deleteSource.QueryGeneration, deletedKey, 10));
            await Wait(() => Current(10) && Collection().CaptureFocusedItem()?.ItemKey == deletedKey);
            await SettleScroll();
            await Send("end-following-delete-anchor");
            await Send("end-following-roll");
            await Wait(() => RecoveryCounts().Finished == 5 && DeclaredKeys().Contains(neighborKey));
            await Wait(() => Collection().CaptureFocusedItem()?.ItemKey == neighborKey);
            var neighborIndex = DeclaredKeys().ToList().IndexOf(neighborKey);
            await Wait(() => Current(neighborIndex) && Position(neighborIndex) >= -1 && Position(neighborIndex) < Scroll().ViewportHeight);
            Check(!DeclaredKeys().Contains(deletedKey), "Deleted recovery anchor chooses visible surviving old neighbor instead of disjoint newest tail");
            Check(failure is null, "End following reported no native presenter error");
            Write(true, null);
        }
        catch (Exception error) { Trace("failure"); Write(false, error.ToString()); }

        Button Parent() => Descendants(presenter).OfType<Button>().Single(b => AutomationProperties.GetAutomationId(b) == "Widget.parent");
        WidgetIndexedCollectionView Collection() => Descendants(presenter).OfType<WidgetIndexedCollectionView>().Single();
        IReadOnlyList<string> DeclaredKeys() => FindNode(session!.GetState("indexed-owned")!.LastGood!.Snapshot.Root, "items")!.IndexedOccurrenceKeys ?? [];
        double Position(int index) => (View()!.ContainerFromIndex(index) as FrameworkElement)?.TransformToVisual(Scroll()).TransformPoint(new(0, 0)).Y ?? double.NaN;
        (int Started, int Committed, int Finished) RecoveryCounts()
        {
            var values = Descendants(presenter).OfType<TextBlock>().Single(t => AutomationProperties.GetAutomationId(t) == "Widget.preview.reads").Text.Split(':');
            return (int.Parse(values[0]), int.Parse(values[1]), int.Parse(values[2]));
        }
        bool ReaderReported() => Descendants(presenter).OfType<TextBlock>().Any(t => AutomationProperties.GetAutomationId(t) == "Widget.background-state" && t.Text == "reading");
        bool FocusedParent() => ReferenceEquals(FocusManager.GetFocusedElement(XamlRoot), Parent());
        ScrollViewer Scroll() => Descendants(View()!).OfType<ScrollViewer>().First();
        bool AtEnd() => Scroll().ViewportHeight > 0 && Scroll().ScrollableHeight - Scroll().VerticalOffset <= 1;
        bool Current(int index) => View()!.Items[index] is IndexedItem<WidgetIndexedRow> { Value.Lease.IsCurrent: true };
        async Task GoToEnd()
        {
            View()!.ScrollIntoView(View()!.Items[View()!.Items.Count - 1], ScrollIntoViewAlignment.Default);
            await Wait(() => Current(View()!.Items.Count - 1));
            for (var attempt = 0; attempt < 3; attempt++)
            {
                await SettleScroll();
                Scroll().ChangeView(null, Scroll().ScrollableHeight, null, true);
                await SettleScroll();
                if (AtEnd()) break;
            }
            await Wait(AtEnd);
            await Task.Delay(120, lifetime.Token);
        }
        async Task SettleScroll()
        {
            var prior = double.NaN;
            var priorExtent = double.NaN;
            var stable = 0;
            var deadline = Environment.TickCount64 + 5000;
            while (stable < 5)
            {
                if (Environment.TickCount64 > deadline) throw new TimeoutException("Scroll animation did not settle.");
                await Task.Delay(40, lifetime.Token);
                var value = Scroll().VerticalOffset;
                var extent = Scroll().ScrollableHeight;
                stable = Math.Abs(value - prior) < 0.1 && Math.Abs(extent - priorExtent) < 0.1 ? stable + 1 : 0;
                prior = value;
                priorExtent = extent;
            }
        }
        async Task Send(string action)
        {
            var frame = session!.GetState("indexed-owned")!.LastGood!;
            var oldSource = FindNode(frame.Snapshot.Root, "items")!.IndexedCollection;
            await session.SendActionAsync(frame.Authority, new(action, "root", InputScopeId: "root"), lifetime.Token);
            await Wait(() => session.GetState("indexed-owned")!.LastGood!.Snapshot != frame.Snapshot &&
                (action is "end-following-hold" or "end-following-release" or "end-following-delete-anchor" || FindNode(session.GetState("indexed-owned")!.LastGood!.Snapshot.Root, "items")!.IndexedCollection != oldSource) &&
                Descendants(presenter).OfType<TextBlock>().Any(t => AutomationProperties.GetAutomationId(t) == "Widget.status" && t.Text == action));
        }
        async Task Wait(Func<bool> condition)
        {
            var deadline = Environment.TickCount64 + 8000;
            while (!condition())
            {
                if (Environment.TickCount64 > deadline) { Trace("timeout"); throw new TimeoutException("End following did not settle: " + presenter.FocusDiagnostics()); }
                await Task.Delay(20, lifetime.Token);
            }
        }
        void Check(bool passed, string message) { if (!passed) throw new InvalidOperationException(message); checks.Add(message); }
        void Trace(string phase)
        {
            if (View() is not { } view) return;
            var descriptor = session?.GetState("indexed-owned")?.LastGood?.Snapshot is { } snapshot ? FindNode(snapshot.Root, "items")?.IndexedCollection : null;
            trace.Add($"{phase}: count={view.Items.Count} y={Scroll().VerticalOffset} max={Scroll().ScrollableHeight} viewport={Scroll().ViewportHeight} lastCurrent={(view.Items.Count > 0 && Current(view.Items.Count - 1))} recoveries={RecoveryCounts()} source={descriptor} reveal={JsonSerializer.Serialize(presenter.ScrollRevealDiagnostics())} preview={JsonSerializer.Serialize(presenter.FocusPreviewDiagnostics())}");
            if(phase is "timeout" or "failure" && Descendants(presenter).OfType<WidgetIndexedCollectionView>().FirstOrDefault() is {} collection)
                trace.Add(JsonSerializer.Serialize(new{phase,navigation=collection.NavigationDiagnosticState(),continuity=collection.ContinuityStateForValidation()}));
        }
        void Write(bool passed, string? error)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
            File.WriteAllText(path, JsonSerializer.Serialize(new { passed, checks, trace, error }, new JsonSerializerOptions { WriteIndented = true }));
        }
    }
}
