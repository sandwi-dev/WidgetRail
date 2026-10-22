using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;
using WidgetRail.OverlayFrontend.WinUI.Presentation;

namespace WidgetRail.OverlayFrontend.WinUI.Shell;

internal sealed partial class OverlayShellPage
{
    internal void EnableIntentValidation(string resultPath, Func<Task<IReadOnlyList<string>>>? verifyWebHandoff = null)
    {
        Loaded += async (_, _) =>
        {
            var checks = new List<string>();
            string? error = null;
            try
            {
                if (startup is not null) await startup;
                await Until(() => activeWidget == "intent-source" && !switching && foreground);
                Check(surface?.CurrentBinding is not null, "Source presentation is live");
                var source = surface!.CurrentBinding!.Frame;
                var originalPreparedLauncher = OpenExternalWebPageRequested;
                var preparedUrls = new List<string>();
                OpenExternalWebPageRequested = (uri, _) => { preparedUrls.Add(uri.AbsoluteUri); return Task.FromResult(true); };
                try
                {
                    var pending = InvokeIntentAsync(new(source, new("prepare-wait", "prepare-wait", ControllerButton.A, InputScopeId: "root")));
                    await Until(() => preparingIntent);
                    await Until(() => Walk(surface!).OfType<TextBlock>().Any(text => text.Text == "Calculating search…"));
                    Check(!pending.IsCompleted, "Native progress renders while intent preparation is still pending");
                    await RouteButtonAsync(ControllerButton.B, ControllerEventPhase.Pressed);
                    await pending;
                    await RouteButtonAsync(ControllerButton.B, ControllerEventPhase.Released);
                    await RouteButtonAsync(ControllerButton.A, ControllerEventPhase.Released);
                    Check(preparedUrls.Count == 0 && !HostChoiceActive && activeWidget == "intent-source" && interactive,
                        "Back cancels preparation without opening a browser or leaving the source");
                    await InvokeIntentAsync(new(surface!.CurrentBinding!.Frame, new("prepare-fail", "prepare-fail", ControllerButton.A, InputScopeId: "root")));
                    await RouteButtonAsync(ControllerButton.A, ControllerEventPhase.Released);
                    Check(preparedUrls.Count == 0 && !HostChoiceActive, "Failed preparation opens no destination or misleading handler dialog");
                    var successful = InvokeIntentAsync(new(surface!.CurrentBinding!.Frame, new("prepare", "prepare", ControllerButton.A, InputScopeId: "root")));
                    await RouteButtonAsync(ControllerButton.A, ControllerEventPhase.Released);
                    await successful;
                    Check(preparedUrls.SequenceEqual(new[] { "https://example.com/calculated" }), "One click prepares and opens the calculated URL exactly once");
                }
                finally { OpenExternalWebPageRequested = originalPreparedLauncher; }
                source = surface!.CurrentBinding!.Frame;
                await InvokeAsync(new(source, new("open", "open", ControllerButton.A, InputScopeId: "root")));
                await Until(() => activeWidget == "intent-target" && surface?.CurrentBinding?.Frame.Snapshot.Root.Children[0].Text == "https://example.com/guide");
                Check(interactive && !switching, "Intent opens its destination through normal widget selection");
                Check(shellGestures.Owns(ControllerButton.A), "Opening A remains owned until release");
                await RouteButtonAsync(ControllerButton.A, ControllerEventPhase.Released);
                Check(!shellGestures.Owns(ControllerButton.A), "Release clears opening gesture ownership");
                await RouteButtonAsync(ControllerButton.B, ControllerEventPhase.Pressed);
                Check(!interactive && activeWidget == "intent-target", "B opens the tray without returning to the sender");
                await RouteButtonAsync(ControllerButton.B, ControllerEventPhase.Released);
                await SelectAsync("intent-source");
                await Until(() => FocusManager.GetFocusedElement(XamlRoot) is DependencyObject focused && AutomationProperties.GetAutomationId(focused) == "Widget.open");
                surface!.MoveFocus(FocusNavigationDirection.Down);
                await Until(() => FocusManager.GetFocusedElement(XamlRoot) is DependencyObject focused && AutomationProperties.GetAutomationId(focused) == "Widget.wait");
                var beforeDialog = FocusManager.GetFocusedElement(XamlRoot) as Control;
                var choice = ShowHostChoiceAsync("Open with", "Choose a widget for this item.",
                    (HostDialogChoice[])[new("first", "First widget"), new("second", "Second widget")], lifetime.Token);
                await Until(() => hostChoiceDialog is { IsLoaded: true });
                var dialog = hostChoiceDialog!;
                dialog.Handle(ControllerButton.A, ControllerEventPhase.Pressed);
                Check(!choice.IsCompleted, "Opening gesture cannot accept an unarmed dialog");
                dialog.ControllerAdmission.Observe(new() { Connected = 1 }, ownsInput: true);
                var list = Walk(dialog).OfType<ListView>().Single();
                list.SelectedIndex = 1;
                ((ListViewItem)list.Items[1]).Focus(FocusState.Keyboard);
                dialog.Handle(ControllerButton.A, ControllerEventPhase.Pressed);
                Check(await choice == "second", "Controller A selects the focused handler");
                Check(!HostChoiceActive && MainFocusEnabled, "Dialog restores normal widget input and focus ownership");
                if (beforeDialog is not null)
                {
                    await Until(() => ReferenceEquals(FocusManager.GetFocusedElement(XamlRoot), beforeDialog));
                    Check(true, "Dialog restores its prior control after native teardown");
                }
                var originalLauncher = OpenExternalWebPageRequested;
                var externalCalls = 0;
                OpenExternalWebPageRequested = (_, _) => { externalCalls++; return Task.FromResult(false); };
                try
                {
                    var failedRequest = InvokeAsync(new(surface!.CurrentBinding!.Frame,
                        new("unsupported", "unsupported", ControllerButton.A, InputScopeId: "root")));
                    await Until(() => hostChoiceDialog is { IsLoaded: true });
                    Check(externalCalls == 0, "Receiver rejection offers fallback without opening a second destination");
                    hostChoiceDialog!.ControllerAdmission.Observe(new() { Connected = 1 }, ownsInput: true);
                    hostChoiceDialog.Handle(ControllerButton.B, ControllerEventPhase.Pressed);
                    await failedRequest;
                    Check(externalCalls == 0 && activeWidget == "intent-target", "Cancelling fallback keeps the ordinary destination and opens nothing");
                }
                finally { OpenExternalWebPageRequested = originalLauncher; }
                await PinAsync("intent-target", WidgetPresentationSession.WidgetPinnedProjection.FullWidgetLayoutId);
                await SelectAsync("intent-source");
                await Until(() => FocusManager.GetFocusedElement(XamlRoot) is DependencyObject focused && AutomationProperties.GetAutomationId(focused) == "Widget.wait");
                var beforePassive = FocusManager.GetFocusedElement(XamlRoot);
                var deliveries = pinned!.Presenter!.CurrentBinding!.Frame.Snapshot.Root.Children[1].Text;
                await InvokeAsync(new(surface!.CurrentBinding!.Frame, new("open", "open", ControllerButton.A, InputScopeId: "root")));
                await Until(() => pinned?.Presenter?.CurrentBinding?.Frame.Snapshot.Root.Children[1].Text != deliveries);
                Check(activeWidget == "intent-source" && MainFocusEnabled && ReferenceEquals(FocusManager.GetFocusedElement(XamlRoot), beforePassive),
                    "Passive intent updates the visible pin and preserves the sender's exact focus");
                Check(pinned!.Window.IsVisible && !pinned.Window.Interactive, "Passive intent does not activate the pinned window");
                await InvokeAsync(new(surface.CurrentBinding!.Frame, new("interaction", "interaction", ControllerButton.A, InputScopeId: "root")));
                await Until(() => activeWidget == "intent-target" && !switching && surface?.CurrentBinding?.Frame.Snapshot.Root.Children[0].Text == "https://example.com/interaction");
                Check(surface!.CurrentBinding!.Frame.Snapshot.Root.Children[1].Text == "Deliveries: 4",
                    "Receiver escalation opens the accepted request without duplicate delivery");
                await SelectAsync("intent-source");
                await InvokeAsync(new(surface!.CurrentBinding!.Frame, new("open-full", "open-full", ControllerButton.A, InputScopeId: "root")));
                await Until(() => activeWidget == "intent-target" && !switching);
                Check(interactive, "Sender can request normal activation even when a passive pin is available");
                await UnpinAsync(save: true);
                await SelectAsync("intent-source");
                await InvokeIndexed(surface!);
                await Until(() => activeWidget == "intent-target" && surface?.CurrentBinding?.Frame.Snapshot.Root.Children[0].Text == "https://example.com/indexed/1");
                Check(interactive, "A virtualized item opens its declared intent through its live lease");
                await SelectAsync("intent-source");
                await PinAsync("intent-source", "links-pin");
                await EnterPinnedAsync();
                await Until(() => PinnedInputActive && FocusManager.GetFocusedElement(pinned!.Window.AutomationRoot.XamlRoot) is DependencyObject focused &&
                    AutomationProperties.GetAutomationId(focused) == "Widget.pin-open");
                await VerifyPinnedDialog();
                pinned!.Presenter!.ActivateFocused();
                await Until(() => activeWidget == "intent-target" && foreground && !PinnedInputActive &&
                    surface?.CurrentBinding?.Frame.Snapshot.Root.Children[0].Text == "https://example.com/pinned");
                Check(interactive && pinned!.IsCurrent, "An authored pinned control transfers intent presentation to the main window");
                await EnterPinnedAsync();
                await Until(() => PinnedInputActive);
                await InvokeIndexed(pinned!.Presenter!);
                await Until(() => activeWidget == "intent-target" && foreground && !PinnedInputActive &&
                    surface?.CurrentBinding?.Frame.Snapshot.Root.Children[0].Text == "https://example.com/indexed/1");
                Check(interactive, "A virtualized pinned item preserves its lease and hands off input once");
                await UnpinAsync(save: true);
                await SelectAsync("intent-source", enterWidget: false);
                var sourceDescriptor = surface!.CurrentBinding!.Frame.Descriptor;
                var pinCommands = PinCommands(sourceDescriptor);
                Check(pinCommands.Select(command => command.Id).SequenceEqual(new[] { "Pin.links-pin", "Pin.Full" }),
                    "Without an existing pin, authored and full-widget choices retain their order");
                await PinAsync("intent-target", WidgetPresentationSession.WidgetPinnedProjection.FullWidgetLayoutId);
                var oldPin = pinned!;
                pinCommands = PinCommands(sourceDescriptor);
                Check(pinCommands[0].Id == "Pin.Replace" && pinCommands[0].Label == "Replace pin",
                    "A different pinnable widget offers Replace pin first");
                Check(pinCommands.Select(command => command.Id).SequenceEqual(new[]
                    { "Pin.Replace", "Pin.Remove", "Pin.Adjust", "Pin.Opacity", "Pin.Interact" }),
                    "Pin commands without shortcuts precede Interact in the requested order");
                await ShowTrayMenuAsync(sourceDescriptor);
                await Until(() => trayMenu?.Items[0].IsLoaded == true);
                var actualMenu = trayMenu!;
                Check(actualMenu.Items.Take(4).All(item => item.Icon is null), "Pin management commands have no invented shortcut hints");
                Check(actualMenu.Items[4].Icon is FontIcon && AutomationProperties.GetHelpText(actualMenu.Items[4]).Contains("Tray shortcut:"),
                    "Interact exposes its controller shortcut and accessible help");
                Check(actualMenu.Items.Single(item => AutomationProperties.GetAutomationId(item) == "Overlay.TrayCommand.Reorder").Icon is FontIcon &&
                    actualMenu.Items.Single(item => AutomationProperties.GetAutomationId(item) == "Overlay.TrayCommand.Restart") is { Icon: null, KeyboardAcceleratorTextOverride: "" },
                    "Reorder exposes its shortcut and Restart has no nonexistent hold shortcut");
                CloseTrayMenu(true);
                Check(PinCommands(sourceDescriptor with { PinningSupported = false, FullWidgetPinningSupported = false })
                    .All(command => command.Id != "Pin.Replace"), "A nonpinnable widget offers no replacement");
                var replacing = pinCommands[0].Action();
                await Until(() => hostChoiceDialog is { IsLoaded: true });
                await Until(() => FocusManager.GetFocusedElement(XamlRoot) is ListViewItem { Tag: "Pin.links-pin" });
                Check(string.IsNullOrEmpty(hostChoiceDialog!.PrimaryButtonText) && hostChoiceDialog.CloseButtonText == "Cancel",
                    "Multiple layouts use an action list with Cancel and no redundant Replace footer");
                Check(hostChoiceDialog.FocusFirstOptionOnOpen, "Replacement picker initially focuses its first layout");
                hostChoiceDialog.ControllerAdmission.Observe(new() { Connected = 1 }, ownsInput: true);
                hostChoiceDialog.Handle(ControllerButton.B, ControllerEventPhase.Pressed);
                await replacing;
                Check(ReferenceEquals(pinned, oldPin) && oldPin.IsCurrent, "Cancelling a replacement preserves the live original pin");
                Check(!interactive && !MainFocusEnabled && !HostChoiceActive,
                    "Replacement chooser returns to tray mode without enabling widget input");
                replacing = PinCommands(sourceDescriptor)[0].Action();
                await Until(() => hostChoiceDialog is { IsLoaded: true });
                await Until(() => FocusManager.GetFocusedElement(XamlRoot) is ListViewItem { Tag: "Pin.links-pin" });
                ++pinIntent;
                ChooseFirstPinLayout();
                await replacing;
                Check(ReferenceEquals(pinned, oldPin), "An intervening pin operation invalidates the open chooser");
                replacing = PinCommands(sourceDescriptor)[0].Action();
                await Until(() => hostChoiceDialog is { IsLoaded: true });
                await Until(() => FocusManager.GetFocusedElement(XamlRoot) is ListViewItem { Tag: "Pin.links-pin" });
                ChooseFirstPinLayout();
                await replacing;
                Check(pinned is { WidgetId: "intent-source", LayoutId: "links-pin", IsCurrent: true } && !oldPin.IsCurrent,
                    "Choosing an authored layout replaces and revokes the old pin");
                Check(PinCommands(sourceDescriptor).Any(command => command.Id == "Pin.Replace"),
                    "An authored pin can be replaced by the same widget's full layout");
                var replacement = pinned;
                await PinAsync("intent-target", WidgetPresentationSession.WidgetPinnedProjection.FullWidgetLayoutId, oldPin);
                Check(ReferenceEquals(pinned, replacement), "A stale replacement cannot remove a newer pin");
                await SelectAsync("intent-target", enterWidget: false);
                var singleReplacement = PinCommands(surface!.CurrentBinding!.Frame.Descriptor)[0].Action();
                Check(!HostChoiceActive, "A single eligible layout replaces directly without a redundant chooser");
                await singleReplacement;
                Check(pinned is { WidgetId: "intent-target", IsCurrent: true } && !HostChoiceActive && !replacement!.IsCurrent,
                    "Direct single-layout replacement revokes the prior pin and leaves the replacement current");
                await UnpinAsync(save: true);
                await SelectAsync("intent-source", enterWidget: false);
                await PinAsync("intent-source", "links-pin");
                var compactPin = pinned!;
                compactPin.Window.Place(PinnedPlacementPolicy.Constrain(new(compactPin.Window.Bounds.X, compactPin.Window.Bounds.Y, 480, 270), compactPin.Monitor, compactPin.Limits));
                await SavePinnedAsync(compactPin, required: true);
                var compactGeometry = pinnedPreferences.Find("intent-source", "links-pin")!;
                await PinAsync("intent-source", WidgetPresentationSession.WidgetPinnedProjection.FullWidgetLayoutId, compactPin);
                var fullPin = pinned!;
                fullPin.Window.Place(PinnedPlacementPolicy.Constrain(new(fullPin.Window.Bounds.X, fullPin.Window.Bounds.Y, 720, 500), fullPin.Monitor, fullPin.Limits));
                await SavePinnedAsync(fullPin, required: true);
                var fullGeometry = pinnedPreferences.Find("intent-source", fullPin.LayoutId)!;
                Check(compactGeometry.WidthDip != fullGeometry.WidthDip || compactGeometry.HeightDip != fullGeometry.HeightDip,
                    "Fixture saves distinct dimensions for two layouts of one widget");
                await UnpinAsync(save: true);
                pinnedPreferences = await pinnedStore!.LoadAsync(lifetime.Token);
                await PinAsync("intent-source", "links-pin");
                Check(pinned!.Window.Bounds == PinnedPlacementPolicy.Resolve(PinnedDisplayAreas.Read(), compactGeometry, pinned.Limits)!.Bounds,
                    "Authored layout restores its own geometry after replacing and reloading preferences");
                await PinAsync("intent-source", WidgetPresentationSession.WidgetPinnedProjection.FullWidgetLayoutId, pinned);
                Check(pinned!.Window.Bounds == PinnedPlacementPolicy.Resolve(PinnedDisplayAreas.Read(), fullGeometry, pinned.Limits)!.Bounds,
                    "Full layout independently restores its geometry after returning from the authored layout");
                await UnpinAsync(save: true);
                if (verifyWebHandoff is not null) checks.AddRange(await verifyWebHandoff());

                async Task VerifyPinnedDialog()
                {
                    var current = pinned!;
                    var root = current.Window.AutomationRoot.XamlRoot;
                    var focusedBefore = FocusManager.GetFocusedElement(root);
                    var decision = ShowHostChoiceAsync("Allow widget access?", "Synthetic access request from a pinned widget.",
                        (HostDialogChoice[])[new("allow", "Allow"), new("deny", "Deny")], lifetime.Token,
                        directDecisionButtons: true, pinnedOwner: current);
                    await Until(() => hostChoiceDialog is { IsLoaded: true });
                    Check(ReferenceEquals(hostChoiceDialog!.XamlRoot, root) && HostChoiceHasForeground,
                        "Host decision uses the active pin root and retains its foreground ownership");
                    // The shell's foreground flag may include owned peer HWNDs.
                    // Exercise routing with that flag revoked while the pin
                    // independently retains its actual native foreground.
                    var originalForeground = foreground;
                    SetForeground(false);
                    var neutral = new WidgetRail.OverlayPlatformClient.ControllerFrame { Connected = 1 };
                    Receive(neutral);
                    Check(hostChoiceDialog.ControllerAdmission.IsArmed, "Neutral controller frame arms a pinned host dialog");
                    var allow = Walk(hostChoiceDialog).OfType<Button>().Single(b => AutomationProperties.GetAutomationId(b) == "Host.Choice.Primary");
                    allow.Focus(FocusState.Keyboard);
                    var accept = neutral; accept.State.Buttons = accept.PressedButtons = 0x1000;
                    Receive(accept);
                    Check(await decision == "allow", "Controller input reaches the pinned dialog while main HWND is inactive");
                    await Until(() => ReferenceEquals(FocusManager.GetFocusedElement(root), focusedBefore));
                    Check(PinnedInputActive && current.IsCurrent && !HostChoiceActive,
                        "Pinned decision restores the exact original focus and interaction");
                    var release = neutral; release.ReleasedButtons = 0x1000; Receive(release);
                    SetForeground(originalForeground);
                }

                List<(string Label, string Id, Func<Task> Action)> PinCommands(WidgetRail.WidgetBridge.BridgeWidgetDescriptor descriptor)
                {
                    var result = new List<(string, string, Func<Task>)>();
                    AddPinnedCommands(descriptor, (label, id, action) => result.Add((label, id, action)));
                    return result;
                }
                void ChooseFirstPinLayout()
                {
                    var picker = hostChoiceDialog!;
                    var items = Walk(picker).OfType<ListView>().Single();
                    Check(ReferenceEquals(FocusManager.GetFocusedElement(XamlRoot), items.Items[0]),
                        "First replacement layout is ready for A without a manual focus transfer");
                    picker.ControllerAdmission.Observe(new() { Connected = 1 }, ownsInput: true);
                    picker.Handle(ControllerButton.A, ControllerEventPhase.Pressed);
                }
            }
            catch (Exception failure) { error = failure.ToString(); }
            await File.WriteAllTextAsync(resultPath, JsonSerializer.Serialize(new { pid = Environment.ProcessId, passed = error is null, checks, error }));
            void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); checks.Add(message); }
        };

        static async Task Until(Func<bool> condition)
        {
            var end = Environment.TickCount64 + 15_000;
            while (!condition()) { if (Environment.TickCount64 >= end) throw new TimeoutException("Intent native fixture did not become ready."); await Task.Delay(20); }
        }
        static IEnumerable<DependencyObject> Walk(DependencyObject node)
        {
            yield return node;
            for (var i = 0; i < VisualTreeHelper.GetChildrenCount(node); i++)
                foreach (var child in Walk(VisualTreeHelper.GetChild(node, i))) yield return child;
        }
        static async Task InvokeIndexed(WidgetViewPresenter presenter)
        {
            var collection = Walk(presenter).OfType<WidgetIndexedCollectionView>().Single();
            await Until(() => collection.NativeView.Items.Count >= 2);
            collection.NativeView.ScrollIntoView(collection.NativeView.Items[1]);
            await Until(() => collection.NativeView.ContainerFromIndex(1) is Control);
            ((Control)collection.NativeView.ContainerFromIndex(1)).Focus(FocusState.Keyboard);
            await Until(() => collection.FocusedRow()?.Item.Key == "link.1");
            if (!await collection.InvokeFocusedInputAsync(ControllerButton.A, ControllerEventPhase.Pressed))
                throw new InvalidOperationException("Indexed intent did not own its primary action.");
        }
    }
}
