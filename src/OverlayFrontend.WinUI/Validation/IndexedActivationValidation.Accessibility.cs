using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using WidgetRail.OverlayFrontend.WinUI.Collections;
using WidgetRail.OverlayFrontend.WinUI.Presentation;
using WidgetRail.WidgetPresentationSession;
using WidgetRail.WidgetProtocol;
using PresentationSession = WidgetRail.WidgetPresentationSession.WidgetPresentationSession;

namespace WidgetRail.OverlayFrontend.WinUI.Validation;

internal static partial class IndexedActivationValidation
{
    internal static async Task<Result> RunAccessibilityAsync(WidgetViewPresenter presenter, PresentationSession session, CancellationToken cancellation)
    {
        var checks = new List<string>();
        try
        {
            await VerifyAccessibilityAsync(presenter, session, cancellation, checks, establishNativeFocus: true);
            return new("passed", checks, null);
        }
        catch (Exception error) { return new("failed", checks, error.ToString()); }
    }

    private static async Task VerifyAccessibilityAsync(WidgetViewPresenter presenter, PresentationSession session,
        CancellationToken cancellation, List<string> checks, bool establishNativeFocus = false)
    {
        try
        {
            foreach (var grid in new[] { false, true })
            {
                var label = grid ? "GridView" : "ListView";
                await Send("activation-mode");
                await Ready();
                if (grid) { await Send("grid"); await Send("focus-exact"); await Ready(); }
                var view = View();
                Check((view is GridView) == grid && view.SelectionMode == ListViewSelectionMode.None,
                    label + " retains command-item semantics without native selection mutation");
                var container = (SelectorItem)view.ContainerFromIndex(75);
                var peer = FrameworkElementAutomationPeer.CreatePeerForElement(container);
                var invoke = peer.GetPattern(PatternInterface.Invoke) as IInvokeProvider
                    ?? throw new InvalidOperationException("Indexed row has no native Invoke provider.");
                Check(peer.GetItemStatus() == string.Empty,
                    label + " native item peer exposes Invoke with initially empty authored status");

                await Send("activation-selected");
                await Until(() => Row() is { Lease.IsCurrent: true, Item.Root.IsSelected: true });
                Check(ReferenceEquals(container, View().ContainerFromIndex(75)) && peer.GetItemStatus() == "Selected" && !container.IsSelected,
                    label + " retained peer announces authored selection without setting native selection");
                await Send("activation-busy");
                await Until(() => Row() is { Lease.IsCurrent: true, Item.Root.IsBusy: true });
                Check(container.IsEnabled && ReferenceEquals(FocusManager.GetFocusedElement(presenter.XamlRoot), container) &&
                    peer.GetItemStatus() == "Selected, Busy",
                    label + " busy row retains native focus and reports selected/busy status");
                var before = Calls();
                invoke.Invoke();
                await presenter.HandleControllerButtonAsync(ControllerButton.A);
                await Task.Delay(250, cancellation);
                Check(Calls() == before, label + " busy row rejects both native UIA and controller activation");

                await Send("activation-ready");
                await Until(() => Row() is { Lease.IsCurrent: true, Item.Root.IsSelected: not true, Item.Root.IsBusy: not true });
                Check(ReferenceEquals(container, View().ContainerFromIndex(75)) && peer.GetItemStatus() == string.Empty &&
                    ReferenceEquals(FocusManager.GetFocusedElement(presenter.XamlRoot), container),
                    label + " clearing authored states updates the existing peer and preserves focus");
                before = Calls();
                invoke.Invoke();
                await Until(() => Status() == "row:306:75:open");
                Check(Calls() == before + 1, label + " native UIA invocation dispatches one admitted row action");

                presenter.SetPresentationInputEnabled(false);
                before = Calls();
                invoke.Invoke();
                await Task.Delay(250, cancellation);
                Check(Calls() == before, label + " retained native provider cannot dispatch after host input revocation");
                presenter.SetPresentationInputEnabled(true);

                // Changing the layout retires the old native view and its event
                // subscription. Its cached provider must not activate the new row.
                await Send("grid");
                await Send("focus-exact");
                await Ready();
                Check(!ReferenceEquals(view, View()) && !container.IsLoaded,
                    label + " owner replacement detaches the previously exposed native container");
                before = Calls();
                try { invoke.Invoke(); }
                catch (Exception error) when (error.HResult is unchecked((int)0x80040200) or unchecked((int)0x80040201)) { }
                await Task.Delay(250, cancellation);
                Check(Calls() == before, label + " detached native provider cannot dispatch into a replacement owner");
            }
        }
        finally { presenter.SetPresentationInputEnabled(true); }

        ListViewBase View() => Descendants(presenter).OfType<ListViewBase>().Single();
        WidgetIndexedRow? Row() => View().Items.Count > 75 && View().Items[75] is IndexedItem<WidgetIndexedRow> slot ? slot.Value : null;
        WidgetPresentationFrame Frame() => session.GetState("indexed-owned")?.LastGood ?? throw new InvalidOperationException("Missing indexed frame");
        string? Status() => Find(Frame().Snapshot.Root, "status")?.Text;
        int Calls() => int.Parse(Find(Frame().Snapshot.Root, "calls")!.Text![7..], System.Globalization.CultureInfo.InvariantCulture);
        async Task Ready()
        {
            if (establishNativeFocus)
            {
                // The standalone state test establishes a native focus precondition.
                // It does not qualify authored query-reset focus requests, which
                // remain covered separately by the full activation regression.
                await Until(() =>
                {
                    var current = View();
                    if (current.Items.Count <= 75) return false;
                    current.ScrollIntoView(current.Items[75]);
                    return Row()?.Lease.IsCurrent == true && current.ContainerFromIndex(75) is Control { IsLoaded: true, IsEnabled: true };
                });
                ((Control)View().ContainerFromIndex(75)).Focus(FocusState.Keyboard);
            }
            await Until(() => Row()?.Lease.IsCurrent == true && View().ContainerFromIndex(75) is Control { IsLoaded: true } item &&
                ReferenceEquals(FocusManager.GetFocusedElement(presenter.XamlRoot), item));
        }
        async Task Send(string action)
        {
            var frame = Frame();
            await session.SendActionAsync(frame.Authority, new(action, "root", InputScopeId: frame.Authority.ActiveInputScopeId), cancellation);
            await Until(() => Frame().Authority.SnapshotSequence > frame.Authority.SnapshotSequence);
        }
        async Task Until(Func<bool> condition)
        {
            var deadline = Environment.TickCount64 + 7000;
            while (!condition())
            {
                if (Environment.TickCount64 > deadline) throw new TimeoutException("Indexed accessibility probe did not settle; status=" + Status());
                await Task.Delay(20, cancellation);
            }
        }
        void Check(bool condition, string name)
        {
            if (!condition) throw new InvalidOperationException(name);
            checks.Add(name);
        }
    }
}
