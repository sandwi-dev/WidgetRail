using System.Runtime.InteropServices;
using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using WidgetRail.OverlayFrontend.WinUI.Previews;
using WidgetRail.PlatformBroker;
using WidgetRail.WidgetProtocol;

namespace WidgetRail.OverlayFrontend.WinUI.Shell;

internal sealed partial class OverlayShellPage
{
    internal void EnableGameHelpValidation(string path, Action reopen, Func<bool> nativeHideCompleted)
    {
        var folder = Path.GetDirectoryName(Path.GetFullPath(path))!;
        Directory.CreateDirectory(folder);
        var sourceArgument = FrontendArguments.Value(Environment.GetCommandLineArgs(), "--capture-fixture-window");
        if (!long.TryParse(sourceArgument, out var sourceHandle) || sourceHandle == 0)
            throw new InvalidOperationException("Game Help validation requires a disposable external capture fixture window.");
        var handle = (nint)sourceHandle;
        var identity = new NativePreviewTarget { Size = (uint)Marshal.SizeOf<NativePreviewTarget>(), Version = 1 };
        Marshal.ThrowExceptionForHR(PreviewNative.ReadIdentity((ulong)handle, ref identity));
        if (identity.ProcessId == (uint)Environment.ProcessId)
            throw new InvalidOperationException("Capture fixture must be outside the overlay process.");
        Loaded += async (_, _) =>
        {
            var checks = new List<string>(); string? error = null;
            try
            {
                if (startup is not null) await startup;
                await Until(() => activeWidget == "game-help" && interactive && foreground && Has("help.composer"));
                Check(!Has("help.application"), "Full-trust Game Help initializes without an application picker or window-list capability");
                var rejectedSelf = false;
                try { _ = WidgetRail.OverlayFrontend.WinUI.Capture.NativeWindowCapture.ForegroundTarget(); }
                catch (InvalidOperationException) { rejectedSelf = true; }
                Check(rejectedSelf, "Foreground selection rejects WidgetRail's own window");
                await Act("settings", "help.settings");
                await Until(() => Has("help.grounding"));
                await Act("grounding.on", "help.grounding");
                await RouteButtonAsync(ControllerButton.B, ControllerEventPhase.Pressed);
                await RouteButtonAsync(ControllerButton.B, ControllerEventPhase.Released);
                await Until(() => Has("help.composer") && !Has("help.grounding"));
                await Act("starter.0", "help.starter.0");
                await Until(() => Has("help.capture-image"));
                await Act("capture.image", "help.capture-image");
                await Until(() => hostChoiceDialog is not null && Find(hostChoiceDialog, "Host.Choice.Primary") is Control);
                Check(hostChoiceDialog!.Title?.ToString() == "Capture game context?", "Capture request reaches host confirmation");
                hostChoiceDialog.ControllerArmed = true;
                hostChoiceDialog.Handle(ControllerButton.B, ControllerEventPhase.Pressed);
                await Until(() => hostChoiceDialog is null && !Has("help.capture-cancel"));
                Check(visible && !Has("help.preview-send"), "Cancelling host confirmation keeps overlay open and creates no preview");
                foreach (var video in new[] { false, true })
                {
                    await Act("context.open", "help.context");
                    await Until(() => Has("help.add-image"));
                    await Act(video ? "capture.video" : "capture.image", video ? "help.add-video" : "help.add-image");
                    await Until(() => hostChoiceDialog is not null && Find(hostChoiceDialog, "Host.Choice.Primary") is Control);
                    ((Control)Find(hostChoiceDialog, "Host.Choice.Primary")!).Focus(FocusState.Keyboard);
                    hostChoiceDialog!.ControllerArmed = true;
                    hostChoiceDialog.Handle(ControllerButton.A, ControllerEventPhase.Pressed);
                    // Logical visibility changes at the start of the close transition.
                    // Moving foreground then would cancel the release-aware handoff.
                    await Until(() => !visible && nativeHideCompleted());
                    WinUIEx.HwndExtensions.SetForegroundWindow(handle);
                    await Until(() => capturing is { IsCompleted: false });
                    var started = Environment.TickCount64;
                    await Until(() => capturing is { IsCompleted: true }, 25000);
                    Check(Environment.TickCount64 - started >= 4500, "Capture waits for the preparation countdown before completing");
                    Check(!visible, "Completed capture does not reopen the overlay automatically");
                    reopen();
                    await Until(() => visible && foreground && Has("help.preview-send"));
                    var attachment = Nodes(surface!.CurrentBinding!.View.Root).Single(node => node.Kind == ViewNodeKind.MediaPlayer).MediaPlayer!.Source.Attachment!;
                    Check(attachment.ContentType == (video ? "video/mp4" : "image/png"), "Completed capture returns through the worker into the native preview modal");
                    await Task.Delay(300);
                    if (!video)
                    {
                        await Act("preview.discard", "help.preview-discard");
                        await Until(() => !Has("help.preview-send"));
                        Check(true, "Discard returns to the pending question without sending it");
                    }
                    else
                    {
                        await Act("preview.send", "help.preview-send");
                        await Until(() => Nodes(surface!.CurrentBinding!.View.Root).Any(n => n.Text == "Synthetic answer: take the green path."));
                        Check(true, "Explicit Send resolves owned clip bytes inside the application-owned fake provider and renders its structured answer");
                        Check(Nodes(surface.CurrentBinding.View.Root).Any(n => n.Text == "What is on the green path?"), "Structured follow-up appears inside the transcript");
                        await Until(() => FindBrowser(surface) is not null);
                        await Until(() => Find(surface, "Widget.help.transcript") is ScrollViewer { ScrollableHeight: > 0 });
                        surface.ScrollBy(0, 2000); // Real user scrolling cancels pending authored reveals.
                        FindBrowser(surface)!.StartBringIntoView();
                        FindBrowser(surface)!.Focus(FocusState.Keyboard);
                        await Until(() => FindBrowser(surface) is { Surface.IsReady: true });
                        var slot = FindBrowser(surface)!;
                        var browser = slot.Surface!;
                        await Until(() => !browser.IsNavigationInProgress);
                        Check(await browser.EvaluateFixtureAsync("typeof window.providerScriptRan === 'undefined'") == "true", "Original provider markup is displayed with site scripts disabled");
                        Check(await browser.EvaluateFixtureAsync("document.querySelector('img').naturalWidth") == "0", "External image content cannot load inside attribution");
                        string? opened = null;
                        browser.OpenExternal = (uri, _) => { opened = uri.AbsoluteUri; return Task.FromResult(true); };
                        slot.Focus(FocusState.Keyboard); slot.Enter();
                        Check(browser.IsBrowsing, "Attribution supports explicit controller interaction");
                        browser.HandleButton(ControllerButton.Y, ControllerEventPhase.Pressed);
                        await Task.Delay(100);
                        Check(!browser.HasDialog, "Attribution cannot be repurposed as an address editor");
                        browser.HandleButton(ControllerButton.A, ControllerEventPhase.Pressed);
                        browser.HandleButton(ControllerButton.A, ControllerEventPhase.Released);
                        await Until(() => opened is not null);
                        Check(opened == "https://www.google.com/search?q=fixture" && browser.CurrentUrl == browser.ProviderReference!.Url,
                            "Attribution link preserves the exact destination without navigating its restricted document");
                        browser.HandleButton(ControllerButton.B, ControllerEventPhase.Pressed);
                        browser.HandleButton(ControllerButton.B, ControllerEventPhase.Released);
                        Check(!browser.IsInteracting && Has("help.composer"), "B leaves attribution interaction and preserves the chat");
                        for (var index = 0; index < 6; index++)
                        {
                            var expected = index + 2;
                            await Act("settings", "help.settings");
                            await Until(() => Has("help.retry"));
                            await Act("answer.retry", "help.retry");
                            await Until(() => Nodes(surface.CurrentBinding!.View.Root).Count(node => node.WebBrowser?.ProviderDocument is not null) == expected);
                        }
                        await Task.Delay(300);
                        Check(browserOwner!.CreatedCount < 7 && browserOwner.ResidentCount <= 4,
                            "Offscreen transcript attributions do not eagerly create native browsers");
                        var last = BrowserSlots(surface).Last();
                        last.StartBringIntoView(); last.Focus(FocusState.Keyboard);
                        await Until(() => last.Surface is { IsReady: true } && last.CanDisplay);
                        Check(browserOwner.ResidentCount <= 4, "Scrolling to another attribution respects the native browser budget");
                        Check(!Has("help.message.2.solution"), "Detailed solution starts collapsed in the chat");
                        await Act("solution.2", "help.message.2.solution-toggle");
                        await Until(() => Has("help.message.2.solution"));
                        await Act("solution.2", "help.message.2.solution-toggle");
                        await Until(() => !Has("help.message.2.solution"));
                        Check(true, "Solution expands and collapses inside its original message");
                        var transcript = (ScrollViewer?)Find(surface, "Widget.help.transcript")
                            ?? throw new InvalidOperationException("Transcript viewport missing.");
                        transcript.ChangeView(null, 0, null, disableAnimation: true);
                        await Until(() => transcript.VerticalOffset < 1);
                        await Act("suggest.2.0", "help.message.2.suggest.0");
                        await Until(() => surface.CurrentBinding!.View.ScrollRevealRequest is { } reveal &&
                            Find(surface, "Widget." + reveal.TargetId) is FrameworkElement target &&
                            target.IsLoaded && transcript.VerticalOffset > 0 &&
                            target.TransformToVisual(transcript).TransformPoint(new(0, 0)).Y is >= -1 &&
                            target.TransformToVisual(transcript).TransformPoint(new(0, 0)).Y < transcript.ViewportHeight);
                        Check(true, "Selecting a suggested reply reveals the new user turn in the transcript");
                        await Act("settings", "help.settings");
                        await Until(() => Has("help.new"));
                        await Act("new", "help.new");
                        await Until(() => Has("help.new-proceed"));
                        await Act("new.confirm", "help.new-proceed");
                        await Until(() => Has("help.starter.0") && browserOwner.ResidentCount == 0);
                        Check(true, "New chat retires native attribution instances and restores ordinary widget controls");


                    }
                }
            }
            catch (Exception failure)
            {
                error = failure.ToString();
                await File.WriteAllTextAsync(path + ".dialog.json", JsonSerializer.Serialize(new
                {
                    visible, foreground, interactive,
                    dialog = hostChoiceDialog is { } dialog ? new { dialog.IsLoaded, dialog.Visibility, dialog.PrimaryButtonText,
                        children = Describe(dialog).ToArray() } : null,
                    popups = VisualTreeHelper.GetOpenPopupsForXamlRoot(XamlRoot).Select(p => new { p.IsOpen, children = Describe(p.Child).ToArray() }).ToArray()
                }));
            }
            await File.WriteAllTextAsync(path, JsonSerializer.Serialize(new { passed = error is null, checks, error }));
            bool Has(string id) => surface?.CurrentBinding is { } binding && Nodes(binding.View.Root).Any(node => node.Id == id);
            async Task Act(string action, string element)
            {
                var frame = surface!.CurrentBinding!.Frame;
                await InvokeAsync(new(frame, new(action, element, ControllerButton.A, InputScopeId: frame.Authority.ActiveInputScopeId)));
                await RouteButtonAsync(ControllerButton.A, ControllerEventPhase.Released);
            }
            void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); checks.Add(message); }
        };
        static IEnumerable<Browser.BrowserSlot> BrowserSlots(DependencyObject? node)
        {
            if (node is null) yield break;
            if (node is Browser.BrowserSlot slot && slot.IsProviderDocument) yield return slot;
            for (var i = 0; i < VisualTreeHelper.GetChildrenCount(node); i++)
                foreach (var child in BrowserSlots(VisualTreeHelper.GetChild(node, i))) yield return child;
        }
        static Browser.BrowserSlot? FindBrowser(DependencyObject? node)
        {
            if (node is null) return null;
            if (node is Browser.BrowserSlot slot && slot.IsProviderDocument) return slot;
            for (var i = 0; i < VisualTreeHelper.GetChildrenCount(node); i++) if (FindBrowser(VisualTreeHelper.GetChild(node, i)) is { } child) return child;
            return null;
        }
        static IEnumerable<ViewNode> Nodes(ViewNode node) => new[] { node }.Concat(node.Children.SelectMany(Nodes));
        static IEnumerable<string> Describe(DependencyObject? node)
        {
            if (node is null) yield break;
            yield return node.GetType().Name + ":" + AutomationProperties.GetAutomationId(node) + ":" + (node as FrameworkElement)?.Name;
            for (var i = 0; i < VisualTreeHelper.GetChildrenCount(node); i++)
                foreach (var child in Describe(VisualTreeHelper.GetChild(node, i))) yield return child;
        }
        static DependencyObject? Find(DependencyObject? node, string id)
        {
            if (node is null) return null;
            if (AutomationProperties.GetAutomationId(node) == id) return node;
            for (var i = 0; i < VisualTreeHelper.GetChildrenCount(node); i++) if (Find(VisualTreeHelper.GetChild(node, i), id) is { } child) return child;
            return null;
        }
        static async Task Until(Func<bool> condition, int milliseconds = 15000)
        {
            var end = Environment.TickCount64 + milliseconds;
            while (!condition()) { if (Environment.TickCount64 >= end) throw new TimeoutException("Game Help fixture condition timed out."); await Task.Delay(25); }
        }
    }
}
