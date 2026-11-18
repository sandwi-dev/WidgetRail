using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using WidgetRail.WidgetProtocol;
using WidgetRail.OverlayFrontend.WinUI.Presentation;
using WidgetRail.OverlayFrontend.WinUI.Collections;

namespace WidgetRail.OverlayFrontend.WinUI.Validation;

internal sealed partial class IndexedWidgetValidationPage
{
    private async Task ValidateDeferredPreviewsAsync(string path)
    {
        var checks=new List<string>();Exception? failed=null;
        var theme=new NativePopupTheme();theme.Attach(presenter);
        var layout=(Grid)Content;Content=null;var overlayRoot=new Grid();overlayRoot.Children.Add(layout);Content=overlayRoot;PresentationLayers.Attach(overlayRoot);
        try
        {
            await Send("deferred-mode");await Until(()=>View()?.Items.Count>2&&View()!.Items[1] is IndexedItem<WidgetIndexedRow> { Value.Lease.IsCurrent: true }&&View()!.ContainerFromIndex(1) is Control {IsEnabled:true});
            var parent=Descendants(presenter).OfType<Button>().Single(button=>AutomationProperties.GetAutomationId(button)=="Widget.parent");
            parent.Focus(FocusState.Keyboard);await Task.Delay(180);
            Check(Reads()==0,"Unfocused rows construct no previews");
            var first=(Control)View()!.ContainerFromIndex(0);Check(first.Focus(FocusState.Keyboard),"First row accepts focus");
            await Until(()=>Reads()==1);var second=(Control)View()!.ContainerFromIndex(1);second.Focus(FocusState.Keyboard);
            await Until(()=>presenter.IsFocusPreviewOpen&&PreviewText()=="item.1");
            Check(ReferenceEquals(FocusManager.GetFocusedElement(XamlRoot),second),"Resolved preview does not steal row focus");
            await Task.Delay(450);Check(PreviewText()=="item.1","Superseded slow result never replaces the current item");
            var reads=Reads();var chrome=presenter.FocusPreviewChrome;
            await presenter.HandleControllerButtonAsync(ControllerButton.X);
            await Until(()=>AutomationProperties.GetAutomationId(FocusManager.GetFocusedElement(XamlRoot) as DependencyObject)=="Widget.preview.apply");
            Check(ReferenceEquals(chrome,presenter.FocusPreviewChrome)&&Reads()==reads,"X enters the existing panel without another read");
            presenter.ActivateFocused();await Until(()=>FindNode(session!.GetState("indexed-owned")!.LastGood!.Snapshot.Root,"status")?.Text?.Contains("item.1:preview-apply")==true);
            Check(true,"Deferred preview action reaches its captured item through the real worker");
            await Until(() => Descendants(presenter).OfType<TextBlock>().Any(text => AutomationProperties.GetAutomationId(text) == "Widget.status" && text.Text.Contains("item.1:preview-apply")));
            var select = Descendants(presenter.FocusPreviewChrome!).OfType<Button>().Single(button => AutomationProperties.GetAutomationId(button) == "Widget.preview.equip");
            Check(select.Focus(FocusState.Keyboard), "Deferred preview select accepts native focus");
            var selectPeer = (IExpandCollapseProvider)FrameworkElementAutomationPeer.CreatePeerForElement(select).GetPattern(PatternInterface.ExpandCollapse);
            selectPeer.Expand();
            await Until(() => presenter.PreviewMenuItemsForValidation.Any(item => item.IsLoaded && AutomationProperties.GetAutomationId(item) == "Widget.preview.equip.Option.slot"));
            var option = presenter.PreviewMenuItemsForValidation.Single(item => AutomationProperties.GetAutomationId(item) == "Widget.preview.equip.Option.slot");
            var optionPeer = FrameworkElementAutomationPeer.CreatePeerForElement(option);
            ((IToggleProvider)optionPeer.GetPattern(PatternInterface.Toggle)).Toggle();
            await Until(() => Descendants(presenter).OfType<TextBlock>().Any(text => AutomationProperties.GetAutomationId(text) == "Widget.status" && text.Text.Contains("item.1:preview-equip")) &&
                presenter.IsPreviewEnteredForValidation && ReferenceEquals(FocusManager.GetFocusedElement(select.XamlRoot), select));
            Check(ReferenceEquals(chrome, presenter.FocusPreviewChrome) && Reads() == reads,
                "Native deferred select dispatches through the exact provider lease and retains entered panel, opener focus and cached content");
            var slider=Descendants(presenter.FocusPreviewChrome!).OfType<Slider>().Single();
            slider.Focus(FocusState.Keyboard);presenter.ActivateFocused();
            presenter.MoveFocus(FocusNavigationDirection.Right);
            await Until(()=>FindNode(session!.GetState("indexed-owned")!.LastGood!.Snapshot.Root,"status")?.Text?.Contains("item.1:preview-volume:55")==true);
            Check(true,"Typed numeric preview value crosses host, bridge and worker unchanged");
            presenter.ActivateFocused();
            await presenter.HandleControllerButtonAsync(ControllerButton.B);Check(ReferenceEquals(FocusManager.GetFocusedElement(XamlRoot),second),"B returns to the exact indexed occurrence");
            var third=(Control)View()!.ContainerFromIndex(2);third.Focus(FocusState.Keyboard);await Until(()=>PreviewText()=="item.2");
            var afterThird=Reads();second.Focus(FocusState.Keyboard);await Until(()=>PreviewText()=="item.1");
            Check(Reads()==afterThird,"Revisiting a current key uses the bounded cache");
            await Send("deferred-replace");await Until(()=>!presenter.IsFocusPreviewOpen||PreviewContext()=="Context 2");
            second=(Control)View()!.ContainerFromIndex(1);second.Focus(FocusState.Keyboard);await Until(()=>PreviewText()=="item.1"&&PreviewContext()=="Context 2");
            Check(Reads()>afterThird,"Context replacement retires cached metadata");
            await presenter.HandleControllerButtonAsync(ControllerButton.X);
            Descendants(presenter.FocusPreviewChrome!).OfType<Button>().Single(button => AutomationProperties.GetAutomationId(button) == "Widget.preview.apply").Focus(FocusState.Keyboard);
            await presenter.HandleControllerButtonAsync(ControllerButton.B);
            foreach (var mode in new[] { "deferred-primary", "deferred-reading" })
            {
                var modeReadStart = Reads();
                await Send(mode);
                await Until(() => View()?.Items.Count > 2 && View()!.Items[1] is IndexedItem<WidgetIndexedRow> { Value.Lease.IsCurrent: true } &&
                    View()!.ContainerFromIndex(1) is Control { IsEnabled: true } && !presenter.HasPendingMemoryRestore &&
                    Descendants(presenter).OfType<WidgetIndexedCollectionView>().All(collection => !collection.IsEntryPending));
                // Item zero deliberately resolves slowly. A must retain exactly one
                // entry request, not require a second press after content arrives.
                await Until(() => View()!.Items[0] is IndexedItem<WidgetIndexedRow> { Value.Lease.IsCurrent: true } &&
                    View()!.ContainerFromIndex(0) is Control target && ReferenceEquals(FocusManager.GetFocusedElement(XamlRoot), target) &&
                    WidgetViewPresenter.VisibleAnchorBounds(target, presenter) is { Valid: true } visible && visible.Height >= target.ActualHeight - .5);
                first = (Control)View()!.ContainerFromIndex(0);
                Check(first.Focus(FocusState.Keyboard), mode + ": settled cold row accepts native focus");
                await Until(() => ReferenceEquals(FocusManager.GetFocusedElement(XamlRoot), first));
                var coldReads = modeReadStart;
                await PressPrimaryAsync();
                await Until(() => ProviderWaiting() && presenter.HasPendingPrimaryPreviewForValidation);
                Check(ReferenceEquals(FocusManager.GetFocusedElement(XamlRoot), first) && !presenter.IsFocusPreviewOpen &&
                    FindNode(session!.GetState("indexed-owned")!.LastGood!.Snapshot.Root, "status")?.Text == mode,
                    mode + ": accepted A waits on the exact source without an early row action");
                await Send("deferred-release");
                await Until(() => PreviewText() == "item.0" &&
                    AutomationProperties.GetAutomationId(FocusManager.GetFocusedElement(XamlRoot) as DependencyObject) == "Widget.preview.apply");
                Check(Reads() == coldReads + 1, mode + ": cold A enters after one deferred read without a second press");
                await presenter.HandleControllerButtonAsync(ControllerButton.B);
                second = (Control)View()!.ContainerFromIndex(1); second.Focus(FocusState.Keyboard);
                await Until(() => presenter.IsFocusPreviewOpen && PreviewText() == "item.1");
                var beforeEntry = Reads(); chrome = presenter.FocusPreviewChrome;
                await PressPrimaryAsync();
                await Until(() => AutomationProperties.GetAutomationId(FocusManager.GetFocusedElement(XamlRoot) as DependencyObject) == "Widget.preview.apply");
                Check(Reads() == beforeEntry && ReferenceEquals(chrome, presenter.FocusPreviewChrome), mode + ": A enters existing exact indexed preview without another read");
                Check(FindNode(session!.GetState("indexed-owned")!.LastGood!.Snapshot.Root, "status")?.Text == "deferred-release",
                    mode + ": entry does not dispatch the row primary action");
                await presenter.HandleControllerButtonAsync(ControllerButton.B);
                Check(ReferenceEquals(FocusManager.GetFocusedElement(XamlRoot), second), mode + ": B returns to the indexed occurrence");
                await ClickVisibleSourceAsync(second);
                await Until(() => AutomationProperties.GetAutomationId(FocusManager.GetFocusedElement(XamlRoot) as DependencyObject) == "Widget.preview.apply");
                Check(FindNode(session!.GetState("indexed-owned")!.LastGood!.Snapshot.Root, "status")?.Text == "deferred-release",
                    mode + ": native click enters exact indexed preview without a row action");
                await presenter.HandleControllerButtonAsync(ControllerButton.B);
                var peer = FrameworkElementAutomationPeer.CreatePeerForElement(second);
                var invoke = peer.GetPattern(PatternInterface.Invoke) as IInvokeProvider ?? throw new InvalidOperationException("Native row Invoke missing.");
                invoke.Invoke();
                await Until(() => AutomationProperties.GetAutomationId(FocusManager.GetFocusedElement(XamlRoot) as DependencyObject) == "Widget.preview.apply");
                Check(FindNode(session!.GetState("indexed-owned")!.LastGood!.Snapshot.Root, "status")?.Text == "deferred-release",
                    mode + ": native UIA invokes the exact preview without a row action");
                var previousContext = PreviewContext();
                await Send("deferred-replace");
                await Until(() => !presenter.IsFocusPreviewOpen || PreviewContext() != previousContext);
                Check(!presenter.OwnsKeyboardEscape, mode + ": context replacement retires entered preview input");
                await Send("deferred-hold");
                await Send("deferred-focus-first");
                await Until(() => !presenter.HasPendingMemoryRestore &&
                    Descendants(presenter).OfType<WidgetIndexedCollectionView>().All(collection => !collection.IsEntryPending) &&
                    View()!.Items[0] is IndexedItem<WidgetIndexedRow> { Value.Lease.IsCurrent: true } &&
                    View()!.ContainerFromIndex(0) is Control target && ReferenceEquals(FocusManager.GetFocusedElement(XamlRoot), target) &&
                    WidgetViewPresenter.VisibleAnchorBounds(target, presenter) is { Valid: true } visible && visible.Height >= target.ActualHeight - .5);
                first = (Control)View()!.ContainerFromIndex(0); Check(first.Focus(FocusState.Keyboard), mode + ": cancellation probe focuses a visible source");
                await PressPrimaryAsync();
                await Until(() => ProviderWaiting() && presenter.HasPendingPrimaryPreviewForValidation);
                parent.Focus(FocusState.Keyboard);
                await Send("deferred-release");
                await Until(() => !presenter.HasPendingDeferredPreviewReadForValidation && !presenter.HasPendingPrimaryPreviewForValidation);
                Check(ReferenceEquals(FocusManager.GetFocusedElement(XamlRoot), parent) && !presenter.OwnsKeyboardEscape,
                    mode + ": focus departure cancels pending primary entry before the delayed result");
                await Send("deferred-hold");
                await Send("deferred-focus-first");
                await Until(() => ReferenceEquals(FocusManager.GetFocusedElement(XamlRoot), first) &&
                    WidgetViewPresenter.VisibleAnchorBounds(first, presenter) is { Valid: true } visible && visible.Height >= first.ActualHeight - .5);
                await PressPrimaryAsync();
                await Until(() => ProviderWaiting() && presenter.HasPendingPrimaryPreviewForValidation);
                await Send("deferred-replace");
                await Until(() => !presenter.HasPendingPrimaryPreviewForValidation);
                await Send("deferred-release");
                await Until(() => !presenter.HasPendingDeferredPreviewReadForValidation);
                Check(!presenter.OwnsKeyboardEscape && !presenter.HasPendingPrimaryPreviewForValidation &&
                    ReferenceEquals(FocusManager.GetFocusedElement(XamlRoot), first),
                    mode + ": provider revision replacement cancels pending primary entry and retains source focus");
            }
            if (FindNode(session!.GetState("indexed-owned")!.LastGood!.Snapshot.Root, "items")?.IndexedSelection is not null)
            {
                await Send("deferred-selection");
                var collection = Descendants(presenter).OfType<WidgetIndexedCollectionView>().Single();
                await Until(() => collection.SelectionActive);
                second = (Control)View()!.ContainerFromIndex(1); second.Focus(FocusState.Keyboard);
                Check(presenter.CaptureControllerGuide().Any(hint => hint.Button == ControllerButton.A && hint.Label == "Select"),
                    "Selection mode advertises Select instead of primary preview entry");
                await PressPrimaryAsync();
                await Until(() => collection.IsSelected(1));
                Check(!presenter.OwnsKeyboardEscape, "A selects the indexed row without entering its preview");
                await ClickVisibleSourceAsync(second);
                await Until(() => !collection.IsSelected(1));
                Check(!presenter.OwnsKeyboardEscape, "Pointer and controller use the same selection-mode precedence");
            }
            await presenter.SetPresentationActiveAsync(false);Check(!presenter.IsFocusPreviewOpen,"Suspension hides and retires preview content");
            await presenter.SetPresentationActiveAsync(true);
            await Send("deferred-readonly");await Until(()=>View()?.Items.Count>2&&View()!.Items[1] is IndexedItem<WidgetIndexedRow> { Value.Lease.IsCurrent: true }&&View()!.ContainerFromIndex(1) is Control {IsEnabled:true});
            await Until(()=>!presenter.HasPendingMemoryRestore && Descendants(presenter).OfType<WidgetIndexedCollectionView>().All(view=>!view.IsEntryPending));
            second=(Control)View()!.ContainerFromIndex(1);Check(second.Focus(FocusState.Keyboard),"Current read-only row accepts focus after query replacement settles");
            await Until(()=>presenter.IsFocusPreviewOpen&&PreviewText()=="item.1");
            Check(!Descendants(presenter.FocusPreviewChrome!).OfType<Button>().Any(),"Read-only resolved content has no action controls");
            Check(ReferenceEquals(FocusManager.GetFocusedElement(XamlRoot),second),"Read-only preview retains source focus");
            if(failure is not null)throw new Exception(failure);
        }
        catch(Exception error){failed=error;}
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path,JsonSerializer.Serialize(new{passed=failed is null,checks,error=failed?.ToString()}));
        if(failed is not null)throw failed;
        void Check(bool value,string message){if(!value)throw new InvalidOperationException(message);checks.Add(message);}
        bool ProviderWaiting() => FindNode(session!.GetState("indexed-owned")!.LastGood!.Snapshot.Root, "background-state")?.Text == "preview-waiting";
        int Reads()=>int.TryParse(FindNode(session!.GetState("indexed-owned")!.LastGood!.Snapshot.Root,"preview.reads")?.Text,out var value)?value:0;
        string? PreviewText()=>presenter.FocusPreviewChrome is {} chrome?Descendants(chrome).OfType<TextBlock>().FirstOrDefault(block=>AutomationProperties.GetAutomationId(block)=="Widget.preview.key")?.Text:null;
        string? PreviewContext()=>presenter.FocusPreviewChrome is {} chrome?Descendants(chrome).OfType<TextBlock>().FirstOrDefault(block=>AutomationProperties.GetAutomationId(block)=="Widget.preview.context")?.Text:null;
        async Task PressPrimaryAsync()
        {
            await presenter.HandleControllerButtonAsync(ControllerButton.A);
            await presenter.HandleControllerButtonAsync(ControllerButton.A, ControllerEventPhase.Released);
        }
        async Task ClickVisibleSourceAsync(Control target)
        {
            var bounds = WidgetViewPresenter.VisibleAnchorBounds(target, target)
                ?? throw new InvalidOperationException("Native source click requires a visible row.");
            var preview = presenter.FocusPreviewChrome;
            var obstruction = preview?.TransformToVisual(target).TransformBounds(new(0, 0, preview.ActualWidth, preview.ActualHeight));
            // A small viewport can center the existing preview over part of a row.
            // Click an interior source point, not clipped pixels or its popup.
            // Leave room for the existing native pressed-scale animation.
            foreach (var x in new[] { bounds.X + Math.Min(64, bounds.Width / 4), bounds.Right - Math.Min(64, bounds.Width / 4), bounds.X + bounds.Width / 2 })
            {
                var point = new Windows.Foundation.Point(x, bounds.Y + bounds.Height / 2);
                if (obstruction?.Contains(point) == true) continue;
                await NativeContextProbeInput.LeftClickAsync(target, point);
                return;
            }
            throw new InvalidOperationException("Preview obscures every native source click point.");
        }
        async Task Send(string action)
        {
            var frame=session!.GetState("indexed-owned")!.LastGood!;var before=publication;
            await session.SendActionAsync(frame.Authority,new(action,"root",InputScopeId:"root"),lifetime.Token);
            // A late preview-read publication is not the requested mode change.
            await Until(()=>publication>before && presenter.CurrentBinding is { } current &&
                FindNode(current.Frame.Snapshot.Root,"status")?.Text==action);
        }
        async Task Until(Func<bool> predicate)
        {var deadline=Environment.TickCount64+10000;while(!predicate()){if(Environment.TickCount64>deadline)throw new TimeoutException("Deferred preview did not settle: "+JsonSerializer.Serialize(new { preview = presenter.FocusPreviewDiagnostics(), rows = View()?.Items.OfType<IndexedItem<WidgetIndexedRow>>().Take(3).Select(slot => new { slot.Index, key = slot.Value?.Item.Key, current = slot.Value?.Lease.IsCurrent, previewKey = slot.Value?.Item.Root.FocusPreviewKey }).ToArray(), focused = FocusManager.GetFocusedElement(XamlRoot) is DependencyObject target ? AutomationProperties.GetAutomationId(target) : null }));await Task.Delay(15,lifetime.Token);}}
    }
}
