using System.Text.Json;
using System.Runtime.InteropServices.WindowsRuntime;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using WidgetRail.OverlayFrontend.WinUI.Presentation;
using WidgetRail.OverlayFrontend.WinUI.Collections;
using WidgetRail.OverlayFrontend.WinUI.Validation;
using WidgetRail.OverlayPlatformClient;
using WidgetRail.WidgetProtocol;
using Windows.Foundation;
using Windows.System;

namespace WidgetRail.OverlayFrontend.WinUI.Shell;

internal sealed partial class OverlayShellPage
{
    internal void EnableDiscordMessagesValidation(string path,bool composerOnly=false)
    {
        var started = false;
        Loaded += async (_, _) =>
        {
            if (started) return; started = true;
            var checks = new List<string>(); var trace = new List<object>(); string? failure = null; var step = "startup";
            var root = Path.GetDirectoryName(Path.GetFullPath(path))!;
            var directionalEntryOnly=Environment.GetCommandLineArgs().Contains("--discord-messages-directional-entry-only");
            var largeConversations=Environment.GetCommandLineArgs().Contains("--discord-large-conversations");
            var privateCalls=Environment.GetCommandLineArgs().Contains("--discord-private-calls");
            try
            {
                if (!Path.GetFullPath(options.SettingsRoot).StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("Messages validation requires its own isolated fixture profile.");
                if (startup is not null) await startup;
                if (owner is null || !catalogItems.Any(widget => widget.Id == "widgetrail.tests.discord-ui") ||
                    catalogItems.Any(widget => widget.Id.StartsWith("widgetrail.prototype.discord", StringComparison.Ordinal)))
                    throw new InvalidOperationException("Messages validation accepts only the synthetic Discord fixture.");
                await SelectAsync("widgetrail.tests.discord-ui", true);
                await Until(() => MainFocusEnabled && Node("discord.messages.open") is not null);
                Check(await IndexedSourceWindowScenarios.RunAsync(DispatcherQueue) >= 12,
                    "Shared window reconciliation preserves identity, delayed-result authority, retention and bounded teardown");
                Check(await IndexedSourceRetentionScenarios.RunAsync(DispatcherQueue) > 0 &&
                    await IndexedSourceReentrancyScenarios.RunAsync(DispatcherQueue) > 0 &&
                    await IndexedSourcePublicationScenarios.RunAsync(DispatcherQueue) > 0,
                    "Existing native retention, reentrant lifetime and batched publication scenarios remain valid");
                App.Window!.Activate(); await Until(OwnsForeground);
                surface!.FocusTrace = message => { if (trace.Count < 500) trace.Add(new { step, focus = message }); };
                using var expectedSurface = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "expected-surface.json")));
                var expectedWidth = expectedSurface.RootElement.GetProperty("width").GetDouble();
                var expectedHeight = expectedSurface.RootElement.GetProperty("height").GetDouble();
                Check(Math.Abs(surface.ActualWidth - expectedWidth) < 1 && Math.Abs(surface.ActualHeight - expectedHeight) < 1,
                    $"Messages fixture uses the requested {expectedWidth}x{expectedHeight} DIP surface (actual {surface.ActualWidth}x{surface.ActualHeight})");
                if(Environment.GetCommandLineArgs().Contains("--discord-media-messages"))await MediaMessagesAsync();
                else if(Environment.GetCommandLineArgs().Contains("--discord-sent-reveal-only"))await SentMessageRevealAsync();
                else if(Environment.GetCommandLineArgs().Contains("--discord-pinned-read-only"))await HiddenChatReadAsync();
                else if(Environment.GetCommandLineArgs().Contains("--discord-history-lifecycle-only"))await HistoryLifecycleAsync();
                else if(Environment.GetCommandLineArgs().Contains("--discord-history-held-scroll-only"))await HeldHistoryScrollAsync();
                else if(Environment.GetCommandLineArgs().Contains("--discord-history-continuity-only"))await HistoryContinuityAsync();
                else if(privateCalls)await PrivateCallsAsync();
                else
                {
                Check(Audit().GetProperty("history").GetArrayLength() == 0, "Call page does not eagerly load message history");
                Check(Audit().GetProperty("readAcks").GetArrayLength()==0,"Call presentation does not acknowledge conversations");

                step="call actions to participant pin";
                foreach(var action in new[]{"discord.main.mute","discord.main.deafen","discord.share.start","discord.main.leave"})
                {
                    Check(surface.FocusHostTarget(action),"Call action is focusable: "+action);
                    await Move(NavigationDirection.Down);
                    await Until(()=>FocusId()=="Widget.discord.people.pin");
                    Check(true,"Down from "+action+" reaches Pin voice participants without invoking either action");
                }

                step = "controller inbox";
                Check(surface.FocusHostTarget("discord.call.open"),"Call tab is a native header focus target");
                await Move(NavigationDirection.Right);await Until(()=>FocusId()=="Widget.discord.messages.open");
                await Move(NavigationDirection.Right);await Until(()=>FocusId()=="Widget.discord.refresh");
                await Move(NavigationDirection.Right);await Until(()=>FocusId()=="Widget.discord.account.open");
                Check(true,"Right traverses tabs and reaches Refresh and Open Discord");
                await Move(NavigationDirection.Left);await Until(()=>FocusId()=="Widget.discord.refresh");
                await Move(NavigationDirection.Left);await Until(()=>FocusId()=="Widget.discord.messages.open");
                Check(true,"Left returns through toolbar actions to Messages without trapping focus");
                Check(surface.FocusHostTarget("discord.messages.open"), "Native Messages entry is focusable");
                await Button(ControllerButton.A);
                await Until(() => Node("discord.messages.list")?.IndexedCollection?.Count==(largeConversations?120:2));
                Check(FocusId()=="Widget.discord.messages.open","Changing a persistent tab preserves its native focus");
                for(var attempt=0;attempt<8&&FocusId()!="Widget.discord.messages.search";++attempt)await Move(NavigationDirection.Down);
                Check(FocusId()=="Widget.discord.messages.search","D-pad enters inbox search from the selected Messages tab");
                Check(surface.CurrentBinding!.Frame.Authority.ActiveInputScopeId == "discord.root", "Inbox shares the root scope with the persistent Call/Messages destinations");
                Check(Audit().GetProperty("history").GetArrayLength() == 0, "Inbox reads conversations without eagerly fetching their histories");
                Check(Find("discord.call.dock.mute") is {} dockMute && InSurface(dockMute) &&
                    Find("discord.call.dock.deafen") is {} dockDeafen && InSurface(dockDeafen),
                    "Messages keeps the current call controls visible and inside the widget");
                for (var attempt = 0; attempt < 8 && FocusId() != "Widget.discord.messages.list.Item.0"; ++attempt) await Move(NavigationDirection.Down);
                Check(FocusId() == "Widget.discord.messages.list.Item.0", "D-pad reaches an existing conversation from inbox search");
                step="conversation list upward navigation";
                await Move(NavigationDirection.Down);
                await Until(()=>FocusId()=="Widget.discord.messages.list.Item.1");
                await Move(NavigationDirection.Up);
                await Until(()=>FocusId()=="Widget.discord.messages.list.Item.0");
                Check(true,"Up from the second conversation stays in the list and reaches the previous row");
                var inbox=Walk(surface).OfType<WidgetIndexedCollectionView>().Single(view=>AutomationProperties.GetAutomationId(view)=="Widget.discord.messages.list");
                await Until(()=>!inbox.HasPendingNativeNavigation);
                var inboxScroll=Walk(inbox.NativeView).OfType<ScrollViewer>().First();
                if(inboxScroll.VerticalOffset>.5)
                {
                    // A compact viewport can fully reveal row zero while still
                    // having leading padding above it. Existing host policy
                    // reveals that remaining content before taking an exit.
                    await Move(NavigationDirection.Up);
                    await Until(()=>inboxScroll.VerticalOffset<=.5);
                    Check(FocusId()=="Widget.discord.messages.list.Item.0","Up reveals remaining leading padding before leaving the collection");
                }
                await Move(NavigationDirection.Up);
                await Until(()=>FocusId()=="Widget.discord.messages.search");
                Check(true,"Up exits to Search only at the first conversation");
                await Move(NavigationDirection.Down);
                await Until(()=>FocusId()=="Widget.discord.messages.list.Item.0");
                Check(true,"Down from Search reenters the conversation list after an upward boundary exit");
                await Move(NavigationDirection.Down);
                await Until(()=>FocusId()=="Widget.discord.messages.list.Item.1");
                NativeContextProbeInput.PressKey(VirtualKey.Up);
                await Until(()=>FocusId()=="Widget.discord.messages.list.Item.0");
                Check(true,"Keyboard Up also moves to the preceding conversation instead of Search");
                if(largeConversations)await LargeConversationsAsync();
                else
                {
                await Button(ControllerButton.A);
                await Until(() => Node("discord.message.history")?.IndexedCollection?.Count == 20 && FocusId() == "Widget.discord.message.draft");
                Check(surface.CurrentBinding!.Frame.Authority.ActiveInputScopeId == "discord.conversation.scope", "Opening a conversation establishes the matching native input scope");
                var audit = Audit();
                Check(audit.GetProperty("history").GetArrayLength() == 1 && audit.GetProperty("history")[0].GetProperty("limit").GetInt32() == 20,
                    "Conversation history loads one bounded 20-message page lazily");
                Check(InSurface(Find("discord.message.draft")!) && InSurface(Find("discord.message.send")!), "Composer and Send are within the actual widget viewport");
                Check(Node("discord.message.send")!.IsDisabled == true, "An empty composer cannot send");
                await Until(()=>Audit().GetProperty("readAcks").EnumerateArray().Any(ack=>ack.GetProperty("channel").GetString()=="10"&&ack.GetProperty("message").GetString()=="1120"));
                Check(true,"Actual visible native history drives one scoped opening read acknowledgment");
                await Until(()=>Node("discord.messages.open")?.Text=="Messages");
                Check(true,"Provider read state clears the primary Messages unread indicator");
                Check((Find("discord.messages.page") is {} sidebar && Displayed(sidebar))==(expectedWidth>800),
                    "Conversation list is a sidebar at expanded size and hidden in the compact conversation");
                await SaveImage(surface,"conversation-initial.png");

                if(Environment.GetCommandLineArgs().Contains("--discord-messages-section-return-only"))
                    await SectionReturnAsync(expectedWidth);
                else if(directionalEntryOnly)
                    await DirectionalHistoryEntryAsync();
                else
                {

                if(!composerOnly)
                {
                step = "inline rich messages and attachment preview";
                var history = Walk(surface).OfType<WidgetIndexedCollectionView>().SingleOrDefault(view => AutomationProperties.GetAutomationId(view) == "Widget.discord.message.history")
                    ?? throw new InvalidOperationException("Native indexed history is missing.");
                var historyScroll = Walk(history).OfType<ScrollViewer>().First();
                var boundaryEvents=0;
                history.NavigationTrace=value=>{if((value.StartsWith("boundary ",StringComparison.Ordinal)||value.StartsWith("realization-retarget",StringComparison.Ordinal)||value.StartsWith("focused-target",StringComparison.Ordinal))&&boundaryEvents++<100)trace.Add(new{step,collectionNavigation=value});};
                var historyExtent = WidgetScrollViewport.ContentBounds(historyScroll).Height;
                trace.Add(new { step, phase = "conversation layout", surface.ActualWidth, surface.ActualHeight, historyExtent,source=Node("discord.message.history")?.IndexedCollection });
                Check(historyExtent >= 116, $"History has room for at least one estimated message row (content viewport {historyExtent} DIPs)");
                var fixedControls = new[] { "discord.messages.back", "discord.message.draft", "discord.message.attach", "discord.message.send" }
                    .Select(Find).Where(element => element is not null && Displayed(element)).Cast<FrameworkElement>().ToArray();
                Check(fixedControls.Length == 4 && fixedControls.All(InSurface), "All displayed non-scrolling conversation controls stay inside the widget client");
                Check(Node("discord.message.options") is null,"Redundant More dropdown is absent");
                var initialHistory = Node("discord.message.history")!.IndexedCollection!;
                var latestTarget = new IndexedCollectionFocusTarget("discord.message.history", initialHistory.SourceId, initialHistory.QueryGeneration, "1120", initialHistory.Count - 1);
                await Until(() => history.NativeView.ContainerFromIndex(latestTarget.Index) is FrameworkElement last &&
                    WidgetReadingScrollFocus.IsVisible(last, historyScroll));
                Check(FocusId() == "Widget.discord.message.draft", "Cold history reveals the newest message without taking composer focus");
                var plainTarget=CurrentMessageTarget("1119");
                Check(history.Enter(plainTarget),"Controller enters a plain-text message occurrence");
                await Until(()=>history.CaptureFocusedItem()?.ItemKey=="1119");
                Check(!surface.IsFocusPreviewOpen,"Ordinary long text does not open a redundant rich preview");
                var plainIndex=history.CaptureFocusedItem()!.Index;
                var plainContainer=(Control)history.NativeView.ContainerFromIndex(plainIndex);
                var plainPresenter=Walk(plainContainer).OfType<WidgetViewPresenter>().First();
                var plainData=(WidgetRail.OverlayFrontend.WinUI.Collections.IndexedItem<WidgetIndexedRow>)history.NativeView.Items[plainIndex];
                var bubble=plainPresenter.FocusVisualTarget(plainData.Value!.Item.Root);
                await Until(()=>NativeComputedStyleAdapter.For(plainContainer)?.FocusDecoration is {IsVisible:true});
                var outline=NativeComputedStyleAdapter.For(plainContainer)!.FocusDecoration!;
                Check(bubble is not null && bubble.ActualWidth<plainContainer.ActualWidth &&
                    Math.Abs(outline.DecorationVisual.Size.X-bubble.ActualWidth)<=16,
                    "Controller outline follows the message bubble rather than row/date separator space");
                var rowPaint=NativeComputedStyleAdapter.For(plainContainer)!;
                Check(rowPaint.InspectionStyle?.GetValueOrDefault("surface-shading")?.Number==0 &&
                    rowPaint.InspectionStyle?.GetValueOrDefault("border-width")?.Number==0 &&
                    rowPaint.Depth?.NativeShadow?.Opacity is null or 0,
                    "Transparent focused message row has no inherited full-width shadow, shading or border");
                var bubbleBounds=bubble!.TransformToVisual(plainContainer).TransformBounds(new(0,0,bubble.ActualWidth,bubble.ActualHeight));
                Check(bubbleBounds.Left>=-.5 && bubbleBounds.Right<=plainContainer.ActualWidth+.5,
                    "Long message wrapping stays inside the actual available row width");
                var rich=Walk(history).OfType<RichTextBlock>().SelectMany(block=>block.Blocks.OfType<Paragraph>().SelectMany(paragraph=>paragraph.Inlines.OfType<Run>())).ToArray();
                Check(rich.Any(run=>run.Text.Contains("Line 26:",StringComparison.Ordinal)),"The last line of a long message is retained inline");
                Check(rich.Any(run=>run.Text=="Italic"&&run.FontStyle==Windows.UI.Text.FontStyle.Italic)&&
                    rich.Any(run=>run.Text=="Strike"&&(run.TextDecorations&Windows.UI.Text.TextDecorations.Strikethrough)!=0),
                    "Native rich text preserves italic and strike styling");
                var compactTarget=CurrentMessageTarget("1111");
                Check(history.Enter(compactTarget),"Controller enters a short message through the same collection navigation");
                await Until(()=>history.CaptureFocusedItem() is {ItemKey:"1111"} focused && !history.HasPendingNativeNavigation &&
                    history.NativeView.ContainerFromIndex(focused.Index) is FrameworkElement {IsLoaded:true} shortRow && WidgetReadingScrollFocus.IsVisible(shortRow,historyScroll));
                Check(((FrameworkElement)history.NativeView.ContainerFromIndex(history.CaptureFocusedItem()!.Index)).ActualHeight<72,
                    "A short message is content-sized below the former 72-DIP tile minimum");
                await WidgetPresentationReadiness.WaitAsync(surface,lifetime.Token);
                await SaveImage(surface,"conversation.png");
                Check(history.Enter(CurrentMessageTarget("1120")), "Native history group admits controller entry at the retained newest message");
                await Until(() => history.CaptureFocusedItem()?.ItemKey=="1120"&&!history.HasPendingNativeNavigation);
                Check(!surface.IsFocusPreviewOpen,"Attachment metadata stays in the timeline without a focus popup");
                var rowFocus=FocusId();
                await Button(ControllerButton.A);
                Check(!surface.IsFocusPreviewOpen&&FocusId()==rowFocus,"Unavailable text files do not offer a misleading media preview");

                step = "bounded paging";
                Check(!surface.CaptureControllerGuide().Any(hint=>hint.Button==ControllerButton.LeftStick),
                    "Conversation without available call support does not advertise a call-focus shortcut");
                await Button(ControllerButton.X);
                await Until(()=>FocusId()=="Widget.discord.message.draft");
                Check(surface.ControllerTextEntry is null,"X focuses composer without sending or implicitly opening keyboard");
                var priorReads=Audit().GetProperty("history").GetArrayLength();
                await CheckOlderPageAsync(history,historyScroll,priorReads);
                await ScrollBoundary(-1,priorReads+1);
                await Until(() => Audit().GetProperty("history").GetArrayLength() >= priorReads+2 && Node("discord.message.history")?.CollectionBoundaries?.After==true);
                Check(Node("discord.message.history")!.IndexedCollection!.Count <= 40, "Older navigation keeps the native history window bounded to 40 messages");
                var reverseQuery=Node("discord.message.history")!.IndexedCollection!.QueryGeneration;
                await ScrollBoundary(1,Audit().GetProperty("history").GetArrayLength());
                await Until(() => Audit().GetProperty("history").EnumerateArray().Any(item => item.GetProperty("after").ValueKind == JsonValueKind.String) &&
                    Node("discord.message.history")!.IndexedCollection!.QueryGeneration>reverseQuery);
                Check(Node("discord.message.history")!.IndexedCollection!.Count <= 40, "Newer navigation uses the reverse provider cursor without unbounding the window");
                for(var adjacent=0;adjacent<4 && Node("discord.message.history")?.CollectionBoundaries?.Before==true;adjacent++)
                {
                    var reads=Audit().GetProperty("history").GetArrayLength();
                    var generation=Node("discord.message.history")!.IndexedCollection!.QueryGeneration;
                    await ScrollBoundary(-1,reads);
                    await Until(()=>Audit().GetProperty("history").GetArrayLength()>reads && Node("discord.message.boundary.status") is null &&
                        Node("discord.message.history")!.IndexedCollection!.QueryGeneration>generation);
                }
                var oldest=Node("discord.message.history")!;
                Check(Node("discord.message.retry") is null && oldest.IndexedOccurrenceKeys?.FirstOrDefault()=="1001",
                    "Exhaustion reaches the actual oldest provider item without a resource error");
                Check(oldest.CollectionBoundaries?.Before==false,"Exhausted older history withdraws the loadable edge");
                Check(history.Enter(new("discord.message.history",oldest.IndexedCollection!.SourceId,oldest.IndexedCollection.QueryGeneration,"1001",0)),
                    "Oldest message remains a native focus target");
                await Until(()=>history.CaptureFocusedItem()?.ItemKey=="1001" && !history.HasPendingNativeNavigation);
                // A long oldest row can enter at its lower visible slice. Up
                // must reveal that content before exiting at the actual edge.
                for(var up=0;up<32&&FocusId().StartsWith("Widget.discord.message.history.Item.",StringComparison.Ordinal);up++)
                    await Move(NavigationDirection.Up);
                await Until(()=>!FocusId().StartsWith("Widget.discord.message.history.Item.",StringComparison.Ordinal));
                Check(!FocusId().StartsWith("Widget.discord.message.history.Item.",StringComparison.Ordinal),"An exhausted boundary does not trap controller navigation in history");
                await Button(ControllerButton.Y);
                await Until(()=>Node("discord.message.history")?.IndexedOccurrenceKeys?.LastOrDefault()=="1120" &&
                    FocusId()=="Widget.discord.message.draft");
                Check(true,"Y returns to current latest history and composer focus from an old window");
                }

                step = "native keyboard composer";
                var composer = Find("discord.message.draft")!;
                await NativeContextProbeInput.LeftClickAsync(composer, new Point(composer.ActualWidth / 2, composer.ActualHeight / 2));
                await Until(() => surface.ControllerTextEntry is { IsLoaded: true } && FocusId().StartsWith("Widget.TextEntry.Key.",StringComparison.Ordinal));
                var editor = Walk(surface.ControllerTextEntry!).OfType<TextBox>().Single();
                await Until(()=>Displayed(editor));
                var editorPresses=0;
                PointerEventHandler editorPressed=(_,args)=>{editorPresses++;trace.Add(new{step,phase="editor pressed",point=args.GetCurrentPoint(editor).Position,focus=FocusId()});};
                RoutedEventHandler editorFocus=(_,_)=>trace.Add(new{step,phase="editor focus event",focus=FocusId()});
                editor.AddHandler(UIElement.PointerPressedEvent,editorPressed,true);
                editor.GotFocus+=editorFocus;editor.LostFocus+=editorFocus;
                var beforeClick=editor.TransformToVisual(null).TransformBounds(new(0,0,editor.ActualWidth,editor.ActualHeight));
                trace.Add(new{step,phase="before editor click",beforeClick,rootBounds=editor.TransformToVisual(XamlRoot.Content).TransformBounds(new(0,0,editor.ActualWidth,editor.ActualHeight)),
                    popups=VisualTreeHelper.GetOpenPopupsForXamlRoot(XamlRoot).Select(popup=>new{popup.HorizontalOffset,popup.VerticalOffset}).ToArray(),
                    editor.ActualWidth,editor.ActualHeight,editor.IsReadOnly,ownsMainForeground=OwnsForeground(),focus=FocusId()});
                await NativeContextProbeInput.LeftClickAsync(editor, new Point(editor.ActualWidth / 2, editor.ActualHeight / 2));
                trace.Add(new{step,phase="after editor click",editorPresses,focus=FocusId(),bounds=editor.TransformToVisual(null).TransformBounds(new(0,0,editor.ActualWidth,editor.ActualHeight))});
                await SaveImage(surface.ControllerTextEntry!,"keyboard.png");
                Check(editorPresses>0,"Actual pointer press reaches the laid-out native editor");
                await Until(() => ReferenceEquals(FocusManager.GetFocusedElement(XamlRoot), editor));
                editor.RemoveHandler(UIElement.PointerPressedEvent,editorPressed);editor.GotFocus-=editorFocus;editor.LostFocus-=editorFocus;
                NativeContextProbeInput.PressKey(VirtualKey.H);
                await Until(() => string.Equals(editor.Text, "h", StringComparison.OrdinalIgnoreCase));
                NativeContextProbeInput.PressKey(VirtualKey.I);
                await Until(() => string.Equals(editor.Text, "hi", StringComparison.OrdinalIgnoreCase));
                trace.Add(new { step, phase = "native editor typed", editor.Text, focus = FocusId() });
                NativeContextProbeInput.PressKey(VirtualKey.Enter);
                await Until(() => surface.ControllerTextEntry is null && Node("discord.message.draft")?.TextEntryValue?.ToLowerInvariant() == "hi");
                Check(Node("discord.message.send") is { IsDisabled: not true } && InSurface(Find("discord.message.send")!), "Native mouse and keyboard edit the draft without sending it");
                Check(Audit().GetProperty("mutationAttempts").GetInt32() == 0, "Inbox, previews, paging and composition produce no send or upload");
                step="Call and Messages section return";
                var readsBeforeSection=Audit().GetProperty("history").GetArrayLength();
                await Button(ControllerButton.LeftBumper);
                await Until(()=>Node("discord.call.page") is not null);
                Check(Node("discord.sections") is not null,"Call retains the persistent section switcher");
                await Button(ControllerButton.RightBumper);
                await Until(()=>Node("discord.message.draft")?.TextEntryValue?.ToLowerInvariant()=="hi" && Find("discord.message.draft") is {IsLoaded:true});
                Check(Audit().GetProperty("history").GetArrayLength()==readsBeforeSection,
                    "Bumper section navigation restores the retained conversation and draft without a history refetch");
                step = "synthetic upload cancellation";
                Check(surface.FocusHostTarget("discord.message.attach"),"Attach files is controller reachable");
                await Until(()=>FocusId()=="Widget.discord.message.attach");await Button(ControllerButton.A);
                await Until(()=>Node("discord.message.file.0") is not null && !SystemFilePickerOpen);
                Check(Audit().GetProperty("uploads").GetInt32()==0,"Selecting a synthetic service attachment does not upload or send it");
                Check(surface.FocusHostTarget("discord.message.send"),"Explicit Send remains controller reachable with an attachment");
                await Until(()=>FocusId()=="Widget.discord.message.send");await Button(ControllerButton.A);
                await Until(()=>Node("discord.message.status")?.Text=="Uploading files… 50%" && Find("discord.message.cancel") is {IsLoaded:true});
                await Until(()=>InSurface(Find("discord.message.cancel")!) && InSurface(Find("discord.message.draft")!) && InSurface(Find("discord.message.send")!));
                Check(InSurface(Find("discord.message.cancel")!) && InSurface(Find("discord.message.draft")!) && InSurface(Find("discord.message.send")!),
                    "Upload progress and Cancel fit the native composer client at this size");
                Check(Node("discord.message.cancel")?.Text=="Cancel upload" && Node("discord.message.cancel")?.IsDisabled!=true,
                    "The native busy composer exposes an enabled Cancel upload action");
                Check(surface.FocusHostTarget("discord.message.cancel"),"Controller can reach Cancel upload");
                await Until(()=>FocusId()=="Widget.discord.message.cancel");await Button(ControllerButton.A);
                await Until(()=>Audit().GetProperty("uploadCancellations").GetInt32()==1 &&
                    Node("discord.message.cancel") is null && Node("discord.message.attach") is not null);
                Check(Node("discord.message.status") is null,"Completed upload cancellation leaves no permanent status text");
                Check(Node("discord.message.file.0") is not null && Node("discord.message.draft")?.TextEntryValue?.ToLowerInvariant()=="hi",
                    "Native upload cancellation keeps the selected file and composed draft");
                Check(Audit().GetProperty("uploads").GetInt32()==1 && Audit().GetProperty("uploadCancellations").GetInt32()==1 && Audit().GetProperty("mutationAttempts").GetInt32()==0,
                    "The synthetic stream cancels exactly once with no provider send or real network upload");
                step = "conversation return";
                await Button(ControllerButton.B);
                await Until(() => Node("discord.messages.page") is not null && FocusId() == "Widget.discord.messages.list.Item.0");
                Check(true, "Conversation Back restores its invoking inbox row");
                step = "dashboard presentation";
                await Button(ControllerButton.B);
                await Until(() => !interactive && !switching && !trayGuide.HasPendingHints && FocusedTrayWidget()?.Id == "widgetrail.tests.discord-ui");
                Check(RadialOpen, "Back from the Messages root opens the host radial chooser directly");
                await Until(() => trayGuide.PublishedHintsForValidation.Any(hint => hint.Button == ControllerButton.X));
                Check(new[] { ControllerButton.X, ControllerButton.LeftBumper, ControllerButton.RightBumper }
                    .All(button => trayGuide.PublishedHintsForValidation.Any(hint => hint.Button == button)),
                    "Radial chooser advertises the same admitted dashboard actions it routes");
                // Both chooser layouts route through the same displayed-frame
                // dashboard authority; neither requires entering widget content.
                FocusDirectionalTray();
                await Until(() => !interactive && !RadialOpen && !switching && !trayGuide.HasPendingHints &&
                    trayGuide.PublishedHintsForValidation.Any(hint => hint.Button == ControllerButton.X));
                var guide = trayGuide.PublishedHintsForValidation.ToArray();
                trace.Add(new { step, interactive, width = trayGuide.ActualWidth, hints = guide.Select(hint => new { hint.Button, hint.Label }).ToArray(),
                    displayed = trayGuide.DisplayedHints.Select(hint => new { hint.Button, hint.Label }).ToArray() });
                Check(guide.Any(hint => hint.Button == ControllerButton.X && hint.Label is "Mute" or "Unmute"), "Passive dashboard publishes Discord mute/unmute on X");
                Check(guide.Any(hint => hint.Button == ControllerButton.LeftBumper && hint.Label is "Deafen" or "Undeafen") &&
                    guide.Any(hint => hint.Button == ControllerButton.RightBumper && hint.Label == "Leave call"),
                    "Passive dashboard publishes Deafen on LB and Leave call on RB");
                if (expectedWidth > 800) Check(new[] { ControllerButton.X, ControllerButton.LeftBumper, ControllerButton.RightBumper }
                    .All(button => trayGuide.DisplayedHints.Any(hint => hint.Button == button)), "Expanded dashboard has room to display all Discord quick actions");
                Check(!interactive && Audit().GetProperty("mutationAttempts").GetInt32() == 0,
                    "Dashboard presentation remains passive and never sends or uploads a message");
                }
                }
                }
            }
            catch (Exception error)
            {
                if(surface is not null)
                {
                    trace.Add(new{step,phase="collection geometry at failure",collections=Walk(surface).OfType<WidgetIndexedCollectionView>().Select(view=>new{
                        id=AutomationProperties.GetAutomationId(view),navigation=view.NavigationDiagnosticState(),scroll=Walk(view).OfType<ScrollViewer>().Select(scroll=>new{scroll.VerticalOffset,scroll.ExtentHeight,scroll.ViewportHeight}).ToArray(),
                        realized=Enumerable.Range(0,(int)Math.Min(40,Node("discord.message.history")?.IndexedCollection?.Count??0)).Select(index=>new{index,element=view.NativeView.ContainerFromIndex(index) as FrameworkElement})
                            .Where(item=>item.element is not null).Select(item=>new{item.index,item.element!.ActualWidth,item.element.ActualHeight,
                                bounds=item.element.TransformToVisual(surface).TransformBounds(new(0,0,item.element.ActualWidth,item.element.ActualHeight))}).ToArray()}).ToArray()});
                    try{await SaveImage(surface,"failure.png");}catch(Exception){ }
                }
                trace.Add(new { step, phase = "failure state", focus = FocusId(), draft = Node("discord.message.draft")?.TextEntryValue,
                    editor = surface?.ControllerTextEntry is { } keyboard ? Walk(keyboard).OfType<TextBox>().FirstOrDefault()?.Text : null,
                    previewTexts = surface?.FocusPreviewChrome is { } chrome ? Walk(chrome).OfType<TextBlock>().Select(text => new { length = text.Text.Length, head = text.Text[..Math.Min(80, text.Text.Length)] }).ToArray() : null,
                    preview = surface?.FocusPreviewDiagnostics(), guideWidth = trayGuide.ActualWidth,
                    notification = new { now=Environment.TickCount64, notificationBannerState.EntryId, notificationBannerState.ExpiresAt,
                        bannerVisible=notificationBanner?.IsVisible, bannerTitle=notificationCard?.TitleText.Text,
                        entries=notificationCenter?.Inbox.Entries.Select(entry=>new{entry.Widget,entry.Title,entry.Available,entry.WasShown}).ToArray() },
                    guidePublished = trayGuide.PublishedHintsForValidation.Select(hint => new { hint.Button, hint.Label }).ToArray(),
                    guideDisplayed = trayGuide.DisplayedHints.Select(hint => new { hint.Button, hint.Label }).ToArray() });
                failure = error.ToString();
            }
            finally { if (surface is not null) surface.FocusTrace = null; }
            Directory.CreateDirectory(root);
            await File.WriteAllTextAsync(path, JsonSerializer.Serialize(new { passed = failure is null, composerOnly, checks, step, error = failure, trace, native = surface?.FocusDiagnostics() }));

            JsonElement Audit() { using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "messages-provider.json"))); return document.RootElement.Clone(); }
            void Check(bool condition, string label)
            {
                if (!condition) throw new InvalidOperationException(label);
                checks.Add(label);
                if(Environment.GetCommandLineArgs().Contains("--discord-media-messages"))
                    File.WriteAllText(path+".progress.json",JsonSerializer.Serialize(new{step,checks,focus=FocusId()}));
            }
            bool OwnsForeground() => App.Window is { } window && DiscordValidationForegroundWindow() == WinRT.Interop.WindowNative.GetWindowHandle(window);
            string FocusId() => FocusManager.GetFocusedElement(XamlRoot) is DependencyObject focus ? AutomationProperties.GetAutomationId(focus) : "";
            ViewNode? Node(string id) => surface?.CurrentBinding is { } binding ? Nodes(binding.View.Root).FirstOrDefault(node => node.Id == id) : null;
            IndexedCollectionFocusTarget CurrentMessageTarget(string key)
            {
                var node=Node("discord.message.history")??throw new InvalidOperationException("History declaration retired.");
                var index=node.IndexedOccurrenceKeys?.ToList().IndexOf(key)??-1;
                if(index<0)throw new InvalidOperationException("Required retained fixture message is missing: "+key);
                var query=node.IndexedCollection!;
                return new(node.Id,query.SourceId,query.QueryGeneration,key,index);
            }
            FrameworkElement? Find(string id) => surface is null ? null : Walk(surface).OfType<FrameworkElement>().FirstOrDefault(element => AutomationProperties.GetAutomationId(element) == "Widget." + id);
            async Task MediaMessagesAsync()
            {
                step="media message screens";
                await WidgetPresentationReadiness.WaitAsync(surface!,lifetime.Token);
                await SaveImage(surface!,"servers.png");
                Check(surface!.FocusHostTarget("discord.messages.open"),"Messages has direct top-bar access");
                await Button(ControllerButton.A);
                await Until(()=>Node("discord.messages.list")?.IndexedCollection?.Count==6);
                await WidgetPresentationReadiness.WaitAsync(surface,lifetime.Token);
                await SaveImage(surface,"conversations.png");
                Check(surface.FocusHostTarget("discord.messages.list"),"Conversation list accepts direct group entry");
                await Until(()=>Walk(surface).OfType<WidgetIndexedCollectionView>().Any(list=>list.CaptureFocusedItem()?.Index==0));
                var requirePortrait=Environment.GetCommandLineArgs().Contains("--discord-public-portrait");
                var inboxForPortrait=Walk(surface).OfType<WidgetIndexedCollectionView>().Single(list=>AutomationProperties.GetAutomationId(list)=="Widget.discord.messages.list");
                if(requirePortrait)await Until(()=>Walk(inboxForPortrait).OfType<WidgetArtworkView>().Any(image=>image.Source is Microsoft.UI.Xaml.Media.Imaging.BitmapImage {PixelWidth:>0}));
                var entryFrames=0;var emptyEntryFrames=0;var passiveEntryFrames=0;var staleEntryClaims=0;var missingPortraitFrames=0;
                void ObserveEntry(object? sender,object args)
                {
                    if(surface.ActualWidth<=800||Node("discord.message.history") is null)return;
                    var sidebar=Walk(surface).OfType<WidgetIndexedCollectionView>().FirstOrDefault(list=>AutomationProperties.GetAutomationId(list)=="Widget.discord.messages.list");
                    if(sidebar is not {IsLoaded:true,ActualWidth:>0,ActualHeight:>0})return;
                    ++entryFrames;
                    var rows=Walk(sidebar).OfType<WidgetIndexedRowView>().Where(row=>row.IsLoaded&&row.ActualHeight>0&&row.Content is not null).ToArray();
                    if(rows.Length==0)++emptyEntryFrames;
                    if(requirePortrait&&!Walk(sidebar).OfType<WidgetArtworkView>().Any(image=>image.Source is Microsoft.UI.Xaml.Media.Imaging.BitmapImage {PixelWidth:>0}))++missingPortraitFrames;
                    var retained=rows.Select(row=>row.Row).OfType<WidgetIndexedRow>().Where(row=>!sidebar.Owns(row.Owner)).ToArray();
                    if(retained.Length>0)++passiveEntryFrames;
                    foreach(var row in retained)
                        try{if(row.Lease.ClaimsInput(surface.CurrentBinding!.Frame,row.Item.Key,ControllerButton.A,ControllerEventPhase.Pressed))++staleEntryClaims;}
                        catch(WidgetRail.WidgetPresentationSession.WidgetPresentationSessionException error) when(error.Code is "indexed_retired" or "input_scope_stale"){ }
                }
                CompositionTarget.Rendering+=ObserveEntry;
                try
                {
                    await Button(ControllerButton.A);
                    await Until(()=>Node("discord.message.history")?.IndexedCollection?.Count==20&&FocusId()=="Widget.discord.message.draft");
                    await WidgetPresentationReadiness.WaitAsync(surface,lifetime.Token);
                }
                finally{CompositionTarget.Rendering-=ObserveEntry;}
                trace.Add(new{step="cold sidebar entry",entryFrames,emptyEntryFrames,passiveEntryFrames,staleEntryClaims,missingPortraitFrames});
                if(surface.ActualWidth>800)Check(entryFrames>0&&emptyEntryFrames==0,
                    "Cold Inbox-to-chat entry retains sidebar paint on every sampled native frame");
                Check(staleEntryClaims==0,"Retained sidebar presentation never claims new-scope input");
                if(requirePortrait)Check(entryFrames>0&&missingPortraitFrames==0,"Cold sidebar entry keeps decoded portrait pixels on every sampled native frame");
                Check(!surface.IsFocusPreviewOpen,"Opening media-rich chat leaves focus previews closed");
                await SaveImage(surface,"chat.png");
                if(surface.ActualWidth>800)
                {
                    step="chat switch paint continuity";
                    var sidebar=Walk(surface).OfType<WidgetIndexedCollectionView>().Single(list=>AutomationProperties.GetAutomationId(list)=="Widget.discord.messages.list");
                    var samples=0;var emptyFrames=0;var replacements=0;var sourceReplacements=0;
                    var sidebarSource=sidebar.NativeView.ItemsSource;
                    var section=Node("discord.section.content")!.Transition;
                    void SamplePaint(object? sender,object args)
                    {
                        samples++;
                        var current=Walk(surface).OfType<WidgetIndexedCollectionView>().FirstOrDefault(list=>AutomationProperties.GetAutomationId(list)=="Widget.discord.messages.list");
                        if(!ReferenceEquals(current,sidebar))replacements++;
                        if(current is not null&&!ReferenceEquals(current.NativeView.ItemsSource,sidebarSource))sourceReplacements++;
                        if(current is null||!Walk(current).OfType<WidgetIndexedRowView>().Any(row=>row.IsLoaded&&row.Content is not null&&row.ActualHeight>0))emptyFrames++;
                    }
                    CompositionTarget.Rendering+=SamplePaint;
                    try
                    {
                        Check(surface.FocusHostTarget("discord.messages.list"),"Chat switching enters the existing conversation list");
                        await Until(()=>sidebar.CaptureFocusedItem()?.Index==0&&!sidebar.HasPendingNativeNavigation);
                        await Move(NavigationDirection.Down);
                        await Until(()=>sidebar.CaptureFocusedItem()?.Index==1&&!sidebar.HasPendingNativeNavigation);
                        await Button(ControllerButton.A);
                        await Until(()=>Node("discord.messages.list")?.IndexedPreferredEntryKey=="11"&&Node("discord.message.history")?.IndexedCollection?.Count==20);
                        await WidgetPresentationReadiness.WaitAsync(surface,lifetime.Token);
                        await SaveImage(surface,"chat-switch-morgan.png");
                        Check(surface.FocusHostTarget("discord.messages.list"),"Switched chat retains its conversation-list entry");
                        await Until(()=>sidebar.CaptureFocusedItem()?.Index==1&&!sidebar.HasPendingNativeNavigation);
                        await Move(NavigationDirection.Up);
                        await Until(()=>sidebar.CaptureFocusedItem()?.Index==0&&!sidebar.HasPendingNativeNavigation);
                        await Button(ControllerButton.A);
                        await Until(()=>Node("discord.messages.list")?.IndexedPreferredEntryKey=="10"&&Node("discord.message.history")?.IndexedCollection?.Count==20);
                        await WidgetPresentationReadiness.WaitAsync(surface,lifetime.Token);
                        await SaveImage(surface,"chat-switch-avery.png");
                    }
                    finally { CompositionTarget.Rendering-=SamplePaint; }
                    trace.Add(new{step,samples,emptyFrames,replacements,sourceReplacements});
                    Check(samples>0&&emptyFrames==0&&replacements==0&&sourceReplacements==0,"Chat transitions and unread metadata refresh keep the same painted conversation list and native source on every sampled frame");
                    Check(Node("discord.section.content")!.Transition==section,"Chat switching does not restart the outer section transition");
                }
                var history=Walk(surface).OfType<WidgetIndexedCollectionView>().Single(list=>AutomationProperties.GetAutomationId(list)=="Widget.discord.message.history");
                history.ReadingActionDiagnosticForValidation=detail=>trace.Add(new{step,phase="reading action admission",detail});
                if(Environment.GetCommandLineArgs().Contains("--discord-media-pointer-open") ||
                    Environment.GetCommandLineArgs().Contains("--discord-media-pointer-click"))
                {
                    step="attachment command with composer focus";
                    Check(FocusId()=="Widget.discord.message.draft","Attachment command starts while focus remains in the composer");
                    var action=Walk(history).OfType<Microsoft.UI.Xaml.Controls.Button>().Single(control=>
                        AutomationProperties.GetAutomationId(control).EndsWith(".media-open",StringComparison.Ordinal));
                    Check(action.Command?.CanExecute(action.CommandParameter)==true,"Visible attachment button has an enabled native command");
                    if(Environment.GetCommandLineArgs().Contains("--discord-media-pointer-click"))
                    {
                        var pressed=0;var clicked=0;
                        PointerEventHandler press=(_,_)=>pressed++;
                        RoutedEventHandler click=(_,_)=>clicked++;
                        action.AddHandler(UIElement.PointerPressedEvent,press,true);action.Click+=click;
                        try
                        {
                            await NativeContextProbeInput.ClickWindowElementAsync(
                                Microsoft.UI.Win32Interop.GetWindowFromWindowId(XamlRoot.ContentIslandEnvironment.AppWindowId),action);
                            trace.Add(new{step,phase="native attachment click",pressed,clicked,target=NativeContextProbeInput.LastPointerTrace});
                        }
                        finally{action.RemoveHandler(UIElement.PointerPressedEvent,press);action.Click-=click;}
                    }
                    else if(action.Command is CommunityToolkit.Mvvm.Input.IAsyncRelayCommand command)
                        await command.ExecuteAsync(action.CommandParameter);
                    else throw new InvalidOperationException("Attachment button has no asynchronous command.");
                    await Until(()=>Node("discord.message.gallery.media")?.MediaPlayer?.Gallery?.Count==2);
                    Check(true,"Native attachment command opens its gallery without prior reading-group focus");
                    await Button(ControllerButton.B);
                    await Until(()=>Node("discord.message.gallery.media") is null);
                    history=Walk(surface).OfType<WidgetIndexedCollectionView>().Single(list=>AutomationProperties.GetAutomationId(list)=="Widget.discord.message.history");
                    history.ReadingActionDiagnosticForValidation=detail=>trace.Add(new{step,phase="reading action admission",detail});
                    await Until(()=>history.CaptureFocusedItem()?.ItemKey=="1120"&&!history.HasPendingNativeNavigation);
                    Check(true,"Command-opened gallery returns to its exact message occurrence");
                }
                Check(history.Enter(CurrentMessageTarget("1120")),"Controller directly enters the attachment message");
                await Until(()=>history.CaptureFocusedItem()?.ItemKey=="1120"&&!history.HasPendingNativeNavigation);
                Check(!surface.IsFocusPreviewOpen,"Focusing the attachment message leaves the conversation unobstructed");
                var scroll=Walk(history).OfType<ScrollViewer>().First();
                if(Environment.GetCommandLineArgs().Contains("--trace-reading-layout"))
                {
                    var samples=0;var tracedStep=step;
                    void Record(string kind,object? detail=null)
                    {
                        if(tracedStep!=step){tracedStep=step;samples=0;}
                        if(++samples>120)return;
                        File.AppendAllText(Path.Combine(root,"reading-layout.jsonl"),JsonSerializer.Serialize(new{step,kind,detail,focus=FocusId(),autoReveal=scroll.BringIntoViewOnFocusChange,offset=scroll.VerticalOffset,extent=scroll.ExtentHeight,viewport=scroll.ViewportHeight,
                            rows=Walk(history).OfType<Microsoft.UI.Xaml.Controls.Primitives.SelectorItem>().Select(item=>new{id=AutomationProperties.GetAutomationId(item),item.ActualHeight,item.ActualWidth,y=item.TransformToVisual(scroll).TransformPoint(new Point()).Y}).ToArray()})+Environment.NewLine);
                    }
                    scroll.BringIntoViewRequested+=(_,args)=>Record("reveal",new{target=AutomationProperties.GetAutomationId(args.TargetElement),args.TargetRect,args.Handled});
                    scroll.ViewChanging+=(_,args)=>Record("changing",new{offset=args.NextView.VerticalOffset,stack=step=="media gallery return"?Environment.StackTrace:null});
                    scroll.LayoutUpdated+=(_,_)=>Record("layout");
                    Record("before-entry");
                }
                step="media action entry";
                await Button(ControllerButton.A);
                await Until(()=>history.FocusedReadingAction?.ActionId=="message.media.open");
                Check(surface.CurrentBinding!.View.Root.Kind!=ViewNodeKind.ModalLayer,"Entering attachment actions does not activate them");
                await SaveImage(surface,"chat-actions.png");
                var offset=scroll.VerticalOffset;
                var openedRow=(FrameworkElement)history.NativeView.ContainerFromIndex(CurrentMessageTarget("1120").Index);
                var openedPosition=openedRow.TransformToVisual(scroll).TransformPoint(new Point()).Y;
                trace.Add(new{step,phase="gallery invocation viewport",offset,extent=scroll.ExtentHeight,viewport=scroll.ViewportHeight});
                step="media gallery open";
                await Button(ControllerButton.A);
                await Until(()=>Node("discord.message.gallery.media")?.MediaPlayer?.Gallery?.Count==2);
                trace.Add(new{step,phase="modal return checkpoint",state=surface.CapturePresentationState(),returns=surface.CapturePresentationState()?.CollectionReturns});
                Check(surface.CurrentBinding!.View.ActiveInputScopeId=="discord.message.gallery.scope","Explicit gallery owns a separate modal input scope");
                Check(Node("discord.message.gallery.scroll") is null,"Gallery has no redundant outer scrolling viewport");
                Check(Node("discord.message.gallery.header") is null && surface.CurrentBinding.View.Root.AccessibilityLabel=="Attachments",
                    "Media gallery omits duplicate header chrome while retaining its accessible modal name");
                await Until(()=>surface.CaptureControllerGuide().Any(hint=>hint.Button==ControllerButton.RightBumper&&hint.Label=="Next attachment"));
                await WidgetPresentationReadiness.WaitAsync(surface,lifetime.Token);
                await SaveImage(this,"attachment-modal.png");
                await Button(ControllerButton.RightBumper);
                await Until(()=>Walk(this).OfType<Media.MediaPlayerView>().Any(media=>media.SelectedGalleryItemId=="attachment.5002"));
                Check(Node("discord.message.gallery.media") is not null,"Gallery RB selects the second attachment without leaving the modal");
                step="media gallery return";
                await Button(ControllerButton.B);
                await Until(()=>Node("discord.message.gallery.media") is null);
                var previousHistory=history;
                var previousScroll=scroll;
                history=Walk(surface).OfType<WidgetIndexedCollectionView>().Single(list=>AutomationProperties.GetAutomationId(list)=="Widget.discord.message.history");
                history.ReadingActionDiagnosticForValidation=detail=>trace.Add(new{step,phase="reading action admission",detail});
                scroll=Walk(history).OfType<ScrollViewer>().First();
                trace.Add(new{step,phase="returned native owners",sameCollection=ReferenceEquals(history,previousHistory),sameScroll=ReferenceEquals(scroll,previousScroll)});
                await Until(()=>history.CaptureFocusedItem()?.ItemKey=="1120"&&!history.HasPendingNativeNavigation&&!surface.HasPendingMemoryRestore);
                if(!ReferenceEquals(history,previousHistory))
                    Check(previousHistory.CaptureFocusedItem() is null,"Retired message list cannot claim focus from its replacement");
                await SaveImage(surface,"chat-return.png");
                trace.Add(new{step,phase="modal returned state",request=surface.CurrentBinding!.View.FocusGroupEntryRequest,state=surface.CapturePresentationState(),returns=surface.CapturePresentationState()?.CollectionReturns});
                trace.Add(new{step,phase="gallery returned viewport",offset=scroll.VerticalOffset,extent=scroll.ExtentHeight,viewport=scroll.ViewportHeight});
                var returnedRow=(FrameworkElement)history.NativeView.ContainerFromIndex(CurrentMessageTarget("1120").Index);
                var returnedPosition=returnedRow.TransformToVisual(scroll).TransformPoint(new Point()).Y;
                // Native virtualization can refine estimates for offscreen rows,
                // changing the absolute extent/offset without moving this message.
                trace.Add(new{step,openedPosition,returnedPosition});
                Check(Math.Abs(returnedPosition-openedPosition)<=1,"Closing Discord attachment gallery restores the message's reading position");
                step="repeated media gallery return";
                await Button(ControllerButton.A);
                await Until(()=>history.FocusedReadingAction?.ActionId=="message.media.open");
                await SaveImage(surface,"chat-actions-repeat.png");
                var repeatPosition=((FrameworkElement)history.NativeView.ContainerFromIndex(CurrentMessageTarget("1120").Index))
                    .TransformToVisual(scroll).TransformPoint(new Point()).Y;
                await Button(ControllerButton.A);
                await Until(()=>Node("discord.message.gallery.media") is not null);
                await WidgetPresentationReadiness.WaitAsync(surface,lifetime.Token);
                await Button(ControllerButton.B);
                await Until(()=>Node("discord.message.gallery.media") is null && history.CaptureFocusedItem()?.ItemKey=="1120" &&
                    !history.HasPendingNativeNavigation && !surface.HasPendingMemoryRestore);
                await SaveImage(surface,"chat-return-repeat.png");
                Check(Math.Abs(((FrameworkElement)history.NativeView.ContainerFromIndex(CurrentMessageTarget("1120").Index))
                    .TransformToVisual(scroll).TransformPoint(new Point()).Y-repeatPosition)<=1,
                    "Repeated attachment return preserves the current reading slice");
                await Move(NavigationDirection.Up);
                await Until(()=>history.CaptureFocusedItem()?.Index==18&&!history.HasPendingNativeNavigation);
                await Move(NavigationDirection.Down);
                await Until(()=>history.CaptureFocusedItem()?.ItemKey=="1120"&&!history.HasPendingNativeNavigation);
                await SaveImage(surface,"chat-navigation-after-return.png");
                Check(history.CaptureFocusedItem()?.ItemKey=="1120",
                    "Ordinary controller navigation continues after the one-shot return reveal");
                step="single inline video";
                Check(history.Enter(CurrentMessageTarget("1117")),"Controller enters the single-video message");
                await Until(()=>history.CaptureFocusedItem()?.ItemKey=="1117"&&!history.HasPendingNativeNavigation);
                await Until(()=>history.FocusedReadingAction?.MediaPlayer?.Presentation==MediaPlayerPresentation.Inline);
                Check(FocusId()=="MediaPlayer.Inline.Play","Entering the video message focuses Play without activating it");
                Check(Walk(history).OfType<Media.MediaPlayerView>().Any(media=>media.IsInlinePoster)&&
                    surface.CaptureControllerGuide().Any(hint=>hint.Button==ControllerButton.A&&hint.Label=="Play video"),
                    "Single-video message exposes a native poster and direct controller Play hint");
                Check(Media.MediaPlayerView.ResidentPlayers==0,"Entering inline video does not open a decoder or contact the video source");
                var inlinePlayer=Walk(history).OfType<Media.MediaPlayerView>().First(media=>media.IsInlinePoster);
                Check(inlinePlayer.ActualWidth<=481&&inlinePlayer.ActualWidth>0&&Math.Abs(inlinePlayer.ActualWidth/inlinePlayer.ActualHeight-16d/9)<.03,
                    "Inline video follows the bounded native aspect-ratio frame");
                if(surface.ActualWidth>=700)
                    Check(inlinePlayer.ActualWidth>=420&&inlinePlayer.ActualHeight>=230,
                        "Expanded chat uses a substantial video viewport without filling the entire chat column");
                await SaveImage(surface,"inline-video.png");
                await Button(ControllerButton.B);
                await Until(()=>history.FocusedReadingAction is null&&history.CaptureFocusedItem()?.ItemKey=="1117");
                step="rapid section switches with an inline video";
                var switchFrames=0;var mediaErrorFrames=0;
                void ObserveMediaSwitch(object? sender,object args)
                {
                    ++switchFrames;
                    if(Walk(surface).OfType<TextBlock>().FirstOrDefault(text=>AutomationProperties.GetAutomationId(text)=="MediaPlayer.Status" &&
                        text.Text.Contains("could not be opened",StringComparison.Ordinal) && Displayed(text)) is { } errorText)
                    {
                        if (mediaErrorFrames++ == 0)
                        {
                            var ancestry = new List<string>();
                            for (DependencyObject? item = errorText; item is FrameworkElement element; item = VisualTreeHelper.GetParent(item))
                                ancestry.Add($"{element.GetType().Name} {AutomationProperties.GetAutomationId(element)} {element.Opacity:F2}");
                            trace.Add(new { step, phase = "first visible media failure", ancestry });
                        }
                    }
                }
                CompositionTarget.Rendering+=ObserveMediaSwitch;
                try
                {
                    for(var i=0;i<3;i++)
                    {
                        await Button(ControllerButton.LeftBumper);
                        await Until(()=>Node("discord.main.mute") is not null);
                        await Button(ControllerButton.RightBumper);
                        await Until(()=>Find("discord.message.draft") is {IsLoaded:true});
                    }
                    await Button(ControllerButton.LeftBumper);
                    await Until(()=>Node("discord.main.mute") is not null);
                }
                finally{CompositionTarget.Rendering-=ObserveMediaSwitch;}
                Check(switchFrames>0 && mediaErrorFrames==0,$"No media error is painted during rapid section switches ({mediaErrorFrames}/{switchFrames} frames)");
                await Until(()=>Node("discord.main.mute") is not null);
                Check(Node("discord.call.open")?.Text=="Servers","LB returns to consistently named Servers");
                Check(Audit().GetProperty("mutationAttempts").GetInt32()==0,"Media navigation performs no message send, call or upload");
            }

            async Task SaveImage(UIElement element,string name)
            {
                if(surface is not null)await Until(()=>surface.IsGuidePresentationReady(requireFocus:false));
                var captureBackdrop = ReferenceEquals(element, surface) && Environment.GetCommandLineArgs().Contains("--discord-solid-captures");
                WidgetPackageIconView[] CaptureIcons() => VisibleIcons(element).ToArray();
                static IEnumerable<WidgetPackageIconView> VisibleIcons(DependencyObject root)
                {
                    if(root is UIElement { Visibility:Visibility.Collapsed })yield break;
                    if(root is WidgetPackageIconView { IsLoaded:true, ActualWidth:>0, ActualHeight:>0 } icon)yield return icon;
                    for(var index=0;index<VisualTreeHelper.GetChildrenCount(root);index++)
                        foreach(var child in VisibleIcons(VisualTreeHelper.GetChild(root,index)))yield return child;
                }
                if(captureBackdrop)
                    await Until(()=>!WidgetSurface.HasDesktopAcrylic && SurfaceBackground.Background is SolidColorBrush {Color.A:255});
                var captureRoot = captureBackdrop ? this : element;
                var bitmap=new Microsoft.UI.Xaml.Media.Imaging.RenderTargetBitmap();
                byte[] pixels;
                object[] capturedIcons=[];
                var allCapturedIconsReady=false;
                var captureDeadline=Environment.TickCount64+15000;
                while(true)
                {
                    if(captureBackdrop)
                        while(CaptureIcons().Any(icon=>icon.CapturePreparation.Pending)&&Environment.TickCount64<captureDeadline)
                            await Task.Delay(25,lifetime.Token);
                    var before=CaptureIcons();
                    // Row realization can add icons at the composition boundary.
                    // Require the same settled set through the actual pixel read.
                    for(var frame=0;frame<2;frame++)
                    {
                        var rendered=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                        EventHandler<object> callback=(_,_)=>rendered.TrySetResult();
                        CompositionTarget.Rendering+=callback;
                        try{await rendered.Task.WaitAsync(TimeSpan.FromSeconds(5),lifetime.Token);}
                        finally{CompositionTarget.Rendering-=callback;}
                    }
                    await bitmap.RenderAsync(captureRoot);
                    pixels=(await bitmap.GetPixelsAsync()).ToArray();
                    var after=CaptureIcons();
                    allCapturedIconsReady=after.All(icon=>!icon.CapturePreparation.Required||icon.CapturePreparation.Ready);
                    capturedIcons=after.Select(icon=>(object)new {name=icon.CapturePreparation.Label,
                        required=icon.CapturePreparation.Required,pending=icon.CapturePreparation.Pending,ready=icon.CapturePreparation.Ready}).ToArray();
                    if(!captureBackdrop || before.SequenceEqual(after)&&after.All(icon=>!icon.CapturePreparation.Pending))break;
                    if(Environment.TickCount64>=captureDeadline)break;
                }
                using var stream=new Windows.Storage.Streams.InMemoryRandomAccessStream();
                var encoder=await Windows.Graphics.Imaging.BitmapEncoder.CreateAsync(Windows.Graphics.Imaging.BitmapEncoder.PngEncoderId,stream);
                encoder.SetPixelData(Windows.Graphics.Imaging.BitmapPixelFormat.Bgra8,Windows.Graphics.Imaging.BitmapAlphaMode.Premultiplied,
                    (uint)bitmap.PixelWidth,(uint)bitmap.PixelHeight,96,96,pixels);
                if(captureBackdrop)
                {
                    var bounds=WidgetSurface.TransformToVisual(this).TransformBounds(new Rect(0,0,WidgetSurface.ActualWidth,WidgetSurface.ActualHeight));
                    var scaleX=bitmap.PixelWidth/ActualWidth;
                    var scaleY=bitmap.PixelHeight/ActualHeight;
                    var left=(uint)Math.Clamp(Math.Floor(bounds.Left*scaleX),0,bitmap.PixelWidth-1);
                    var top=(uint)Math.Clamp(Math.Floor(bounds.Top*scaleY),0,bitmap.PixelHeight-1);
                    var right=(uint)Math.Clamp(Math.Ceiling(bounds.Right*scaleX),left+1,bitmap.PixelWidth);
                    var bottom=(uint)Math.Clamp(Math.Ceiling(bounds.Bottom*scaleY),top+1,bitmap.PixelHeight);
                    encoder.BitmapTransform.Bounds=new(){X=left,Y=top,Width=right-left,Height=bottom-top};
                    Check(right-left>=bounds.Width*scaleX-2 && bottom-top>=bounds.Height*scaleY-2,
                        "Solid capture retains the complete widget bounds for "+name);
                    await File.WriteAllTextAsync(Path.Combine(root,name+".capture.json"),JsonSerializer.Serialize(new
                    {kind="solid-host-backdrop",width=right-left,height=bottom-top,icons=capturedIcons,bounds,
                        rootWidth=ActualWidth,rootHeight=ActualHeight,pixelWidth=bitmap.PixelWidth,pixelHeight=bitmap.PixelHeight,
                        opaque=SurfaceBackground.Background is SolidColorBrush {Color.A:255}}));
                }
                await encoder.FlushAsync();stream.Seek(0);using var output=File.Create(Path.Combine(root,name));await stream.AsStreamForRead().CopyToAsync(output);
                // Assert the icon state that produced these pixels, not a newer
                // realization arriving while PNG encoding awaited the dispatcher.
                if(captureBackdrop)Check(allCapturedIconsReady,
                    "All visible package icons have completed native preparation for "+name);
            }
            bool Displayed(FrameworkElement element)
            {
                if (!element.IsLoaded || element.ActualWidth <= 0 || element.ActualHeight <= 0) return false;
                for (DependencyObject? item = element; item is not null; item = VisualTreeHelper.GetParent(item)) if (item is UIElement { Visibility: not Visibility.Visible }) return false;
                return true;
            }
            bool InSurface(FrameworkElement element)
            {
                var bounds = element.TransformToVisual(surface).TransformBounds(new Rect(0, 0, element.ActualWidth, element.ActualHeight));
                return bounds.Width > 0 && bounds.Height > 0 && bounds.Left >= -.5 && bounds.Top >= -.5 && bounds.Right <= surface!.ActualWidth + .5 && bounds.Bottom <= surface.ActualHeight + .5;
            }
            async Task Until(Func<bool> condition,[System.Runtime.CompilerServices.CallerArgumentExpression(nameof(condition))] string? expectation=null)
            {
                var deadline = Environment.TickCount64 + 15000;
                while (!condition())
                {
                    if (validationFailure is not null || Environment.TickCount64 > deadline) throw new InvalidOperationException("Messages did not settle: " + step + "; expected="+expectation+"; focus=" + FocusId(), validationFailure);
                    await Task.Delay(20, lifetime.Token);
                }
            }
            async Task SectionReturnAsync(double expectedWidth)
            {
                step="Call and Messages section return";
                var readsBeforeSection=Audit().GetProperty("history").GetArrayLength();
                var readingHistory=Walk(surface).OfType<WidgetIndexedCollectionView>().Single(view=>AutomationProperties.GetAutomationId(view)=="Widget.discord.message.history");
                Check(readingHistory.Enter(CurrentMessageTarget("1111")),"A middle message establishes the section-return reading position");
                await Until(()=>readingHistory.CaptureFocusedItem()?.ItemKey=="1111"&&!readingHistory.HasPendingNativeNavigation);
                Check(surface.FocusHostTarget("discord.message.draft"),"Section switching can start from the composer while retaining a reading viewport");
                await Task.Delay(300,lifetime.Token);
                var reading=surface.CapturePresentationState()!.IndexedViewports.Single(item=>item.Element.Id=="discord.message.history");
                for(var round=0;round<3;round++)
                {
                    await Button(ControllerButton.LeftBumper);
                    await Until(()=>Node("discord.call.page") is not null);
                    Check(Node("discord.sections") is not null,"Call retains the persistent section switcher");
                    await Button(ControllerButton.RightBumper);
                    await Until(()=>Find("discord.message.draft") is {IsLoaded:true});
                    await Until(()=>!surface.HasPendingMemoryRestore && surface.CapturePresentationState()!.IndexedViewports.Any(item=>
                        item.Element.Id==reading.Element.Id&&item.Key==reading.Key&&Math.Abs(item.ClippedFraction-reading.ClippedFraction)<.02));
                    await Task.Delay(250,lifetime.Token);
                    var restored=surface.CapturePresentationState()!.IndexedViewports.Single(item=>item.Element.Id==reading.Element.Id);
                    Check(restored.Key==reading.Key&&Math.Abs(restored.ClippedFraction-reading.ClippedFraction)<.02,
                        "Call/Messages round trip "+round+" preserves the visible message and its clipped position");
                }
                Check(Audit().GetProperty("history").GetArrayLength()==readsBeforeSection,
                    "Section return preserves reading position without fetching history");
                if(expectedWidth>800)
                {
                    var conversations=Walk(surface).OfType<WidgetIndexedCollectionView>().Single(view=>AutomationProperties.GetAutomationId(view)=="Widget.discord.messages.list");
                    var conversationQuery=Node("discord.messages.list")!.IndexedCollection!;
                    Check(conversations.Enter(new("discord.messages.list",conversationQuery.SourceId,conversationQuery.QueryGeneration,"11",1)),"The nonselected sidebar conversation is focusable");
                    await Until(()=>conversations.CaptureFocusedItem()?.ItemKey=="11"&&!conversations.HasPendingNativeNavigation);
                    await Move(NavigationDirection.Right);
                    await Until(()=>FocusId()=="Widget.discord.message.draft");
                    Check(true,"Right from the conversation list enters the composer");
                    await Move(NavigationDirection.Left);
                    await Until(()=>FocusId()=="Widget.discord.messages.list.Item.0");
                    Check(true,"Left from the composer returns to the selected conversation, not the last focused sidebar row");
                    readingHistory=Walk(surface).OfType<WidgetIndexedCollectionView>().Single(view=>AutomationProperties.GetAutomationId(view)=="Widget.discord.message.history");
                    Check(readingHistory.Enter(CurrentMessageTarget("1111")),"History can own focus before a cross-panel exit");
                    await Until(()=>readingHistory.CaptureFocusedItem()?.ItemKey=="1111"&&!readingHistory.HasPendingNativeNavigation);
                    await Move(NavigationDirection.Left);
                    await Until(()=>FocusId()=="Widget.discord.messages.list.Item.0");
                    Check(true,"Left from history also returns to the selected conversation");
                }
            }
            async Task LargeConversationsAsync()
            {
                step="automatic conversation loading";
                WidgetIndexedCollectionView Conversations()=>Walk(surface!).OfType<WidgetIndexedCollectionView>()
                    .Single(view=>AutomationProperties.GetAutomationId(view)=="Widget.discord.messages.list");
                Check(Node("discord.messages.next") is null && Node("discord.messages.previous") is null,
                    "Large conversation catalog has no paging buttons");
                for(var index=1;index<=70;index++)
                {
                    await Move(NavigationDirection.Down);
                    var expected=index;
                    await Until(()=>Conversations().CaptureFocusedItem()?.Index==expected&&!Conversations().HasPendingNativeNavigation);
                }
                Check(Conversations().CaptureFocusedItem()?.ItemKey=="80","D-pad crosses provider pages in provider order");
                var ranges=Audit().GetProperty("conversationRanges").EnumerateArray().ToArray();
                Check(ranges.Any(range=>range.GetProperty("offset").GetInt32()>=40)&&ranges.All(range=>range.GetProperty("limit").GetInt32()<=50),
                    "Scrolling beyond the first page loads bounded provider ranges automatically");
                Check(ranges.All(range=>range.GetProperty("offset").GetInt32()+range.GetProperty("limit").GetInt32()<120),
                    "Partial browsing does not fetch the entire conversation catalog");
                Check(Audit().GetProperty("history").GetArrayLength()==0,"Browsing conversation rows never eagerly opens their histories");
                for(var index=69;index>=0;index--)
                {
                    await Move(NavigationDirection.Up);
                    var expected=index;
                    await Until(()=>Conversations().CaptureFocusedItem()?.Index==expected&&!Conversations().HasPendingNativeNavigation);
                }
                Check(true,"Up crosses provider pages in reverse order without taking the Search exit early");
                await Move(NavigationDirection.Up);await Until(()=>FocusId()=="Widget.discord.messages.search");
                Check(true,"After reverse scrolling only the first-row boundary exits to Search");
                await Move(NavigationDirection.Down);
                await Until(()=>Conversations().CaptureFocusedItem()?.Index==0&&!Conversations().HasPendingNativeNavigation);
                for(var index=1;index<=70;index++)
                {
                    await Move(NavigationDirection.Down);
                    var expected=index;
                    await Until(()=>Conversations().CaptureFocusedItem()?.Index==expected&&!Conversations().HasPendingNativeNavigation);
                }
                await Button(ControllerButton.A);
                await Until(()=>Node("discord.message.history")?.IndexedCollection?.Count==20&&FocusId()=="Widget.discord.message.draft");
                Check(Audit().GetProperty("history").EnumerateArray().All(read=>read.GetProperty("channel").GetString()=="80"),
                    "Opening the off-page conversation fetches only that channel's bounded history");
                await Move(NavigationDirection.Left);
                await Until(()=>Conversations().CaptureFocusedItem()?.ItemKey=="80"&&!Conversations().HasPendingNativeNavigation);
                Check(Audit().GetProperty("conversationLookups").EnumerateArray().Any(lookup=>lookup.GetProperty("key").GetString()=="80"),
                    "Sidebar entry locates its off-page selected conversation through the provider key lookup");
                await Move(NavigationDirection.Down);
                await Until(()=>Conversations().CaptureFocusedItem()?.ItemKey=="81"&&!Conversations().HasPendingNativeNavigation);
                await Move(NavigationDirection.Right);await Until(()=>FocusId()=="Widget.discord.message.draft");
                await Move(NavigationDirection.Left);
                await Until(()=>Conversations().CaptureFocusedItem()?.ItemKey=="80"&&!Conversations().HasPendingNativeNavigation);
                Check(true,"Returning from the composer prefers the selected conversation over the last browsed row");
                await Move(NavigationDirection.Right);await Until(()=>FocusId()=="Widget.discord.message.draft");
                var reads=Audit().GetProperty("history").GetArrayLength();
                await Button(ControllerButton.LeftBumper);await Until(()=>Node("discord.call.page") is not null);
                await Button(ControllerButton.RightBumper);await Until(()=>Find("discord.message.draft") is {IsLoaded:true});
                await Until(()=>FocusId()=="Widget.discord.message.draft"&&!surface!.HasPendingMemoryRestore);
                Check(true,"Restored off-page chat returns focus to its composer");
                await Move(NavigationDirection.Left);
                await Until(()=>Conversations().CaptureFocusedItem()?.ItemKey=="80"&&!Conversations().HasPendingNativeNavigation);
                Check(Audit().GetProperty("history").GetArrayLength()==reads,"Section return preserves the off-page conversation without reloading history");
                Check(Audit().GetProperty("mutationAttempts").GetInt32()==0,"Automatic loading and selected-row restoration perform no message or call mutation");
            }
            async Task PrivateCallsAsync()
            {
                step="private call controls";
                var first=new string('b',32);var second=new string('c',32);
                JsonElement Calls(){using var document=JsonDocument.Parse(File.ReadAllText(Path.Combine(root,"calls-provider.json")));return document.RootElement.Clone();}
                bool Called(string method)=>Calls().GetProperty("actions").EnumerateArray().Any(action=>action.GetProperty("method").GetString()==method);
                await notificationCenter!.Preferences.SetAsync("widgetrail.tests.discord-ui",Notifications.NotificationPresentation.BannerAndList,lifetime.Token);
                await Until(()=>Node("discord.incoming.answer."+first) is not null);
                Check(Node("discord.call.open")?.Text=="Servers" && Node("discord.call.dock.mute") is null,
                    "No active call uses Servers and omits the call dock while offering incoming actions");
                Check(surface!.FocusHostTarget("discord.incoming.answer."+first),"Incoming Answer is a native controller focus target");
                await Move(NavigationDirection.Right);await Until(()=>FocusId()=="Widget.discord.incoming.decline."+first);
                Check(true,"Controller traversal reaches Decline beside Answer");
                await Button(ControllerButton.A);await Until(()=>Node("discord.calls.incoming") is null&&Called("callDecline"));
                Check(Calls().GetProperty("microphoneRequests").GetInt32()==0,"Native Decline dispatches without microphone admission");
                Check(surface.FocusHostTarget("discord.messages.open"),"Messages remains reachable after declining");
                await Button(ControllerButton.A);await Until(()=>Node("discord.messages.list")?.IndexedCollection?.Count==2);
                for(var attempt=0;attempt<10&&FocusId()!="Widget.discord.messages.list.Item.0";attempt++)await Move(NavigationDirection.Down);
                Check(FocusId()=="Widget.discord.messages.list.Item.0","Controller reaches an existing DM for an outgoing call");
                await Button(ControllerButton.A);await Until(()=>FocusId()=="Widget.discord.message.draft"&&Node("discord.message.call") is not null);
                Check(Node("discord.message.call")?.Text=="Call" && Node("discord.message.call.hint")?.ControllerPrompt==ControllerPrompt.LeftStickPress,
                    "Conversation header exposes Call with a native L3 hint");
                await Button(ControllerButton.LeftStick);await Until(()=>FocusId()=="Widget.discord.message.call");
                Check(!Called("callStart")&&Calls().GetProperty("microphoneRequests").GetInt32()==0,
                    "L3 moves from composer directly to Call without ringing or microphone admission");
                await Button(ControllerButton.A);
                await Until(()=>Called("callStart")&&Node("discord.call.dock.leave") is not null&&Calls().GetProperty("phases").EnumerateArray().Any(phase=>phase.GetString()=="active"));
                Check(Calls().GetProperty("microphoneRequests").GetInt32()==1&&Calls().GetProperty("phases").EnumerateArray().Any(phase=>phase.GetString()=="outgoing"),
                    "Outgoing call uses microphone admission and reconciles outgoing to active state");
                Check(Node("discord.call.open")?.Text=="Servers","Active call retains the Servers destination label");
                await CallDockTransitionsAsync();
                Check(surface.FocusHostTarget("discord.call.dock.leave"),"The Messages call dock exposes Leave");
                notificationCenter.Inbox.Clear();
                await Button(ControllerButton.A);await Until(()=>Node("discord.call.dock.leave") is null);
                SetVisible(false);
                step="background private call notification";
                await Until(()=>notificationCenter.Inbox.Entries.Any(entry=>entry.Widget=="widgetrail.tests.discord-ui"&&entry.Title=="Incoming call"));
                await Until(()=>notificationBanner?.IsVisible==true&&notificationCard?.TitleText.Text=="Incoming call");
                Check(!visible&&Calls().GetProperty("microphoneRequests").GetInt32()==1,
                    "A synthetic invitation reaches the real host inbox and banner while hidden without microphone admission");
                SetVisible(true);App.Window!.Activate();await Until(OwnsForeground);
                var open=OpenNotificationListForValidation();await Until(()=>utilityPanel is {IsLoaded:true});
                var list=FindControl<ListView>(utilityPanel!,"Overlay.Utility.List")!;
                list.SelectedIndex=0;((Control)list.Items[0]).Focus(FocusState.Keyboard);
                Receive(new ControllerFrame{Connected=1});
                await RouteButtonAsync(ControllerButton.A,ControllerEventPhase.Pressed);
                await RouteButtonAsync(ControllerButton.A,ControllerEventPhase.Released);await open;
                await Until(()=>utilityPanel is null&&FocusId()=="Widget.discord.incoming.answer."+second);
                Check(!Called("callAnswer")&&Calls().GetProperty("microphoneRequests").GetInt32()==1,
                    "Opening the real call notification focuses Answer without answering or granting microphone access");
                await Button(ControllerButton.A);await Until(()=>Called("callAnswer")&&Node("discord.main.leave") is not null);
                Check(Calls().GetProperty("microphoneRequests").GetInt32()==2&&Calls().GetProperty("current").GetString()=="11",
                    "Native Answer enters the exact synthetic invitation after microphone admission");
                await Until(()=>notificationCenter.Inbox.Entries.All(entry=>entry.Widget!="widgetrail.tests.discord-ui"));
                Check(Node("discord.calls.incoming") is null,"Answered invitation and its host alert are withdrawn");
                await Until(()=>MainFocusEnabled&&!surface.HasPendingMemoryRestore&&
                    Find("discord.main.leave") is Control {IsLoaded:true,IsEnabled:true,IsTabStop:true,ActualWidth:>0} leave&&Displayed(leave));
                Check(surface.FocusHostTarget("discord.main.leave"),"Joined private call reuses the native Leave control");
                await Button(ControllerButton.A);await Until(()=>Node("discord.call.open")?.Text=="Servers"&&Node("discord.main.leave") is null);
                Check(Audit().GetProperty("mutationAttempts").GetInt32()==0,"Private-call fixture completes without messages, captures, devices or network activity");
            }
            async Task CallDockTransitionsAsync()
            {
                step="dynamic call dock";
                Check(surface!.FocusHostTarget("discord.call.dock.mute"),"Dock Mute establishes controller focus before participant updates");
                var mute=Find("discord.call.dock.mute");
                double? dockHeight=null,composerTop=null;
                foreach(var phase in new[]{"overflow","reconnecting","one","empty"})
                {
                    await File.WriteAllTextAsync(Path.Combine(root,"call-dock-phase.txt"),phase,lifetime.Token);
                    var count=phase is "overflow" or "reconnecting"?4:phase=="one"?1:0;
                    await Until(()=>Node("discord.call.dock.people")?.Children.Count==count+(count==4?1:0)&&
                        (Node("discord.call.dock.name")?.Text?.StartsWith("Reconnecting",StringComparison.Ordinal)==true)==(phase=="reconnecting")&&
                        (phase!="reconnecting"||Node("discord.call.dock.people")?.Children.FirstOrDefault()?.Id=="discord.call.dock.person.7"));
                    await Task.Delay(100,lifetime.Token);
                    var currentHeight=Find("discord.call.dock")!.ActualHeight;
                    var currentTop=Find("discord.message.draft")!.TransformToVisual(surface).TransformPoint(new(0,0)).Y;
                    dockHeight??=currentHeight;composerTop??=currentTop;
                    Check(Math.Abs(currentHeight-dockHeight.Value)<.5&&Math.Abs(currentTop-composerTop.Value)<.5,
                        "Provider "+phase+" preserves dock height and composer position");
                    Check(FocusId()=="Widget.discord.call.dock.mute"&&ReferenceEquals(mute,Find("discord.call.dock.mute")),
                        "Provider "+phase+" retains the same native Mute control and controller focus");
                    Check((Node("discord.call.dock.more")?.Text??"")==(count==4?"+3":"")&&
                        new[]{"open","mute","deafen","share","leave"}.All(id=>Find("discord.call.dock."+id) is {} control&&InSurface(control))&&
                        Find("discord.message.draft") is {} draft&&InSurface(draft),
                        "Provider "+phase+" keeps overflow, all dock actions and composer within the actual surface");
                    if(phase=="reconnecting")Check(Node("discord.call.dock.open")!.AccessibilityLabel!.Split("Reconnecting to voice…",StringSplitOptions.None).Length==2,
                        "Native reconnect summary announces connection state exactly once");
                    await SaveImage(surface,"call-dock-"+phase+".png");
                }
                await Move(NavigationDirection.Left);await Until(()=>FocusId()=="Widget.discord.call.dock.open");
                Check(true,"One Left from Mute reaches the participant summary without traversing avatars");
                await Button(ControllerButton.A);await Until(()=>Node("discord.call.page") is not null&&Node("discord.main.leave") is not null);
                Check(Node("discord.call.open")?.Text=="Servers","Dock summary opens the full call while preserving the Servers label");
                await Button(ControllerButton.RightBumper);await Until(()=>Node("discord.call.dock.mute") is not null&&Find("discord.message.draft") is {IsLoaded:true});
                Check(true,"Messages bumper returns to the same chat after inspecting the call");
            }
            async Task DirectionalHistoryEntryAsync()
            {
                var history=Walk(surface!).OfType<WidgetIndexedCollectionView>().Single(view=>AutomationProperties.GetAutomationId(view)=="Widget.discord.message.history");
                var scroll=Walk(history).OfType<ScrollViewer>().First();
                string? LastVisibleKey()
                {
                    var keys=Node("discord.message.history")?.IndexedOccurrenceKeys;
                    if(keys is null)return null;
                    var visible=WidgetScrollViewport.ContentBounds(scroll);
                    return Enumerable.Range(0,keys.Count).Where(index=>history.NativeView.ContainerFromIndex(index) is FrameworkElement {IsLoaded:true,ActualHeight:>0} row&&
                        row.TransformToVisual(scroll).TransformBounds(new(0,0,row.ActualWidth,row.ActualHeight)) is var bounds&&
                        bounds.Bottom>visible.Top+1&&bounds.Top<visible.Bottom-1).Select(index=>keys[index]).LastOrDefault();
                }
                async Task EnterRemembered()
                {
                    Check(history.Enter(CurrentMessageTarget("1111")),"A retained message can establish ordinary native item memory");
                    await Until(()=>history.CaptureFocusedItem()?.ItemKey=="1111"&&!history.HasPendingNativeNavigation);
                    await Button(ControllerButton.X);await Until(()=>FocusId()=="Widget.discord.message.draft");
                }
                step="composer Up after Latest";
                await EnterRemembered();
                var beforeLatest=Node("discord.message.history")!.IndexedCollection!;
                await Button(ControllerButton.Y);
                await Until(()=>FocusId()=="Widget.discord.message.draft"&&LastVisibleKey()=="1120"&&scroll.ScrollableHeight-scroll.VerticalOffset<=1&&
                    Node("discord.message.history")?.IndexedCollection is {} latest&&(latest.QueryGeneration!=beforeLatest.QueryGeneration||latest.ContentRevision!=beforeLatest.ContentRevision));
                var reads=Audit().GetProperty("history").GetArrayLength();
                var offset=scroll.VerticalOffset;
                trace.Add(new{step,phase="before Up",remembered="1111",visible=LastVisibleKey(),offset,source=Node("discord.message.history")?.IndexedCollection});
                await Move(NavigationDirection.Up);
                await Until(()=>history.CaptureFocusedItem() is not null&&!history.HasPendingNativeNavigation);
                trace.Add(new{step,phase="after Up",focused=history.CaptureFocusedItem()?.ItemKey,offset=scroll.VerticalOffset});
                Check(history.CaptureFocusedItem()?.ItemKey=="1120","Up from composer enters current latest visible history instead of old offscreen item memory");
                Check(Audit().GetProperty("history").GetArrayLength()==reads,"Composer entry does not add a Discord history request");
                step="composer Up while reading earlier messages";
                await EnterRemembered();
                var expected=LastVisibleKey();var readingOffset=scroll.VerticalOffset;
                Check(expected is not null&&expected!="1120","Earlier reading has a distinct current visible edge");
                await Move(NavigationDirection.Up);
                await Until(()=>history.CaptureFocusedItem() is not null&&!history.HasPendingNativeNavigation);
                Check(history.CaptureFocusedItem()?.ItemKey==expected,"Up respects the current reading viewport rather than jumping to the conversation tail");
                trace.Add(new{step,expected,readingOffset,afterOffset=scroll.VerticalOffset,focused=history.CaptureFocusedItem()?.ItemKey});
                Check(Audit().GetProperty("mutationAttempts").GetInt32()==0,"Directional entry sends no messages or attachments");
            }
            async Task SentMessageRevealAsync()
            {
                step="send from a historical conversation window";
                Check(surface!.FocusHostTarget("discord.messages.open"),"Messages accepts input");
                await Button(ControllerButton.A);
                await Until(()=>Node("discord.messages.list")?.IndexedCollection?.Count==2);
                Check(surface.FocusHostTarget("discord.messages.list"),"Conversations accept input");
                await Until(()=>Walk(surface).OfType<WidgetIndexedCollectionView>().Any(list=>list.CaptureFocusedItem()?.Index==0));
                await Button(ControllerButton.A);
                await Until(()=>Node("discord.message.history")?.IndexedOccurrenceKeys?.Contains("1040")==true && FocusId()=="Widget.discord.message.draft");
                Check(Node("discord.message.history")!.IndexedOccurrenceKeys?.Contains("1120")==false,
                    "The starting history window is older than the latest message");
                await SaveImage(surface,"before-send.png");
                await Button(ControllerButton.A);
                await Until(()=>surface.ControllerTextEntry?.IsLoaded==true);
                var editor=Walk(surface.ControllerTextEntry!).OfType<TextBox>().Single();
                editor.Focus(FocusState.Keyboard);
                editor.Text="Generated outgoing message";
                NativeContextProbeInput.PressKey(VirtualKey.Enter);
                await Until(()=>surface.ControllerTextEntry is null && Node("discord.message.send") is {IsDisabled:not true});
                Check(surface.FocusHostTarget("discord.message.send"),"The explicit Send action is reachable");
                await Button(ControllerButton.A);
                await Until(()=>Audit().GetProperty("mutationAttempts").GetInt32()==1 && FocusId()=="Widget.discord.message.draft" && SentRowVisible());
                Check(true,"After sending from older history, the exact confirmed row is visible and focus is back in the composer");
                // Let the widget's 250 ms read coalescing delay expire after
                // the sent row is on screen. The synthetic read head still lags.
                await Task.Delay(750,lifetime.Token);
                Check(Audit().GetProperty("readTargetsAheadOfHead").GetInt32()==0 && SentRowVisible(),
                    "Sending from older history does not issue a read write for the outgoing row ahead of Discord's read head");
                await SaveImage(surface,"after-send.png");
                bool SentRowVisible()
                {
                    var keys=Node("discord.message.history")?.IndexedOccurrenceKeys;
                    var index=keys is null?-1:Array.IndexOf(keys.ToArray(),"1121");
                    var history=Walk(surface).OfType<WidgetIndexedCollectionView>()
                        .SingleOrDefault(list=>AutomationProperties.GetAutomationId(list)=="Widget.discord.message.history");
                    if(index<0 || history?.NativeView.ContainerFromIndex(index) is not FrameworkElement {IsLoaded:true,ActualHeight:>0} row)return false;
                    var scroll=Walk(history.NativeView).OfType<ScrollViewer>().First();
                    var viewport=WidgetScrollViewport.ContentBounds(scroll);
                    var bounds=row.TransformToVisual(scroll).TransformBounds(new(0,0,row.ActualWidth,row.ActualHeight));
                    return bounds.Top>=viewport.Top-1 && bounds.Bottom<=viewport.Bottom+1;
                }
            }

            async Task HiddenChatReadAsync()
            {
                step="hide pending chat with a visible voice pin";
                var descriptor=surface!.CurrentBinding!.Frame.Descriptor;
                Check(GetPinChoices(descriptor).Any(choice=>choice.LayoutId=="discord.call"),
                    "The host offers the synthetic call's voice pin");
                await PinAsync(descriptor.Id,"discord.call");
                await Until(()=>pinned?.LayoutId=="discord.call" && pinned.Presenter?.CurrentBinding is not null);
                Check(surface.FocusHostTarget("discord.messages.open"),"Open Messages beside the voice pin");
                await Button(ControllerButton.A);
                await Until(()=>Node("discord.messages.list")?.IndexedCollection?.Count==2);
                Check(surface.FocusHostTarget("discord.messages.list"),"Conversations accept keyboard focus");
                await Until(()=>Walk(surface).OfType<WidgetIndexedCollectionView>().Any(list=>list.CaptureFocusedItem()?.Index==0));
                await Button(ControllerButton.A);
                await Until(()=>Audit().GetProperty("history").GetArrayLength()>0);
                SetVisible(false);
                var gate=Path.Combine(root,"hold-history");
                Check(File.Exists(gate) && !File.Exists(gate+".completed"),
                    "The main overlay hides before the provider returns its first history page");
                File.Delete(gate);
                await Until(()=>File.Exists(gate+".completed"));
                // Allow publication and the widget's 250 ms read coalescing delay
                // to elapse while the independently pinned call remains visible.
                await Task.Delay(900,lifetime.Token);
                Check(pinned?.LayoutId=="discord.call" && Audit().GetProperty("readAcks").GetArrayLength()==0,
                    "Completing history while only the call pin is visible leaves the conversation unread");
                SetVisible(true);App.Window!.Activate();await Until(OwnsForeground);
                await Until(()=>Node("discord.message.history")?.IndexedCollection?.Count==20 &&
                    Audit().GetProperty("readAcks").GetArrayLength()==1);
                var ack=Audit().GetProperty("readAcks")[0];
                Check(ack.GetProperty("channel").GetString()=="10" && ack.GetProperty("message").GetString()=="1120",
                    "Reopening the overlay acknowledges the displayed conversation through its opening message");
                await SaveImage(surface,"reopened-chat.png");
            }

            async Task HistoryLifecycleAsync()
            {
                step="history loading across overlay suspension";
                Check(surface!.FocusHostTarget("discord.messages.open"),"Messages accepts direct lifecycle-test entry");
                await Button(ControllerButton.A);
                await Until(()=>Node("discord.messages.list")?.IndexedCollection?.Count==2);
                Check(surface.FocusHostTarget("discord.messages.list"),"Conversations accept lifecycle-test entry");
                await Until(()=>Walk(surface).OfType<WidgetIndexedCollectionView>().Any(list=>list.CaptureFocusedItem()?.Index==0));
                await Button(ControllerButton.A);
                await Until(()=>Node("discord.message.history")?.IndexedCollection?.Count==20&&FocusId()=="Widget.discord.message.draft");
                var history=Walk(surface).OfType<WidgetIndexedCollectionView>().Single(list=>AutomationProperties.GetAutomationId(list)=="Widget.discord.message.history");
                var sidebar=Walk(surface).OfType<WidgetIndexedCollectionView>().SingleOrDefault(list=>AutomationProperties.GetAutomationId(list)=="Widget.discord.messages.list");
                var sidebarSource=sidebar?.NativeView.ItemsSource;
                var scroll=Walk(history).OfType<ScrollViewer>().First();
                var lifecycleEvents=new List<string>();
                history.NavigationTrace=message=>{if(lifecycleEvents.Count<500)lifecycleEvents.Add(message);};
                await WidgetPresentationReadiness.WaitAsync(surface,lifetime.Token);
                await Until(()=>!history.HasPendingNativeNavigation&&
                    history.NativeView.ContainerFromIndex(19) is FrameworkElement {IsLoaded:true,ActualHeight:>0} last&&
                    last.TransformToVisual(scroll).TransformBounds(new(0,0,last.ActualWidth,last.ActualHeight)).Top<scroll.ViewportHeight);
                var before=Audit().GetProperty("history").GetArrayLength();
                await ScrollBoundary(-1,before);
                await Until(()=>Audit().GetProperty("history").GetArrayLength()>before);
                Check(Node("discord.message.retry") is null,"The pending synthetic page has no injected retry state");
                if(Environment.GetCommandLineArgs().Contains("--discord-history-suspend-preparation"))
                {
                    await Until(()=>history.HasPendingWindowForValidation);
                    Check(true,"Overlay suspension can interrupt row preparation after the provider page succeeds");
                }
                SetVisible(false);
                await Until(()=>!surface.IsPresentationActive&&activeWidget is {} id&&retainedSurfaces[id].PresentedLifecycle is null);
                Check(!history.HasPendingContinuityForValidation,"Background retirement releases native insertion tracking");
                Check(!history.HasPendingWindowForValidation,"Background retirement drains any prepared successor and range ownership");
                await Task.Delay(700,lifetime.Token);
                SetVisible(true);App.Window!.Activate();await Until(OwnsForeground);
                await Until(()=>MainFocusEnabled&&surface.IsPresentationActive&&!surface.HasPendingMemoryRestore);
                Check(Node("discord.message.retry") is null,"Canceling a pending history request while hidden does not become a loading error on return");
                if(sidebar is not null)Check(ReferenceEquals(sidebarSource,sidebar.NativeView.ItemsSource),
                    "Overlay reopen retains the native conversation source across lifecycle and metadata refresh");
                Check(surface.FocusHostTarget("discord.message.draft"),"Composer remains directly reachable after overlay reopen");
                var reads=Audit().GetProperty("history").GetArrayLength();
                var query=Node("discord.message.history")!.IndexedCollection!.QueryGeneration;
                // A provider page can succeed before hidden preparation is
                // cancelled. On return its retained anchor is then inside a
                // larger window, so drive to the actual edge, not a fixed offset.
                for(var pulse=0;pulse<1200&&Audit().GetProperty("history").GetArrayLength()==reads;pulse++)
                {surface.ScrollBy(0,-18);await Task.Delay(16,lifetime.Token);}
                await Until(()=>Audit().GetProperty("history").GetArrayLength()>reads);
                await Until(()=>Node("discord.message.history")?.IndexedCollection?.QueryGeneration>query||Node("discord.message.retry") is not null);
                Check(Node("discord.message.retry") is null,"The next history request after cancellation succeeds");
                try { await Until(()=>!history.HasPendingContinuityForValidation&&!history.HasPendingWindowForValidation); }
                finally { trace.Add(new{step,lifecycleEvents,continuity=history.ContinuityStateForValidation()});history.NavigationTrace=null; }
                await SaveImage(surface,"history-after-reopen.png");
                Check(Audit().GetProperty("mutationAttempts").GetInt32()==0,"History lifecycle test performs no account mutations");
            }
            async Task HeldHistoryScrollAsync()
            {
                step="held analog scroll across page boundaries";
                using var pipeline=new PresentationPipelineValidationProbe();
                Check(surface!.FocusHostTarget("discord.messages.open"),"Messages accepts direct entry for held-scroll checks");
                await Button(ControllerButton.A);
                await Until(()=>Node("discord.messages.list")?.IndexedCollection?.Count==(largeConversations?120:2));
                Check(surface.FocusHostTarget("discord.messages.list"),"Conversations accept entry for held-scroll checks");
                await Until(()=>Walk(surface).OfType<WidgetIndexedCollectionView>().Any(list=>list.CaptureFocusedItem()?.Index==0));
                await Button(ControllerButton.A);
                await Until(()=>Node("discord.message.history")?.IndexedCollection?.Count==20&&FocusId()=="Widget.discord.message.draft");
                var history=Walk(surface).OfType<WidgetIndexedCollectionView>().Single(list=>AutomationProperties.GetAutomationId(list)=="Widget.discord.message.history");
                await WidgetPresentationReadiness.WaitAsync(surface,lifetime.Token);
                var scroll=Walk(history).OfType<ScrollViewer>().First();
                if(!Environment.GetCommandLineArgs().Contains("--discord-history-held-scroll-during-entry"))
                {
                    await Until(()=>!history.HasPendingNativeNavigation &&
                        history.NativeView.ContainerFromIndex(19) is FrameworkElement {IsLoaded:true,ActualHeight:>0} last &&
                        last.TransformToVisual(scroll).TransformBounds(new(0,0,last.ActualWidth,last.ActualHeight)) is var bounds &&
                        bounds.Top<scroll.ViewportHeight && bounds.Bottom>0);
                    Check(true,"Held scrolling starts after the initial latest-message reveal has settled visibly");
                }
                var directionResults=new List<(int Direction,int Frames,int Wrong,int Commits,int Invalid,int Missing)>();
                await Until(()=>Audit().GetProperty("history").EnumerateArray().Any(read=>read.GetProperty("before").ValueKind==JsonValueKind.String));
                Check(Node("discord.message.history")?.IndexedCollection?.Count==20 && scroll.VerticalOffset>scroll.ViewportHeight*.5,
                    "An older page is fetched before held input reaches the edge without publishing or moving the initial window");
                if(Environment.GetCommandLineArgs().Contains("--discord-history-advance-commit-only"))
                {
                    surface.ScrollBy(0,-1); // Retire the initial reveal before positioning the fixture.
                    scroll.ChangeView(null,scroll.ViewportHeight*.8,null,true);
                    await Task.Delay(300,lifetime.Token);
                    var initialQuery=history.PresentedQueryForValidation;
                    var minimumOffset=scroll.VerticalOffset;var pulses=0;
                    for(;pulses<240&&history.PresentedQueryForValidation==initialQuery&&scroll.VerticalOffset>16;pulses++)
                    {
                        surface.ScrollBy(0,-3);
                        await Task.Delay(16,lifetime.Token);
                        minimumOffset=Math.Min(minimumOffset,scroll.VerticalOffset);
                    }
                    trace.Add(new{step,phase="commit before edge during uninterrupted input",pulses,minimumOffset,initialQuery,
                        committedQuery=history.PresentedQueryForValidation,viewport=scroll.ViewportHeight,offset=scroll.VerticalOffset});
                    Check(history.PresentedQueryForValidation!=initialQuery&&minimumOffset>1,
                        "A prefetched page is inserted before the old edge while right-stick input continues, without waiting for release");
                    Check(Node("discord.message.retry") is null,"Advance insertion does not violate protected-row capacity");
                    return;
                }
                if(Environment.GetCommandLineArgs().Contains("--discord-history-held-scroll-focused-row"))
                {
                    Check(history.Enter(CurrentMessageTarget("1120")),"Held-scroll fixture can start with the latest message focused");
                    await Until(()=>history.CaptureFocusedItem()?.ItemKey=="1120"&&!history.HasPendingNativeNavigation);
                }
                var sidebar=largeConversations?Walk(surface).OfType<WidgetIndexedCollectionView>().Single(list=>AutomationProperties.GetAutomationId(list)=="Widget.discord.messages.list"):null;
                var sidebarScroll=sidebar is not null?Walk(sidebar).OfType<ScrollViewer>().First():null;
                if(sidebarScroll is not null)
                {
                    Check(sidebarScroll.ScrollableHeight>0,"The regression sidebar is genuinely scrollable, like the live conversation catalog");
                    surface.ScrollBy(0,-18);
                    Check(history.ParkPagingFocusForValidation()&&history.IsFocusParked,"The normal page-retirement focus target can own native focus");
                    surface.ScrollBy(0,-18);
                    Check(surface.ScrollOwnerForValidation=="discord.message.history",
                        "Held analog input on parked history focus cannot fall back to the scrollable conversation list");
                }
                var navigationEvents=new List<string>();
                history.NavigationTrace=message=>{if(navigationEvents.Count<2000)navigationEvents.Add(message);};
                using var wheel=Environment.GetCommandLineArgs().Contains("--discord-history-wheel-scroll")?new NativeWheelProbe(scroll):null;
                if(wheel is not null){wheel.MoveToTarget();await Task.Delay(100,lifetime.Token);}
                var reverseWhileMoving=Environment.GetCommandLineArgs().Contains("--discord-history-held-scroll-reverse");
                var directions=reverseWhileMoving?new[]{-1,1,-1}:new[]{-1,1};
                for(var directionIndex=0;directionIndex<directions.Length;directionIndex++)
                {
                    var direction=directions[directionIndex];
                    var before=Audit().GetProperty("history").GetArrayLength();
                    var committedWindows=0;var paintedQuery=history.PresentedQueryForValidation;
                    var frames=0;var wrongDirection=0;var invalidOccurrences=0;var noAnchorFrames=0;var staleAuthorityFrames=0;var escapedScrollFrames=0;double? last=null;var samples=new List<object>();
                    var sidebarOffset=sidebarScroll?.VerticalOffset;
                    var requests=new List<object>();var elapsed=System.Diagnostics.Stopwatch.StartNew();
                    surface.ScrollRequestTrace=(_,requested)=>requests.Add(new{ms=elapsed.ElapsedMilliseconds,
                        current=scroll.VerticalOffset,requested,extent=scroll.ExtentHeight,
                        query=Node("discord.message.history")?.IndexedCollection?.QueryGeneration});
                    void Observe(object? sender,object args)
                    {
                        if(history.PresentedQueryForValidation!=paintedQuery){paintedQuery=history.PresentedQueryForValidation;committedWindows++;}
                        if(sidebarScroll is not null&&(surface.ScrollOwnerForValidation is {} scrollOwner&&scrollOwner!="discord.message.history"||
                            sidebar!.CaptureFocusedItem() is not null||sidebar.IsFocusParked||
                            Math.Abs(sidebarScroll.VerticalOffset-sidebarOffset!.Value)>.5))escapedScrollFrames++;
                        var anchor=history.CaptureViewport(new("discord.root","discord.message.history",ViewNodeKind.IndexedCollection,""));
                        if(anchor is null||!int.TryParse(anchor.Key,out var key))
                        {
                            noAnchorFrames++;
                            if(noAnchorFrames<=8)trace.Add(new{step,phase="missing reading anchor",direction,ms=elapsed.ElapsedMilliseconds,
                                pipelineMs=pipeline.ElapsedMilliseconds,
                                query=Node("discord.message.history")?.IndexedCollection?.QueryGeneration,
                                continuity=history.ContinuityStateForValidation(),navigation=history.NavigationDiagnosticState(),
                                resources=history.PerformanceStatistics()});
                            return;
                        }
                        if(anchor.Index<0||history.PresentedOccurrenceKeysForValidation?.ElementAtOrDefault(anchor.Index)!=anchor.Key||
                            history.PresentedQueryForValidation!=anchor.Query)invalidOccurrences++;
                        if(history.HasPendingWindowForValidation&&Node("discord.message.history") is {IndexedCollection:{} published,IndexedOccurrenceKeys:{} latestKeys}&&
                            anchor.Query!=published.QueryGeneration&&history.PresentedOccurrenceKeysForValidation is {} paintedKeys&&
                            !paintedKeys.SequenceEqual(latestKeys.Take(paintedKeys.Count),StringComparer.Ordinal)&&history.HasCurrentPresentedRowsForValidation)
                            staleAuthorityFrames++;
                        // A leading gap can give the next row a negative clipped
                        // fraction. Fractions of differently sized rows are not
                        // comparable across that gap; keep the ordinal metric
                        // inside the row while retaining raw geometry below.
                        var position=key+Math.Clamp(anchor.ClippedFraction,0,1);
                        if(last is {} previous&&(position-previous)*direction < -.08)wrongDirection++;
                        last=position;frames++;
                        if(samples.Count<5000)samples.Add(new{ms=elapsed.ElapsedMilliseconds,key,anchor.Index,anchor.ClippedFraction,position,query=anchor.Query,
                            publishedQuery=Node("discord.message.history")?.IndexedCollection?.QueryGeneration,
                            preparing=history.HasPendingWindowForValidation,
                            declaredKey=history.PresentedOccurrenceKeysForValidation?.ElementAtOrDefault(anchor.Index),
                            firstDeclaredKey=history.PresentedOccurrenceKeysForValidation?.FirstOrDefault(),
                            lastDeclaredKey=history.PresentedOccurrenceKeysForValidation?.LastOrDefault(),
                            offset=scroll.VerticalOffset,extent=scroll.ExtentHeight});
                    }
                    CompositionTarget.Rendering+=Observe;
                    try
                    {
                        // Exercise eviction, not just the first prepend. Row
                        // height and native animation cadence vary by layout;
                        // a fixed number of pulses may never reach that edge.
                        var requiredReads=directionIndex==0?2:1;
                        var crossedAt=-1;
                        for(var pulse=0;pulse<1600;pulse++)
                        {
                            if(wheel is not null)wheel.Send(-direction*120);
                            else surface.ScrollBy(0,direction*18);
                            await Task.Delay(16,lifetime.Token);
                            if(escapedScrollFrames>0)break;
                            var completedReads=Audit().GetProperty("history").EnumerateArray().Skip(before).Count(read=>
                                read.GetProperty(direction<0?"before":"after").ValueKind==JsonValueKind.String);
                            if(committedWindows>=requiredReads&&crossedAt<0)crossedAt=pulse;
                            if(pulse>=420&&crossedAt>=0&&pulse-crossedAt>=90)break;
                        }
                        // Delayed layout/native animation must not spring back
                        // after input release either.
                        if(!reverseWhileMoving||directionIndex==directions.Length-1)await Task.Delay(600,lifetime.Token);
                    }
                    finally{CompositionTarget.Rendering-=Observe;surface.ScrollRequestTrace=null;}
                    var reads=Audit().GetProperty("history").EnumerateArray().Skip(before).Count(read=>
                        read.GetProperty(direction<0?"before":"after").ValueKind==JsonValueKind.String);
                    trace.Add(new{step,direction,frames,wrongDirection,reads,invalidOccurrences,noAnchorFrames,staleAuthorityFrames,escapedScrollFrames,
                        focus=FocusId(),scrollOwner=surface.ScrollOwnerForValidation,sidebarOffset,sidebarAfter=sidebarScroll?.VerticalOffset,samples,requests});
                    Check(escapedScrollFrames==0,"Held paging keeps focus and scroll ownership out of the conversation sidebar");
                    Check(staleAuthorityFrames==0,"Prepared window paint never revives obsolete row action authority");
                    Check(committedWindows>=(directionIndex==0?2:1),"Held scrolling commits repeated page windows rather than only issuing speculative reads");
                    directionResults.Add((direction,frames,wrongDirection,committedWindows,invalidOccurrences,noAnchorFrames));
                }
                history.NavigationTrace=null;trace.Add(new{step,navigationEvents,pipelineEvents=pipeline.Events});
                await Until(()=>!history.HasPendingContinuityForValidation&&!history.HasPendingWindowForValidation);
                Check(true,"Native insertion anchoring retires after held input and delayed row layout settle");
                if(wheel is null&&Environment.GetCommandLineArgs().Contains("--discord-history-held-scroll-focused-row"))
                {
                    var beforeSettle=history.CaptureViewport(new("discord.root","discord.message.history",ViewNodeKind.IndexedCollection,""));
                    await Until(surface.SettleScrollFocus);
                    var afterSettle=history.CaptureViewport(new("discord.root","discord.message.history",ViewNodeKind.IndexedCollection,""));
                    Check(history.CaptureFocusedItem() is not null&&beforeSettle is not null&&afterSettle?.Key==beforeSettle.Key&&
                        Math.Abs(afterSettle.ClippedFraction-beforeSettle.ClippedFraction)<.02,
                        "Releasing held scroll restores visible message focus without moving the reading position");
                }
                foreach(var result in directionResults)
                {
                    Check(result.Invalid==0,"Held "+(result.Direction<0?"up":"down")+" viewport never reports a removed occurrence as current");
                    Check(result.Missing==0,"Held "+(result.Direction<0?"up":"down")+" retains a readable anchor on every sampled frame");
                    Check(result.Commits>0,"Held "+(result.Direction<0?"up":"down")+" commits a fetched history page, including a previously prepared opposite edge");
                    Check(result.Frames>0&&result.Wrong==0,"Held "+(result.Direction<0?"up":"down")+" never restores behind newer input on sampled frames");
                }
                Check(Audit().GetProperty("mutationAttempts").GetInt32()==0,"Bidirectional scroll test performs no account mutations");
            }
            async Task HistoryContinuityAsync()
            {
                step="history transition continuity";
                Check(surface!.FocusHostTarget("discord.messages.open"),"Messages can be opened directly");
                await Button(ControllerButton.A);
                await Until(()=>Node("discord.messages.list")?.IndexedCollection?.Count==2);
                Check(surface.FocusHostTarget("discord.messages.list"),"Conversation list accepts entry");
                await Until(()=>Walk(surface).OfType<WidgetIndexedCollectionView>().Any(list=>list.CaptureFocusedItem()?.Index==0));
                await Button(ControllerButton.A);
                await Until(()=>Node("discord.message.history")?.IndexedCollection?.Count==20&&FocusId()=="Widget.discord.message.draft");
                var history=Walk(surface).OfType<WidgetIndexedCollectionView>().Single(list=>AutomationProperties.GetAutomationId(list)=="Widget.discord.message.history");
                var scroll=Walk(history).OfType<ScrollViewer>().First();
                await WidgetPresentationReadiness.WaitAsync(surface,lifetime.Token);
                for(var pass=0;pass<3;pass++)
                {
                    await CheckOlderPageAsync(history,scroll,Audit().GetProperty("history").GetArrayLength());
                    Check(Node("discord.message.history")!.IndexedCollection!.Count<=40,"Repeated prepend keeps the history window bounded");
                }
                Check(Audit().GetProperty("mutationAttempts").GetInt32()==0,"Transition checks perform no account mutations");
            }
            async Task CheckOlderPageAsync(WidgetIndexedCollectionView history,ScrollViewer historyScroll,int priorReads)
            {
                var generation = Node("discord.message.history")!.IndexedCollection!.QueryGeneration;
                var pagingItems = history.NativeView.ItemsSource;
                var pagingFrames = 0;
                var replacedPagingFrames = 0;
                var blankPagingFrames = 0;
                IndexedItem<WidgetIndexedRow>? pagingAnchor = null;
                FrameworkElement? pagingContainer = null;
                double pagingTop = 0;
                var anchorReplacements = 0;
                var anchorMaxDrift = 0d;
                void SamplePaging(object? sender, object args)
                {
                    pagingFrames++;
                    if (!ReferenceEquals(pagingItems, history.NativeView.ItemsSource)) replacedPagingFrames++;
                    if (!Walk(history).OfType<WidgetIndexedRowView>().Any(row => row.IsLoaded && row.ActualHeight > 0 && row.Content is not null)) blankPagingFrames++;
                    if (pagingAnchor is not null)
                    {
                        if (!ReferenceEquals(history.NativeView.ContainerFromIndex(pagingAnchor.Index), pagingContainer)) anchorReplacements++;
                        else anchorMaxDrift = Math.Max(anchorMaxDrift, Math.Abs(pagingContainer!.TransformToVisual(historyScroll).TransformPoint(new(0,0)).Y-pagingTop));
                    }
                }
                try
                {
                await ScrollBoundary(-1,priorReads);
                pagingAnchor = Enumerable.Range(0, history.NativeView.Items.Count).Select(index => history.NativeView.Items[index])
                    .OfType<IndexedItem<WidgetIndexedRow>>().FirstOrDefault(slot =>
                        history.NativeView.ContainerFromIndex(slot.Index) is FrameworkElement {IsLoaded:true} row &&
                        row.TransformToVisual(historyScroll).TransformBounds(new(0,0,row.ActualWidth,row.ActualHeight)) is var bounds &&
                        bounds.Bottom > 1 && bounds.Top < historyScroll.ViewportHeight);
                pagingContainer = pagingAnchor is null ? null : history.NativeView.ContainerFromIndex(pagingAnchor.Index) as FrameworkElement;
                if (pagingContainer is not null) pagingTop = pagingContainer.TransformToVisual(historyScroll).TransformPoint(new(0,0)).Y;
                Check(Node("discord.message.history")!.IndexedCollection!.QueryGeneration == generation,
                    "Frame sampling begins before the delayed older-page publication");
                // Deliberate test teleporting to the edge is not the page-load
                // transition. Start sampling its now-visible reader before the
                // delayed response, and retain every frame through publication.
                CompositionTarget.Rendering += SamplePaging;
                await SaveImage(surface,"older-loading-"+priorReads+".png");
                await Until(() => Node("discord.message.history")!.IndexedCollection!.QueryGeneration > generation && Audit().GetProperty("history").GetArrayLength()>priorReads);
                await Task.Delay(250,lifetime.Token);
                }
                finally { CompositionTarget.Rendering -= SamplePaging; }
                trace.Add(new { step, pagingFrames, replacedPagingFrames, blankPagingFrames, anchorReplacements, anchorMaxDrift });
                Check(pagingFrames > 0 && replacedPagingFrames == 0 && blankPagingFrames == 0,
                    "Older-history loading retains its native items source and painted rows on every sampled frame");
                Check(pagingAnchor is not null && anchorReplacements == 0 && anchorMaxDrift < 2,
                    "Older-history prepend preserves the visible native container and its position on every sampled frame");
                await SaveImage(surface,"older-loaded-"+priorReads+".png");
            }
            async Task ScrollBoundary(int direction,int previousReads)
            {
                // Model held stick input. A single oversized delta only reaches
                // the current estimated extent; long virtualized rows can enlarge
                // it after that input has already completed.
                var pulses=0;
                int AdjacentReads()=>Audit().GetProperty("history").EnumerateArray().Skip(previousReads).Count(read=>
                    read.GetProperty(direction<0?"before":"after").ValueKind==JsonValueKind.String &&
                    read.GetProperty("around").ValueKind==JsonValueKind.Null);
                while(AdjacentReads()==0 && pulses<64)
                {
                    surface!.ScrollBy(0,direction*100000);pulses++;
                    await Task.Delay(40,lifetime.Token);
                }
                trace.Add(new{step,phase="held boundary input",direction,pulses,previousReads,
                    reads=Audit().GetProperty("history").GetArrayLength(),adjacentReads=AdjacentReads()});
                Check(AdjacentReads()==1,
                    "Held scrolling admits exactly one adjacent history request at the realized edge");
            }
            async Task Button(ControllerButton button)
            {
                await Until(() => MainFocusEnabled); await RouteButtonAsync(button, ControllerEventPhase.Pressed); await RouteButtonAsync(button, ControllerEventPhase.Released);
                trace.Add(new { step, input = button.ToString(), focus = FocusId() });
            }
            async Task Move(NavigationDirection direction)
            {
                await Until(() => MainFocusEnabled); var sample = Sample(); sample.DpadNavigation = new() { Direction = direction, Phase = NavigationPhase.Pressed };
                Receive(sample); Receive(Sample()); await Task.Delay(80, lifetime.Token);
            }
        };
        static IEnumerable<ViewNode> Nodes(ViewNode node) => new[] { node }.Concat(node.Children.SelectMany(Nodes));
        static IEnumerable<DependencyObject> Walk(DependencyObject node)
        { yield return node; for (var index = 0; index < VisualTreeHelper.GetChildrenCount(node); ++index) foreach (var child in Walk(VisualTreeHelper.GetChild(node, index))) yield return child; }
    }
}
