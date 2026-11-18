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
                App.Window!.Activate(); await Until(OwnsForeground);
                surface!.FocusTrace = message => { if (trace.Count < 500) trace.Add(new { step, focus = message }); };
                using var expectedSurface = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "expected-surface.json")));
                var expectedWidth = expectedSurface.RootElement.GetProperty("width").GetDouble();
                var expectedHeight = expectedSurface.RootElement.GetProperty("height").GetDouble();
                Check(Math.Abs(surface.ActualWidth - expectedWidth) < 1 && Math.Abs(surface.ActualHeight - expectedHeight) < 1,
                    $"Messages fixture uses the requested {expectedWidth}x{expectedHeight} DIP surface (actual {surface.ActualWidth}x{surface.ActualHeight})");
                Check(Audit().GetProperty("history").GetArrayLength() == 0, "Call page does not eagerly load message history");

                step = "controller inbox";
                Check(surface.FocusHostTarget("discord.messages.open"), "Native Messages entry is focusable");
                await Button(ControllerButton.A);
                await Until(() => Node("discord.conversation.10") is not null && FocusId() == "Widget.discord.messages.search");
                Check(surface.CurrentBinding!.Frame.Authority.ActiveInputScopeId == "discord.messages.scope", "Inbox owns the Messages input scope");
                Check(Audit().GetProperty("history").GetArrayLength() == 0, "Inbox reads conversations without eagerly fetching their histories");
                for (var attempt = 0; attempt < 8 && FocusId() != "Widget.discord.conversation.10"; ++attempt) await Move(NavigationDirection.Down);
                Check(FocusId() == "Widget.discord.conversation.10", "D-pad reaches an existing conversation from inbox search");
                await Button(ControllerButton.A);
                await Until(() => Node("discord.message.history")?.IndexedCollection?.Count == 20 && FocusId() == "Widget.discord.message.draft");
                Check(surface.CurrentBinding!.Frame.Authority.ActiveInputScopeId == "discord.conversation.scope", "Opening a conversation establishes the matching native input scope");
                var audit = Audit();
                Check(audit.GetProperty("history").GetArrayLength() == 1 && audit.GetProperty("history")[0].GetProperty("limit").GetInt32() == 20,
                    "Conversation history loads one bounded 20-message page lazily");
                Check(InSurface(Find("discord.message.draft")!) && InSurface(Find("discord.message.send")!), "Composer and Send are within the actual widget viewport");
                Check(Node("discord.message.send")!.IsDisabled == true, "An empty composer cannot send");

                if(directionalEntryOnly)
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
                var fixedControls = new[] { "discord.messages.back", "discord.message.options", "discord.message.draft", "discord.message.attach", "discord.message.send" }
                    .Select(Find).Where(element => element is not null && Displayed(element)).Cast<FrameworkElement>().ToArray();
                Check(fixedControls.Length >= 5 && fixedControls.All(InSurface), "All displayed non-scrolling conversation controls stay inside the widget client");
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
                await Until(()=>history.CaptureFocusedItem() is {ItemKey:"1111"} focused && history.NativeView.ContainerFromIndex(focused.Index) is FrameworkElement {IsLoaded:true});
                Check(((FrameworkElement)history.NativeView.ContainerFromIndex(history.CaptureFocusedItem()!.Index)).ActualHeight<72,
                    "A short message is content-sized below the former 72-DIP tile minimum");
                await SaveImage(surface,"conversation.png");
                Check(history.Enter(CurrentMessageTarget("1120")), "Native history group admits controller entry at the retained newest message");
                await Until(() => history.CaptureFocusedItem() is not null && surface.IsFocusPreviewOpen);
                var rowFocus = FocusId();
                await Button(ControllerButton.A);
                await Until(() => surface.IsPreviewEnteredForValidation && AttachmentPreviewReady());
                Check(AttachmentPreviewReady(),
                    "Controller A enters attachment details without moving message text out of the timeline");
                await Button(ControllerButton.B);
                await Until(() => !surface.IsPreviewEnteredForValidation && FocusId() == rowFocus);
                Check(Node("discord.message.page") is not null, "Preview B restores the exact history row without leaving the conversation");

                step = "keyboard preview";
                NativeContextProbeInput.PressKey(VirtualKey.F6);
                await Until(() => surface.IsPreviewEnteredForValidation);
                NativeContextProbeInput.Escape();
                await Until(() => !surface.IsPreviewEnteredForValidation && FocusId() == rowFocus);
                Check(true, "Native F6 and Escape enter and leave the same message preview");

                step = "mouse preview";
                Check(surface.FocusHostTarget("discord.message.draft") && history.Enter(CurrentMessageTarget("1120")), "Message preview can be reopened after keyboard dismissal");
                await Until(() => surface.IsFocusPreviewOpen && history.CaptureFocusedItem() is not null);
                var row = FocusManager.GetFocusedElement(XamlRoot) as FrameworkElement ?? throw new InvalidOperationException("History source is not focused.");
                var visiblePoint=historyScroll.TransformToVisual(row).TransformPoint(new(historyScroll.ActualWidth/2,historyScroll.ActualHeight/2));
                Check(visiblePoint.X>=0 && visiblePoint.X<row.ActualWidth && visiblePoint.Y>=0 && visiblePoint.Y<row.ActualHeight,
                    "Mouse source point lies in the visible portion of the focused oversized message");
                await NativeContextProbeInput.HoverTransitAsync(historyScroll,
                    () => Walk(surface.FocusPreviewChrome!).OfType<ScrollViewer>().First(),
                    () => Until(() => surface.IsFocusPreviewOpen),
                    async () =>
                    {
                        var scroll = Walk(surface.FocusPreviewChrome!).OfType<ScrollViewer>().First();
                        await NativeContextProbeInput.LeftClickAsync(scroll, new Point(scroll.ActualWidth / 2, scroll.ActualHeight / 2));
                        Check(surface.IsFocusPreviewOpen, "Pointer transit and clicking attachment metadata preserve the preview");
                    });
                NativeContextProbeInput.Escape();
                await Until(() => !surface.IsPreviewEnteredForValidation);

                step = "bounded paging";
                await Button(ControllerButton.LeftStick);
                await Until(()=>FocusId()=="Widget.discord.messages.back");
                Check(true,"L3 reaches conversation header through the shared exact-focus request");
                await Button(ControllerButton.X);
                await Until(()=>FocusId()=="Widget.discord.message.draft");
                Check(surface.ControllerTextEntry is null,"X focuses composer without sending or implicitly opening keyboard");
                var priorReads=Audit().GetProperty("history").GetArrayLength();
                await ScrollBoundary(-1,priorReads);
                await Until(() => Node("discord.message.history")?.IndexedCollection?.Count == 40 && Audit().GetProperty("history").GetArrayLength()>priorReads);
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
                Check(Audit().GetProperty("mutationAttempts").GetInt32() == 0, "Inbox, previews, paging and composition produce no provider mutation or upload");
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
                await Until(()=>Node("discord.message.status")?.Text?.StartsWith("Upload cancelled.",StringComparison.Ordinal)==true && Node("discord.message.attach") is not null);
                Check(Node("discord.message.file.0") is not null && Node("discord.message.draft")?.TextEntryValue?.ToLowerInvariant()=="hi",
                    "Native upload cancellation keeps the selected file and composed draft");
                Check(Audit().GetProperty("uploads").GetInt32()==1 && Audit().GetProperty("uploadCancellations").GetInt32()==1 && Audit().GetProperty("mutationAttempts").GetInt32()==0,
                    "The synthetic stream cancels exactly once with no provider send or real network upload");
                step = "conversation return";
                await Button(ControllerButton.B);
                await Until(() => Node("discord.messages.page") is not null && FocusId() == "Widget.discord.conversation.10");
                Check(true, "Conversation Back restores its invoking inbox row");
                step = "dashboard presentation";
                await Button(ControllerButton.B);
                await Until(() => Node("discord.call.page") is not null && MainFocusEnabled);
                await Button(ControllerButton.B);
                await Until(() => !interactive && !switching && !trayGuide.HasPendingHints && FocusedTrayWidget()?.Id == "widgetrail.tests.discord-ui");
                Check(RadialOpen, "Root Back opens the host radial chooser");
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
                    guidePublished = trayGuide.PublishedHintsForValidation.Select(hint => new { hint.Button, hint.Label }).ToArray(),
                    guideDisplayed = trayGuide.DisplayedHints.Select(hint => new { hint.Button, hint.Label }).ToArray() });
                failure = error.ToString();
            }
            finally { if (surface is not null) surface.FocusTrace = null; }
            Directory.CreateDirectory(root);
            await File.WriteAllTextAsync(path, JsonSerializer.Serialize(new { passed = failure is null, composerOnly, checks, step, error = failure, trace, native = surface?.FocusDiagnostics() }));

            JsonElement Audit() { using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "messages-provider.json"))); return document.RootElement.Clone(); }
            void Check(bool condition, string label) { if (!condition) throw new InvalidOperationException(label); checks.Add(label); }
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
            bool AttachmentPreviewReady() => surface?.FocusPreviewChrome is { IsLoaded: true } chrome && Walk(chrome).OfType<TextBlock>()
                .Any(text => text.Text.StartsWith("fixture-1.txt", StringComparison.Ordinal));
            async Task SaveImage(UIElement element,string name)
            {
                var bitmap=new Microsoft.UI.Xaml.Media.Imaging.RenderTargetBitmap();await bitmap.RenderAsync(element);
                using var stream=new Windows.Storage.Streams.InMemoryRandomAccessStream();
                var encoder=await Windows.Graphics.Imaging.BitmapEncoder.CreateAsync(Windows.Graphics.Imaging.BitmapEncoder.PngEncoderId,stream);
                encoder.SetPixelData(Windows.Graphics.Imaging.BitmapPixelFormat.Bgra8,Windows.Graphics.Imaging.BitmapAlphaMode.Premultiplied,
                    (uint)bitmap.PixelWidth,(uint)bitmap.PixelHeight,96,96,(await bitmap.GetPixelsAsync()).ToArray());
                await encoder.FlushAsync();stream.Seek(0);using var output=File.Create(Path.Combine(root,name));await stream.AsStreamForRead().CopyToAsync(output);
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
            async Task Until(Func<bool> condition)
            {
                var deadline = Environment.TickCount64 + 15000;
                while (!condition())
                {
                    if (validationFailure is not null || Environment.TickCount64 > deadline) throw new InvalidOperationException("Messages did not settle: " + step + "; focus=" + FocusId(), validationFailure);
                    await Task.Delay(20, lifetime.Token);
                }
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
