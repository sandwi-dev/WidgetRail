using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

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
                if (verifyWebHandoff is not null) checks.AddRange(await verifyWebHandoff());
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
    }
}
