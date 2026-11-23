using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using WidgetRail.OverlayFrontend.WinUI.Collections;
using WidgetRail.OverlayFrontend.WinUI.Presentation;
using WidgetRail.WidgetProtocol;
using Windows.System;
namespace WidgetRail.OverlayFrontend.WinUI.Validation;
internal sealed partial class IndexedWidgetValidationPage
{
    private async Task ValidateSelectionAsync(string path)
    {
        var checks = new List<string>(); var phase = "startup";
        var trace=new Queue<string>();
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        try
        {
            Background = new SolidColorBrush(Microsoft.UI.Colors.Black);
            App.Window!.AppWindow.ResizeClient(new(1400,1000));
            await Wait(() => View() is { IsLoaded: true } && Parent() is { IsLoaded: true });
            await Task.Delay(250,lifetime.Token);
            foreach (var mode in new[] { "selection-list", "selection-grid", "selection-grouped" })
            {
                phase = mode; await Send(mode); await FocusItem(0);
                Collection().NavigationTrace=message=>{if(trace.Count==512)trace.Dequeue();trace.Enqueue(message);};
                await Send("selection-end");await FocusItem(0);
                var heldCalls=Calls();
                Check(await presenter.HandleControllerButtonAsync(ControllerButton.LeftTrigger),mode+": L2 starts framework-owned selection");
                await Selected(0);
                Check(Collection().MoveFocus(FocusNavigationDirection.Down),mode+": held directional navigation enters another row");
                await Wait(()=>Collection().CaptureFocusedItem() is {Index:>0});
                var heldTarget=Collection().CaptureFocusedItem()!.Index;
                await Selected(0,heldTarget);
                Check(Selection().Count==2,mode+": L2 selects visited grid positions without the intervening range");
                Check(Collection().MoveFocus(FocusNavigationDirection.Up),mode+": held reverse navigation returns to the original row");
                await Wait(()=>Collection().CaptureFocusedItem()?.Index==0);
                await Selected(0,heldTarget);
                Check(true,mode+": revisiting a row while holding L2 does not deselect it");
                await presenter.HandleControllerButtonAsync(ControllerButton.LeftTrigger,ControllerEventPhase.Released);
                Check(Collection().MoveFocus(FocusNavigationDirection.Down),mode+": released navigation still works");
                await Wait(()=>Collection().CaptureFocusedItem()?.Index==heldTarget);
                Check(Collection().MoveFocus(FocusNavigationDirection.Down),mode+": released navigation can reach an unselected row");
                await Wait(()=>Collection().CaptureFocusedItem() is {} next && next.Index!=heldTarget);
                await Selected(0,heldTarget);
                Check(Calls()==heldCalls,mode+": held selection never invokes an item's primary action");
                var third=Collection().CaptureFocusedItem()!.Index;
                await Send("selection-trigger-conflict");await FocusItem(third);
                var conflictCalls=Calls();
                await presenter.HandleControllerButtonAsync(ControllerButton.LeftTrigger);
                await Selected(new[]{0,heldTarget,third}.Distinct().Order().ToArray());
                Check(Calls()==conflictCalls,mode+": active selection takes LT precedence over an authored parent shortcut");
                Check(presenter.CaptureControllerGuide().Where(hint=>hint.Button==ControllerButton.LeftTrigger).Single().Label=="Hold to select",
                    mode+": active selection guide replaces the conflicting LT shortcut hint");
                Parent().Focus(FocusState.Keyboard);
                await presenter.HandleControllerButtonAsync(ControllerButton.LeftTrigger,ControllerEventPhase.Released);
                Check(true,mode+": focus departure and release retire the held gesture");
                await Send("selection-end");await FocusItem(0);
                conflictCalls=Calls();
                await presenter.HandleControllerButtonAsync(ControllerButton.LeftTrigger);
                await Wait(()=>Calls()!=conflictCalls);
                Check(!Collection().SelectionActive,mode+": outside selection the authored LT shortcut keeps precedence");
                await FocusItem(0);
                Check(!await presenter.HandleControllerButtonAsync(ControllerButton.LeftTrigger,ControllerEventPhase.Released),
                    mode+": a completed selection gesture cannot claim a later unrelated LT release");
            }
            phase="focus request withdrawal ownership";
            presenter.FocusTrace=message=>{if(trace.Count==512)trace.Dequeue();trace.Enqueue(message);};
            await Send("selection-list");await FocusItem(0);
            await Send("focus-default");
            // Being on row zero already does not prove the newly published
            // default-entry request has run. Wait for the presenter-owned request
            // too before testing withdrawal of a *completed* request.
            await Wait(()=>Collection().CaptureFocusedItem()?.Index==0&&!Collection().HasPendingNativeNavigation &&
                presenter.IsGuidePresentationReady(requireFocus:true));
            await Send("selection-delay");
            var entryQuery=FindNode(session!.GetState("indexed-owned")!.LastGood!.Snapshot.Root,"items")!.IndexedCollection!;
            Check(Collection().Enter(new("items",entryQuery.SourceId,entryQuery.QueryGeneration,"item.75",75)),
                "A newer native collection entry starts after the authored entry request completed");
            Check(Collection().HasPendingNativeNavigation,"The newer entry waits for delayed range data");
            await Send("focus-clear");
            Check(Collection().HasPendingNativeNavigation,"Withdrawing the completed request preserves newer native navigation");
            await Send("selection-release");
            await Wait(()=>Collection().CaptureFocusedItem()?.Index==75&&!Collection().HasPendingNativeNavigation);
            Check(true,"The unrelated native entry completes after old-request withdrawal");
            await Send("selection-delay");
            await Send("focus-exact-85");
            await Wait(()=>Collection().HasPendingNativeNavigation);
            await Send("focus-clear");
            Check(!Collection().HasPendingNativeNavigation,"Withdrawing a pending authored request cancels its own entry");
            await Send("selection-release");await Task.Delay(400,lifetime.Token);
            Check(Collection().CaptureFocusedItem()?.Index!=85&&!Collection().HasPendingNativeNavigation,
                "Delayed range completion cannot revive the withdrawn authored request");
            phase="held selection lifecycle";await Send("selection-list");await FocusItem(0);
            await presenter.HandleControllerButtonAsync(ControllerButton.LeftTrigger);await Selected(0);
            await Send("selection-delay");
            Check(Collection().MoveFocus(FocusNavigationDirection.Down),"Directional selection can wait for a delayed current row");
            await presenter.HandleControllerButtonAsync(ControllerButton.LeftTrigger,ControllerEventPhase.Released);
            await Send("selection-release");
            await Wait(()=>Collection().CaptureFocusedItem()?.Index==1&&!Collection().HasPendingNativeNavigation);
            await Selected(0);
            Check(true,"Releasing LT before delayed navigation completes does not select the later row");
            await FocusItem(0);await presenter.HandleControllerButtonAsync(ControllerButton.LeftTrigger);
            presenter.ReconcileHeldSelectionInput(false,false);
            Check(Collection().MoveFocus(FocusNavigationDirection.Down),"Navigation remains available after controller loss");
            await Wait(()=>Collection().CaptureFocusedItem()?.Index==1);await Selected(0);
            Check(true,"Disconnect or re-prime retires held selection without selecting later navigation");
            await presenter.HandleControllerButtonAsync(ControllerButton.LeftTrigger);await Selected(0,1);
            presenter.ReconcileHeldSelectionInput(true,true);
            Check(Collection().MoveFocus(FocusNavigationDirection.Down),"Navigation remains available after a missing release edge");
            await Wait(()=>Collection().CaptureFocusedItem()?.Index==2);await Selected(0,1);
            Check(true,"Physical release reconciliation ends selection even when no release edge was delivered");
            await presenter.HandleControllerButtonAsync(ControllerButton.LeftTrigger);await Selected(0,1,2);
            presenter.ResetPressedStyles();
            Check(Collection().MoveFocus(FocusNavigationDirection.Down),"Navigation resumes after a host input reset");
            await Wait(()=>Collection().CaptureFocusedItem()?.Index==3);await Selected(0,1,2);
            Check(true,"Host foreground/input reset ends the held gesture");
            await presenter.HandleControllerButtonAsync(ControllerButton.LeftTrigger);await Selected(0,1,2,3);
            await Send("selection-grid");await FocusItem(0);await Selected();
            Check(Collection().MoveFocus(FocusNavigationDirection.Down),"New query still supports ordinary navigation");
            await Wait(()=>Collection().CaptureFocusedItem() is {Index:>0});await Selected();
            Check(true,"A query/layout replacement cannot inherit the old held gesture");
            await presenter.HandleControllerButtonAsync(ControllerButton.LeftTrigger,ControllerEventPhase.Released);
            // Run every collection's controller contract before the separate
            // physical mouse/keyboard regression checks.
            foreach(var mode in new[]{"selection-list","selection-grid","selection-grouped"})
            {
                phase=mode+" mouse/keyboard";
                await Send(mode);await FocusItem(0);
                var before = Calls();
                var last = View()!.Items.Count - 1;
                NativeContextProbeInput.PressKey(VirtualKey.End);
                await Wait(() => Collection().CaptureFocusedItem()?.Index == last);
                NativeContextProbeInput.PressKey(VirtualKey.Home);
                await Wait(() => Collection().CaptureFocusedItem()?.Index == 0);
                await Selected();
                Check(Calls() == before, mode + ": End and Home move focus across virtual rows without activating one");
                var rowPressed=0;var rowReleased=0;var rowTapped=0;
                var clickTarget=Item(0);
                clickTarget.AddHandler(PointerPressedEvent,new PointerEventHandler((_,args)=>
                {rowPressed++;trace.Enqueue("selection-pointer-pressed "+args.GetCurrentPoint(null).Position);}),true);
                clickTarget.AddHandler(PointerReleasedEvent,new PointerEventHandler((_,_)=>rowReleased++),true);
                clickTarget.AddHandler(TappedEvent,new TappedEventHandler((_,_)=>rowTapped++),true);
                await NativeContextProbeInput.SelectionClickAsync(clickTarget);
                Check(rowPressed==1&&rowReleased==1&&rowTapped==1,
                    $"{mode}: injected row click reaches one tap (pressed={rowPressed}, released={rowReleased}, tapped={rowTapped})");
                await Selected(0);
                Check(Calls() == before, mode + ": mouse selection does not invoke the row action");
                var checkbox = Descendants(Item(0)).OfType<CheckBox>().Single();
                Check(checkbox.ReadLocalValue(Control.BackgroundProperty) == DependencyProperty.UnsetValue,
                    mode + ": selection hit area does not inherit the row background");
                Check(checkbox.Resources.MergedDictionaries.OfType<WidgetIndexedSelectionResources>().Any() == (mode != "selection-list"),
                    mode + ": on-image indicator resources apply only to thumbnail overlays");
                await ValidateSelectionCheckboxVisualsAsync(checkbox, mode, path, Check, async value =>
                {
                    if (Collection().IsSelected(0) != value)
                        await Collection().ToggleSelectionAsync(((IndexedItem<WidgetIndexedRow>)Collection().NativeView.Items[0]).Value!);
                    await Wait(() => checkbox.IsChecked == value && Collection().IsSelected(0) == value);
                });
                Check(checkbox.IsChecked == true && AutomationProperties.GetItemStatus(Item(0)).Contains("Selected", StringComparison.Ordinal), mode + ": selected state is visible and accessible");
                var pressed = false; var clicked = false;
                checkbox.AddHandler(PointerPressedEvent, new PointerEventHandler((_, _) => pressed = true), true);
                checkbox.Click += (_, _) => clicked = true;
                for (var click = 0; click < 3; ++click)
                {
                    pressed = clicked = false;
                    var checkboxBefore = checkbox.TransformToVisual(null).TransformBounds(new(0,0,checkbox.ActualWidth,checkbox.ActualHeight));
                    var checkboxDuring = checkboxBefore;
                    await NativeContextProbeInput.LeftClickAsync(checkbox, new(checkbox.ActualWidth / 2, checkbox.ActualHeight / 2),
                        () => checkboxDuring = checkbox.TransformToVisual(null).TransformBounds(new(0,0,checkbox.ActualWidth,checkbox.ActualHeight)));
                    Check(pressed && clicked, $"{mode}: physical checkbox click {click + 1} reached control (pressed={pressed}, clicked={clicked})");
                    Check(Math.Abs(checkboxBefore.X - checkboxDuring.X) < .5 && Math.Abs(checkboxBefore.Y - checkboxDuring.Y) < .5,
                        mode + ": checkbox hit target stays in place during press");
                    await Selected(click % 2 == 0 ? [] : [0]);
                }
                Check(true, mode + ": repeated checkbox clicks toggle exactly once each");
                await FocusItem(1);
                await NativeContextProbeInput.SelectionClickAsync(Item(1), control: true); await Selected(1);
                var rangeEnd = mode == "selection-grouped" ? 6 : 3;
                var range = Enumerable.Range(1,rangeEnd).ToArray();
                await FocusItem(rangeEnd);
                await NativeContextProbeInput.SelectionClickAsync(Item(rangeEnd), shift: true); await Selected(range);
                Check(true, mode + ": Ctrl-click and Shift-click preserve the logical range");
                Item(2).Focus(FocusState.Keyboard); NativeContextProbeInput.PressKey(VirtualKey.Space); await Selected(range.Where(index => index != 2).ToArray());
                Check(true, mode + ": keyboard Space toggles one row");
                await presenter.HandleControllerButtonAsync(ControllerButton.A, origin: WidgetRail.WidgetSdk.ControllerInputOrigin.AccessibilityAutomation); await Selected(range);
                Check(presenter.CaptureControllerGuide().Any(h => h.Button == ControllerButton.A && h.Label == "Deselect"), mode + ": controller A toggles and the guide describes selection");
                var anchor = Collection().CaptureFocusedItem()!.Index;
                NativeContextProbeInput.PressKey(VirtualKey.Down, shift: true);
                await Wait(() => Collection().CaptureFocusedItem() is { } moved && moved.Index != anchor);
                var target = Collection().CaptureFocusedItem()!.Index;
                await Selected(Enumerable.Range(Math.Min(anchor,target),Math.Abs(target-anchor)+1).ToArray());
                Check(true, mode + ": Shift-arrow extends the logical selection range");
                NativeContextProbeInput.SelectionAllKey(); await Wait(() => Selection().Count == 100);
                Check(true, mode + ": Ctrl+A selects all matching items, including offscreen rows");
                await Send("selection-clear"); await Selected();

            }
            phase = "large query"; await Send("selection-large"); await FocusItem(0);
            await NativeContextProbeInput.SelectionClickAsync(Item(0)); await Selected(0);
            NativeContextProbeInput.SelectionAllKey(); await Wait(() => Selection().LimitReached);
            Check(Descendants(Collection()).OfType<SelectorItem>().Count() < 256, "Large-query selection retains bounded native realization");
            Check(Selection().Indices.SequenceEqual(new[] { 0 }), "Oversized Select all keeps the prior selection without truncation");
            await Wait(() => Descendants(Collection()).OfType<TextBlock>().Any(t => t.Visibility == Visibility.Visible && t.Text.StartsWith("Select up to 256", StringComparison.Ordinal)));
            Check(true, "Selection limit has a visible accessible explanation");
            await FocusItem(300);
            await NativeContextProbeInput.SelectionClickAsync(Item(300), shift: true); await Wait(() => Selection().LimitReached);
            Check(Selection().Indices.SequenceEqual(new[] { 0 }), "Range selection across unrealized rows rejects an oversized range atomically");
            await Send("selection-end"); await FocusItem(0);
            var calls = Calls(); await NativeContextProbeInput.SelectionClickAsync(Item(0)); await Wait(() => Calls() != calls);
            Check(!Collection().SelectionActive && Descendants(Item(0)).OfType<CheckBox>().All(c => c.Visibility == Visibility.Collapsed), "Leaving selection restores normal activation and hides selection controls");
            phase = "large query lookup"; await Send("locator-start"); await FocusItem(7500);
            var beforeTop = Position(7500);
            Check(FindNode(session!.GetState("indexed-owned")!.LastGood!.Snapshot.Root,"items")!.IndexedOccurrenceKeys is null,
                "Large-query continuity does not publish the complete occurrence order");
            await Send("locator-insert");
            await Wait(() => Collection().CaptureFocusedItem() is { ItemKey: "item.7500", Index: 7501 } && Math.Abs(Position(7501)-beforeTop)<2);
            Check(true,"Lookup preserves deep focused occurrence and pixel position after insertion");
            await Send("locator-shuffle");
            await Wait(() => Collection().CaptureFocusedItem() is { ItemKey: "item.7500", Index: 2499 } && Math.Abs(Position(2499)-beforeTop)<2);
            Check(true,"Lookup follows the same occurrence through reordering beyond 512 items");
            Parent().Focus(FocusState.Keyboard);
            await Send("locator-start"); await Task.Delay(400,lifetime.Token);
            Check(ReferenceEquals(FocusManager.GetFocusedElement(XamlRoot),Parent()),"Unfocused lookup preserves external focus");
            await FocusItem(7500); await Send("locator-delayed"); await Task.Delay(120,lifetime.Token);
            Parent().Focus(FocusState.Keyboard); await Send("locator-release"); await Task.Delay(400,lifetime.Token);
            Check(ReferenceEquals(FocusManager.GetFocusedElement(XamlRoot),Parent()),"Late lookup cannot steal focus after user departure");
            Check(failure is null, "No native presenter failure"); Write(true,null);
        }
        catch (Exception error) { Write(false,phase+": "+error); }

        WidgetIndexedCollectionView Collection() => Descendants(presenter).OfType<WidgetIndexedCollectionView>().Single();
        Control Parent() => Descendants(presenter).OfType<Control>().Single(e => AutomationProperties.GetAutomationId(e) == "Widget.parent");
        Control Item(int index) => (Control)Collection().NativeView.ContainerFromIndex(index);
        IndexedCollectionSelection Selection() => FindNode(session!.GetState("indexed-owned")!.LastGood!.Snapshot.Root,"items")!.IndexedSelection!;
        double Position(int index)
        {
            var scroll = Descendants(Collection().NativeView).OfType<ScrollViewer>().First();
            return Collection().NativeView.ContainerFromIndex(index) is FrameworkElement item
                ? item.TransformToVisual(scroll).TransformPoint(new(0,0)).Y : double.NaN;
        }
        string? Calls() => FindNode(session!.GetState("indexed-owned")!.LastGood!.Snapshot.Root,"calls")?.Text;
        async Task Selected(params int[] indexes)
        {
            await Wait(() => Selection().Indices.SequenceEqual(indexes)); await Task.Delay(120,lifetime.Token);
            Check(Selection().Indices.SequenceEqual(indexes), "Selection remains stable after input release");
        }
        async Task FocusItem(int index)
        {
            var query = FindNode(session!.GetState("indexed-owned")!.LastGood!.Snapshot.Root,"items")!.IndexedCollection!;
            Collection().Enter(new("items",query.SourceId,query.QueryGeneration,"item."+index,index));
            await Wait(() => Collection().CaptureFocusedItem()?.Index == index && Item(index) is { IsLoaded:true,ActualWidth:>0,ActualHeight:>0 } &&
                Collection().NativeView.Items[index] is IndexedItem<WidgetIndexedRow> { Value.Lease.IsCurrent:true });
            await Task.Delay(180,lifetime.Token);
        }
        async Task Send(string action)
        {
            var frame=session!.GetState("indexed-owned")!.LastGood!;
            await session.SendActionAsync(frame.Authority,new(action,"root",InputScopeId:"root"),lifetime.Token);
            await Wait(() => Descendants(presenter).OfType<TextBlock>().Any(t => AutomationProperties.GetAutomationId(t)=="Widget.status"&&t.Text==action));
        }
        async Task Wait(Func<bool> ready)
        {
            var until=Environment.TickCount64+12000;
            while(!ready())
            {
                if(failure is not null)throw new InvalidOperationException(failure);
                if(Environment.TickCount64>until)throw new TimeoutException(phase+" "+presenter.FocusDiagnostics()+" request="+
                    JsonSerializer.Serialize(session!.GetState("indexed-owned")!.LastGood!.Snapshot.FocusGroupEntryRequest));
                await Task.Delay(20,lifetime.Token);
            }
        }
        void Check(bool value,string message)
        {
            if(!value)throw new InvalidOperationException(message);checks.Add(message);
            File.WriteAllText(path+".progress.json",JsonSerializer.Serialize(new{phase,checks}));
        }
        void Write(bool passed,string? error)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
            File.WriteAllText(path,JsonSerializer.Serialize(new{passed,checks,error,trace,physicalInput=true},new JsonSerializerOptions{WriteIndented=true}));
        }
    }
}
