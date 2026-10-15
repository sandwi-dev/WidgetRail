using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using WidgetRail.OverlayFrontend.WinUI.Shell;
using WidgetRail.PlatformBroker;
using WidgetRail.WidgetBridge;
using WidgetRail.WidgetProtocol;

namespace WidgetRail.OverlayFrontend.WinUI.Validation;

internal sealed partial class PermissionDialogValidationPage : Page, IAsyncDisposable
{
    private HostChoiceDialog? active;
    private PinnedWidgetWindow? pin;
    private readonly CancellationTokenSource lifetime = new();
    internal PermissionDialogValidationPage(string output)
    {
        var status = new TextBlock { Text = "Synthetic permission dialog checks", TextWrapping = TextWrapping.Wrap };
        Content = status;
        Loaded += async (_, _) =>
        {
            var checks = new List<string>(); string? error = null;
            try
            {
                string[] permissions = [PlatformCapabilities.WindowCaptureV1, PlatformCapabilities.CaptureLibraryReadV1,
                    PlatformCapabilities.CaptureLibrarySaveV1, PlatformCapabilities.CaptureLibraryManageV1,
                    PlatformCapabilities.AudioDevicesControlV1, PlatformCapabilities.NetworkBluetoothPairV1,
                    PlatformCapabilities.PerformanceReadV1, PlatformCapabilities.PerformanceFpsControlV1];
                var prompt = new BridgePermissionPrompt(new string('a',32), "fixture", "Synthetic permission fixture", permissions.Select(CapabilityPresentation.CapabilityName).ToArray());
                foreach (var choice in new[] { "deny", "cancel", "allow" })
                {
                    active = new("Allow widget access?", OverlayShellPage.PermissionPromptMessage(prompt),
                        (HostDialogChoice[])[new("allow", "Allow"), new("deny", "Deny")], directDecisionButtons: true) { XamlRoot = XamlRoot };
                    var opened = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                    var loaded = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                    active.Opened += (_, _) => opened.TrySetResult();
                    active.Loaded += (_, _) => loaded.TrySetResult();
                    var showing = active.ShowAsync().AsTask();
                    await opened.Task.WaitAsync(TimeSpan.FromSeconds(3), lifetime.Token);
                    await loaded.Task.WaitAsync(TimeSpan.FromSeconds(3), lifetime.Token);
                    Check(active.IsLoaded && Walk(active).OfType<TextBlock>().Any(t => t.Text.Contains("Synthetic permission fixture")), "Native prompt renders trusted widget identity: " + choice);
                    var focused = FocusManager.GetFocusedElement(XamlRoot);
                    Check(focused is Button { Content: "Cancel" }, "Default focus is Cancel: " + choice + " (actual " + focused?.GetType().Name + ": " + (focused as ContentControl)?.Content + ")");
                    active.Handle(ControllerButton.A, ControllerEventPhase.Pressed);
                    Check(!showing.IsCompleted, "Opening A cannot accept an unarmed dialog: " + choice);
                    Check(Walk(active).OfType<Button>().Any(b => b.Content is "Allow") && Walk(active).OfType<Button>().Any(b => b.Content is "Deny"),
                        "Allow/Deny are native buttons: " + choice);
                    var primary = Walk(active).OfType<FrameworkElement>().First(e => AutomationProperties.GetAutomationId(e) == "Host.Choice.Primary");
                    var bounds = primary.TransformToVisual(this).TransformBounds(new(0,0,primary.ActualWidth,primary.ActualHeight));
                    Check(bounds.Top >= 0 && bounds.Bottom <= ActualHeight && bounds.Left >= 0 && bounds.Right <= ActualWidth,
                        $"Confirmation remains within {ActualWidth:0}×{ActualHeight:0} viewport: {choice}");
                    active.ControllerArmed = true;
                    if (choice == "cancel") active.Handle(ControllerButton.B, ControllerEventPhase.Pressed);
                    else
                    {
                        var button = Walk(active).OfType<Button>().Single(b => b.Content is string label && label == (choice == "allow" ? "Allow" : "Deny"));
                        button.Focus(FocusState.Keyboard);
                        active.Handle(ControllerButton.A, ControllerEventPhase.Pressed);
                    }
                    await showing.WaitAsync(TimeSpan.FromSeconds(3), lifetime.Token);
                    Check(active.SelectedId == (choice == "cancel" ? null : choice), "Controller outcome is exact: " + choice);
                    active = null;
                }
                var pinContent = new Grid();
                pinContent.Children.Add(new Button { Content = "Pinned permission origin" });
                pin = new PinnedWidgetWindow("Permission fixture");
                var pinForeground = false;
                pin.ForegroundChanged += focused => pinForeground = focused;
                pin.SetContent(pinContent); pin.Place(new(100,100,740,660)); pin.Show(); pin.SetInteraction(true); pin.Activate();
                await Until(() => pinContent.IsLoaded && pinForeground);
                active = new("Allow widget access?", OverlayShellPage.PermissionPromptMessage(prompt),
                    (HostDialogChoice[])[new("allow","Allow"),new("deny","Deny")],directDecisionButtons:true) { XamlRoot = pinContent.XamlRoot };
                var pinShowing = active.ShowAsync().AsTask();
                await Until(() => active.IsLoaded && Walk(active).OfType<Button>().Any(b => b.Content is "Cancel"));
                Check(pinForeground,"Pinned window keeps foreground ownership during its native dialog");
                active.ControllerArmed = true; active.Handle(ControllerButton.B,ControllerEventPhase.Pressed);
                await pinShowing.WaitAsync(TimeSpan.FromSeconds(3),lifetime.Token);
                Check(pinForeground && pin.Interactive,"Pinned interaction survives native dialog cancellation");
                active = null;
                foreach (var (width, height) in new[] { (480, 360), (320, 280) })
                {
                    var scale = pinContent.XamlRoot.RasterizationScale;
                    pin.Place(new(100, 100, (int)(width * scale), (int)(height * scale)));
                    await Until(() => Math.Abs(pinContent.XamlRoot.Size.Width - width) < 2 && Math.Abs(pinContent.XamlRoot.Size.Height - height) < 2);
                    active = new("Allow widget access?", OverlayShellPage.PermissionPromptMessage(prompt),
                        (HostDialogChoice[])[new("allow", "Allow"), new("deny", "Deny")], directDecisionButtons: true) { XamlRoot = pinContent.XamlRoot };
                    active.ConstrainMessageViewport(height);
                    pinShowing = active.ShowAsync().AsTask();
                    await Until(() => active.IsLoaded && Walk(active).OfType<Button>().Any(b => b.Content is "Allow"));
                    foreach (var button in Walk(active).OfType<Button>().Where(b => b.Content is "Allow" or "Deny" or "Cancel"))
                    {
                        var bounds = button.TransformToVisual(pinContent).TransformBounds(new(0, 0, button.ActualWidth, button.ActualHeight));
                        Check(bounds.Top >= 0 && bounds.Bottom <= height && bounds.Left >= 0 && bounds.Right <= width,
                            $"{button.Content} remains fully visible in a {width}×{height} pin");
                    }
                    var viewport = Walk(active).OfType<ScrollViewer>().Single(v => AutomationProperties.GetAutomationId(v) == "Host.Choice.Message");
                    await Until(() => viewport.IsTabStop && viewport.ScrollableHeight > 0);
                    Check(viewport.ScrollableHeight > 0 && viewport.Focus(FocusState.Keyboard), "Long permission message is scrollable and focusable");
                    active.MoveFocus(FocusNavigationDirection.Down);
                    await Until(() => viewport.VerticalOffset > 0);
                    Check(true, "D-pad scrolls the permission message");
                    var offset = viewport.VerticalOffset;
                    active.SampleScroll(0, -32767, 1000);
                    active.SampleScroll(0, -32767, 1040);
                    await Until(() => viewport.VerticalOffset > offset);
                    Check(true, "Right stick scrolls the permission message");
                    active.ControllerArmed = true;
                    active.Handle(ControllerButton.B, ControllerEventPhase.Pressed);
                    await pinShowing.WaitAsync(TimeSpan.FromSeconds(3), lifetime.Token);
                    active = null;
                }
                active = null; pin.Dispose(); pin = null;
                status.Text = $"PASS: {checks.Count} native permission dialog checks";
            }
            catch (Exception failure) { error = failure.ToString(); status.Text = "FAIL: " + error; active?.Hide(); }
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!);
            await File.WriteAllTextAsync(output, JsonSerializer.Serialize(new { passed = error is null, checks, error }, new JsonSerializerOptions { WriteIndented = true }));
            void Check(bool passed, string name) { if (!passed) throw new Exception(name); checks.Add(name); }
            async Task Until(Func<bool> ready) { var deadline=Environment.TickCount64+3000; while(!ready()) { if(Environment.TickCount64>=deadline)throw new Exception("Native permission fixture timed out"); await Task.Delay(15,lifetime.Token); } }
        };
    }
    private static IEnumerable<DependencyObject> Walk(DependencyObject root)
    {
        yield return root;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            foreach (var child in Walk(VisualTreeHelper.GetChild(root,i))) yield return child;
    }
    public ValueTask DisposeAsync() { lifetime.Cancel(); active?.Hide(); pin?.Dispose(); pin=null; return ValueTask.CompletedTask; }
}
