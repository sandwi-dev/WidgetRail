using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using WidgetRail.OverlayFrontend.WinUI.Presentation;
using WidgetRail.PlatformSettings;
using WidgetRail.WidgetPresentationSession;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;
using PresentationSession=WidgetRail.WidgetPresentationSession.WidgetPresentationSession;

namespace WidgetRail.OverlayFrontend.WinUI.Validation;

internal sealed partial class WidgetStylesValidationPage
{
    private async Task ReadingGroupsAsync(string directory,string pipe)
    {
        Directory.CreateDirectory(directory);
        using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(90));
        await using var session=await PresentationSession.ConnectAsync(pipe,cancellationToken:timeout.Token);
        var page=new WidgetViewPresenter{Width=720,Height=460,Session=session};
        var focusTrace=new List<string>();
        page.FocusTrace=value=>{if(focusTrace.Count<1000)focusTrace.Add(value);};
        var layout=new Grid{Width=720,Height=460};layout.Children.Add(page);host.Children.Add(layout);presenter.Visibility=Visibility.Collapsed;
        PresentationLayers.Attach(layout);var theme=new NativePopupTheme();theme.Attach(page);
        page.ApplyAppearance(AppearanceSettings.Default with{Motion=MotionPreference.Reduced},false);
        Exception? failure=null;var retired=false;long revision=0;var intents=0;
        page.Failed=error=>failure=error;
        page.DispatchActionAsync=async request=>
        {
            if(request.IndexedLease is not null){intents++;return;} // Observe the synthetic web intent; never open a browser.
            await session.SendActionAsync(request.Displayed,request.Action,timeout.Token);
        };
        void Changed(object? sender,WidgetPresentationChangedEventArgs args)=>DispatcherQueue.TryEnqueue(()=>
        {
            if(retired || args.State.PublicationRevision<=revision)return;
            revision=args.State.PublicationRevision;
            try
            {
                if(args.State.Failure is {} error)throw new InvalidOperationException(error.Message);
                if(args.State.LastGood is {} frame){theme.Update(frame.RenderStyles,AppearanceSettings.Default);page.Apply(frame);}
            }
            catch(Exception error){failure=error;}
        });
        session.PresentationChanged+=Changed;
        try
        {
            await session.ListWidgetsAsync(timeout.Token);
            await session.EstablishPresentationAsync(session.GetTarget("reading-fixture"),WidgetLifecycleState.Interactive,timeout.Token);
            await Until(()=>Walk(page).OfType<WidgetIndexedCollectionView>().FirstOrDefault()?.NativeView.ContainerFromIndex(0) is Control);
            var list=Walk(page).OfType<WidgetIndexedCollectionView>().Single();
            Check(page.FocusHostTarget("reading.list"),"Explicit page entry reaches the reading collection");
            await Until(()=>list.CaptureFocusedItem()?.ItemKey=="message.0");
            await Until(()=>list.FocusedReadingAction?.ActionId=="open");
            Check(Text("reading.status")=="0 / None","Entering an actionable message focuses its first control without executing it");
            page.MoveFocus(FocusNavigationDirection.Right);await Until(()=>list.FocusedReadingAction?.ActionId=="secondary");
            await Button(ControllerButton.A);await Until(()=>Text("reading.status")=="1 / secondary:0");
            Check(list.FocusedReadingAction?.ActionId=="secondary","Inline activation executes once and preserves child focus through publication");
            page.MoveFocus(FocusNavigationDirection.Down);await Until(()=>list.FocusedReadingAction?.ActionId=="web");
            await Button(ControllerButton.A);await Until(()=>intents==1);
            Check(Text("reading.status")=="1 / secondary:0","Inline hyperlink reaches host intent routing without a preview or worker fallback");
            await Button(ControllerButton.B);await Until(()=>list.FocusedReadingAction is null && list.CaptureFocusedItem()?.ItemKey=="message.0");
            Check(true,"B press and release return to the message instead of leaving the widget");
            for(var i=1;i<=6;i++){page.MoveFocus(FocusNavigationDirection.Down);var key="message."+i;await Until(()=>list.CaptureFocusedItem()?.ItemKey==key);}
            var scroll=Walk(list).OfType<ScrollViewer>().First();var before=scroll.VerticalOffset;
            var beforeY=((FrameworkElement)list.NativeView.ContainerFromIndex(6)).TransformToVisual(scroll).TransformPoint(new()).Y;
            Check(before>0,"Long reading list uses a real scrolled viewport");
            await Until(()=>list.FocusedReadingAction?.ActionId=="open");
            await Button(ControllerButton.A);await Until(()=>FocusedId()=="Widget.reading.modal.content");
            Check(Text("reading.status")=="2 / open:6","Attachment action opens the shared modal with exact row context");
            await Button(ControllerButton.B);await Until(()=>list.CaptureFocusedItem()?.ItemKey=="message.6"&&Find<Control>("Widget.reading.modal.content",layout) is null);
            await WidgetPresentationReadiness.WaitAsync(page,timeout.Token);
            await Until(()=>!list.HasPendingNativeNavigation&&!page.HasPendingMemoryRestore);
            var afterY=((FrameworkElement)list.NativeView.ContainerFromIndex(6)).TransformToVisual(scroll).TransformPoint(new()).Y;
            await File.WriteAllTextAsync(Path.Combine(directory,"return-position.json"),JsonSerializer.Serialize(new
                {beforeOffset=before,afterOffset=scroll.VerticalOffset,beforeY,afterY}));
            Check(Math.Abs(scroll.VerticalOffset-before)<=1&&Math.Abs(afterY-beforeY)<=1,
                "Modal dismissal restores the invoking message and reading position");
            await Until(()=>list.FocusedReadingAction is not null);
            var old=Walk(list).OfType<Button>().First(button=>button.Content is not null&&AutomationProperties.GetAutomationId(button).EndsWith(".secondary",StringComparison.Ordinal));
            var oldCommand=old.Command ?? throw new InvalidOperationException("Inline action has no native command.");
            var oldParameter=old.CommandParameter;
            await Declared("selection","reading.selection");await Until(()=>list.SelectionActive);
            Check(list.FocusedReadingAction is null,"Selection mode returns from inline controls to their reading row");
            page.MoveFocus(FocusNavigationDirection.Right);Check(list.FocusedReadingAction is null,"Selection mode prevents directional entry into inline actions");
            await Button(ControllerButton.A);await Until(()=>Text("reading.selection.status")=="Selected: 1");
            Check(Text("reading.status")=="2 / open:6","A selects the message rather than opening an attachment in selection mode");
            await Declared("selection","reading.selection");await Until(()=>!list.SelectionActive);
            var firstItem=list.CaptureFocusedItem()! with {Index=0,ItemKey="message.0"};
            Check(list.Enter(firstItem),"Pointer test enters a visible current reading row");
            await Until(()=>list.CaptureFocusedItem()?.Index==0&&!list.HasPendingNativeNavigation);
            var row=(FrameworkElement)list.NativeView.ContainerFromIndex(0);
            var secondary=Walk(row).OfType<Button>().Single(button=>AutomationProperties.GetAutomationId(button).EndsWith(".secondary",StringComparison.Ordinal));
            var secondaryText=Walk(secondary).OfType<TextBlock>().Single(text=>AutomationProperties.GetAutomationId(text).EndsWith(".secondary-text",StringComparison.Ordinal));
            var beforeClick=secondary.TransformToVisual(null).TransformBounds(new(0,0,secondary.ActualWidth,secondary.ActualHeight));
            var duringClick=beforeClick;
            await NativeContextProbeInput.LeftClickAsync(secondaryText,new(secondaryText.ActualWidth/2,secondaryText.ActualHeight/2),
                ()=>duringClick=secondary.TransformToVisual(null).TransformBounds(new(0,0,secondary.ActualWidth,secondary.ActualHeight)));
            await Until(()=>Text("reading.status")=="3 / secondary:0");
            Check(Math.Abs(beforeClick.X-duringClick.X)<.5&&Math.Abs(beforeClick.Y-duringClick.Y)<.5,
                "Clicking composed action text keeps the pointer target stable and invokes exactly once");
            NativeContextProbeInput.PressKey(Windows.System.VirtualKey.Space);
            await Until(()=>Text("reading.status")=="4 / secondary:0");
            Check(true,"Keyboard Space activates the focused inline action exactly once");
            await Button(ControllerButton.B);
            NativeContextProbeInput.PressKey(Windows.System.VirtualKey.Enter);
            await Until(()=>list.FocusedReadingAction?.ActionId=="secondary");
            Check(Text("reading.status")=="4 / secondary:0","Keyboard Enter enters the remembered inline action without activating it");
            NativeContextProbeInput.PressKey(Windows.System.VirtualKey.Left);
            await Until(()=>list.FocusedReadingAction?.ActionId=="open");
            Check(true,"Keyboard arrows move between inline actions through the same navigation owner");
            await Declared("refresh","reading.refresh");await Until(()=>Walk(list).OfType<Button>().Any(button=>AutomationProperties.GetAutomationId(button).EndsWith(".secondary",StringComparison.Ordinal)&&!button.IsEnabled));
            oldCommand.Execute(oldParameter);await Task.Delay(120);
            Check(Text("reading.status")=="4 / secondary:0","A retired native child command cannot dispatch through a replaced row lease");
            Check(!page.IsFocusPreviewOpen,"Inline controls never create a focus preview");
        }
        catch(Exception error)
        {
            failure=error;
            await File.WriteAllTextAsync(Path.Combine(directory,"focus-state.json"),page.FocusDiagnostics());
        }
        finally{retired=true;session.PresentationChanged-=Changed;await page.DisposeAsync();host.Children.Remove(layout);}
        await File.WriteAllLinesAsync(Path.Combine(directory,"focus-trace.jsonl"),focusTrace);
        await File.WriteAllTextAsync(Path.Combine(directory,"result.json"),JsonSerializer.Serialize(new{passed=failure is null,checks,intents,error=failure?.ToString(),scope="Native indexed presenter, real bridge/worker, synthetic data and intercepted web intent; no Discord or network."}));
        if(failure is not null)throw failure;
        string? Text(string id)=>Find<TextBlock>("Widget."+id,layout)?.Text;
        string? FocusedId()=>FocusManager.GetFocusedElement(page.XamlRoot) is DependencyObject value?AutomationProperties.GetAutomationId(value):null;
        async Task Until(Func<bool> predicate,
            [System.Runtime.CompilerServices.CallerArgumentExpression(nameof(predicate))] string? condition=null)
        {
            try { await Wait(()=>failure is not null||predicate(),condition); }
            catch(TimeoutException error)
            {
                var diagnostic=new{condition,focused=FocusedId(),
                    navigation=Walk(page).OfType<WidgetIndexedCollectionView>().FirstOrDefault()?.NavigationDiagnosticState()};
                throw new TimeoutException(error.Message+"; "+JsonSerializer.Serialize(diagnostic),error);
            }
            if(failure is not null)throw failure;
        }
        async Task Button(ControllerButton button){await page.HandleControllerButtonAsync(button);await page.HandleControllerButtonAsync(button,ControllerEventPhase.Released);}
        async Task Declared(string action,string id){var frame=page.CurrentBinding!.Frame;await session.SendActionAsync(frame,new(action,id,InputScopeId:frame.Snapshot.ActiveInputScopeId),timeout.Token);}
        static IEnumerable<DependencyObject> Walk(DependencyObject root){yield return root;for(var i=0;i<VisualTreeHelper.GetChildrenCount(root);i++)foreach(var child in Walk(VisualTreeHelper.GetChild(root,i)))yield return child;}
    }
}
