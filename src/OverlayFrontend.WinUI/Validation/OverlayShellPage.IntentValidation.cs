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
                await InvokeAsync(new(source, new("open", "open", ControllerButton.A, InputScopeId: "root")));
                await Until(() => activeWidget == "intent-target" && surface?.CurrentBinding?.Frame.Snapshot.Root.Children[0].Text == "https://example.com/guide");
                Check(interactive && !switching, "Intent opens its destination through normal widget selection");
                Check(shellOwnedReleases.Contains(ControllerButton.A), "Opening A remains owned until release");
                await RouteButtonAsync(ControllerButton.A, ControllerEventPhase.Released);
                Check(!shellOwnedReleases.Contains(ControllerButton.A), "Release clears opening gesture ownership");
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
                dialog.ControllerArmed = true;
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
                    hostChoiceDialog!.ControllerArmed = true;
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
                Check(actualMenu.Items[5].Icon is FontIcon && actualMenu.Items[6].Icon is FontIcon &&
                    actualMenu.Items[6].KeyboardAcceleratorTextOverride == "Hold",
                    "Reorder and restart show controller glyphs with an explicit hold hint");
                CloseTrayMenu(true);
                Check(PinCommands(sourceDescriptor with { PinningSupported = false, FullWidgetPinningSupported = false })
                    .All(command => command.Id != "Pin.Replace"), "A nonpinnable widget offers no replacement");
                var replacing = pinCommands[0].Action();
                await Until(() => hostChoiceDialog is { IsLoaded: true });
                Check(hostChoiceDialog!.PrimaryButtonText == "Replace pin", "Multiple layouts use a clearly labelled replacement chooser");
                hostChoiceDialog.ControllerArmed = true;
                hostChoiceDialog.Handle(ControllerButton.B, ControllerEventPhase.Pressed);
                await replacing;
                Check(ReferenceEquals(pinned, oldPin) && oldPin.IsCurrent, "Cancelling a replacement preserves the live original pin");
                Check(!interactive && !MainFocusEnabled && !HostChoiceActive,
                    "Replacement chooser returns to tray mode without enabling widget input");
                replacing = PinCommands(sourceDescriptor)[0].Action();
                await Until(() => hostChoiceDialog is { IsLoaded: true });
                ++pinIntent;
                ChooseFirstPinLayout();
                await replacing;
                Check(ReferenceEquals(pinned, oldPin), "An intervening pin operation invalidates the open chooser");
                replacing = PinCommands(sourceDescriptor)[0].Action();
                await Until(() => hostChoiceDialog is { IsLoaded: true });
                ChooseFirstPinLayout();
                await replacing;
                Check(pinned is { WidgetId: "intent-source", LayoutId: "links-pin", IsCurrent: true } && !oldPin.IsCurrent,
                    "Choosing an authored layout replaces and revokes the old pin");
                Check(PinCommands(sourceDescriptor).All(command => command.Id != "Pin.Replace"),
                    "The already pinned widget does not offer Replace pin");
                var replacement = pinned;
                await PinAsync("intent-target", WidgetPresentationSession.WidgetPinnedProjection.FullWidgetLayoutId, oldPin);
                Check(ReferenceEquals(pinned, replacement), "A stale replacement cannot remove a newer pin");
                await SelectAsync("intent-target", enterWidget: false);
                await PinCommands(surface!.CurrentBinding!.Frame.Descriptor)[0].Action();
                Check(pinned is { WidgetId: "intent-target", IsCurrent: true } && !HostChoiceActive && !replacement!.IsCurrent,
                    "A single available layout replaces directly without a chooser");
                await UnpinAsync(save: true);
                if (verifyWebHandoff is not null) checks.AddRange(await verifyWebHandoff());

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
                    items.SelectedIndex = 0;
                    ((ListViewItem)items.Items[0]).Focus(FocusState.Keyboard);
                    picker.ControllerArmed = true;
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
