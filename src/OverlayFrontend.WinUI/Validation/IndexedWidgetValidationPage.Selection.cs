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
                var before = Calls();
                await NativeContextProbeInput.SelectionClickAsync(Item(0)); await Selected(0);
                Check(Calls() == before, mode + ": mouse selection does not invoke the row action");
                var checkbox = Descendants(Item(0)).OfType<CheckBox>().Single();
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
                if(Environment.TickCount64>until)throw new TimeoutException(phase+" "+presenter.FocusDiagnostics());
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
            File.WriteAllText(path,JsonSerializer.Serialize(new{passed,checks,error,physicalInput=true},new JsonSerializerOptions{WriteIndented=true}));
        }
    }
}
