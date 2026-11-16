using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using WidgetRail.PlatformSettings;

namespace WidgetRail.OverlayFrontend.WinUI.Shell;

internal sealed partial class OverlayShellPage
{
    internal void EnableCaptureConfirmationValidation(string path)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(path))!;
        var profile = Path.GetFullPath(options.SettingsRoot);
        if (!profile.StartsWith(directory + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
            profile.Equals(PlatformSettingsPaths.CreateDefault().RootDirectory, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Capture confirmation validation requires an isolated profile below its result directory.");
        var started = false;
        Loaded += async (_, _) =>
        {
            if (started) return; started = true;
            var checks = new List<string>();
            var store = new PlatformSettingsStore(new(profile));
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(100));
            PlatformSettingsDocument? original = null;
            string? failure = null;
            var phase = "startup";
            try
            {
                original = await store.LoadAsync();
                await store.UpdateAsync(s => s with { CaptureConfirmations = new() });
                if (startup is not null) await startup;
                await SelectAsync("settings", true);
                await Wait(() => MainFocusEnabled && !switching);
                Check(!await Decision("capture-library", false, proceed: false, remember: true), "Cancel rejects the capture even with Remember checked");
                Check((await store.LoadAsync()).CaptureConfirmations.Widgets.Count == 0, "Cancel does not persist Remember");
                Check(await Decision("capture-library", false, proceed: true, remember: false), "Proceed without Remember permits the current capture");
                Check((await store.LoadAsync()).CaptureConfirmations.Widgets.Count == 0, "Proceed alone retains future confirmations");
                Check(await Decision("capture-library", false, proceed: true, remember: true), "Proceed with Remember saves the screenshot preference");
                phase = "remembered skip";
                Check(await ConfirmCaptureAsync("capture-library", false, "unused", () => Task.FromResult(true), deadline.Token) && hostChoiceDialog is null,
                    "A repeated screenshot proceeds without a dialog");
                Check(!await Decision("capture-library", true, proceed: false, remember: false), "Recording still asks after remembering screenshots");
                Check(!await Decision("widgetrail.samples.game-help", false, proceed: false, remember: false), "Game Help has an independent confirmation");
                Check(await Decision("widgetrail.samples.game-help", true, proceed: true, remember: true), "Game Help recording choices persist independently");
                var saved = (await store.LoadAsync()).CaptureConfirmations;
                Check(saved.ShouldConfirm("widgetrail.samples.game-help", false) && !saved.ShouldConfirm("widgetrail.samples.game-help", true), "Game Help capture types remain isolated");

                // Re-enter Settings so its worker reads the host-owned changes.
                await SelectAsync("audio-mixer", true);
                await SelectAsync("settings", true);
                await InvokeWidget("category.overlay");
                await InvokeWidget("overlay.capture.restore");
                await WaitAsync(async () => (await store.LoadAsync()).CaptureConfirmations.Widgets.Count == 0);
                Check(true, "The actual Settings restore button clears remembered choices");
                Check(!await Decision("capture-library", false, proceed: false, remember: false), "Reset makes Capture Library ask again");
                Check(!await Decision("widgetrail.samples.game-help", true, proceed: false, remember: false), "Reset makes Game Help ask again");

                phase = "reset with dialog open";
                var pending = ConfirmCaptureAsync("capture-library", false, "Capture confirmation fixture", () => Task.FromResult(true), deadline.Token);
                await WaitDialog();
                ToggleRemember();
                await store.UpdateAsync(s => s with { CaptureConfirmations = s.CaptureConfirmations.Reset() });
                InvokeDialog("Proceed");
                Check(await pending && (await store.LoadAsync()).CaptureConfirmations.Widgets.Count == 0, "A pending accepted dialog cannot undo a newer reset");

                var current = true;
                phase = "retired request";
                pending = ConfirmCaptureAsync("capture-library", false, "Capture confirmation fixture", () => Task.FromResult(current), deadline.Token);
                await WaitDialog(); ToggleRemember(); current = false; InvokeDialog("Proceed");
                Check(!await pending && (await store.LoadAsync()).CaptureConfirmations.Widgets.Count == 0, "A retired request neither proceeds nor remembers");
            }
            catch (Exception error) { failure = phase + ": " + error; }
            finally
            {
                deadline.Cancel(); hostChoiceDialog?.Hide();
                if (original is not null)
                    try { await store.UpdateAsync(_ => original); }
                    catch (Exception error) { failure ??= "Profile restoration: " + error; }
                Directory.CreateDirectory(directory);
                File.WriteAllText(path, JsonSerializer.Serialize(new { passed = failure is null, checks, error = failure,
                    profile, physicalInput = false, capturesStarted = 0 }, new JsonSerializerOptions { WriteIndented = true }));
            }

            async Task<bool> Decision(string widget, bool video, bool proceed, bool remember)
            {
                phase = widget + (video ? " recording" : " screenshot");
                var pending = ConfirmCaptureAsync(widget, video, "Capture confirmation fixture", () => Task.FromResult(true), deadline.Token);
                await WaitDialog();
                var box = Walk(hostChoiceDialog!).OfType<CheckBox>().Single(x => AutomationProperties.GetAutomationId(x) == "Host.Choice.Remember");
                Check(box.Content?.ToString()?.Contains(video ? "recordings" : "screenshots", StringComparison.Ordinal) == true, "Checkbox names the current capture type");
                if (remember) ToggleRemember();
                InvokeDialog(proceed ? "Proceed" : "Cancel");
                return await pending;
            }
            Task WaitDialog() => Wait(() => hostChoiceDialog is { IsOpenForValidation: true } dialog &&
                Walk(dialog).OfType<CheckBox>().Any(x => x.IsLoaded && AutomationProperties.GetAutomationId(x) == "Host.Choice.Remember"));
            void ToggleRemember()
            {
                var box = Walk(hostChoiceDialog!).OfType<CheckBox>().Single(x => AutomationProperties.GetAutomationId(x) == "Host.Choice.Remember");
                ((IToggleProvider)FrameworkElementAutomationPeer.CreatePeerForElement(box).GetPattern(PatternInterface.Toggle)).Toggle();
            }
            void InvokeDialog(string label)
            {
                var id = label == "Proceed" ? "Host.Choice.Primary" : "Host.Choice.Close";
                var button = Walk(hostChoiceDialog!).OfType<Button>().Single(x => AutomationProperties.GetAutomationId(x) == id && x.IsEnabled);
                ((IInvokeProvider)FrameworkElementAutomationPeer.CreatePeerForElement(button).GetPattern(PatternInterface.Invoke)).Invoke();
            }
            async Task InvokeWidget(string id)
            {
                phase = "Settings " + id;
                await Wait(() => FindWidget(id) is { IsLoaded: true, IsEnabled: true } && MainFocusEnabled);
                var control = FindWidget(id)!;
                control.StartBringIntoView(new BringIntoViewOptions { AnimationDesired = false });
                ((IInvokeProvider)FrameworkElementAutomationPeer.CreatePeerForElement(control).GetPattern(PatternInterface.Invoke)).Invoke();
            }
            Control? FindWidget(string id) => surface is null ? null : Walk(surface).OfType<Control>()
                .FirstOrDefault(x => AutomationProperties.GetAutomationId(x) == "Widget." + id);
            void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); checks.Add(message); }
            Task Wait(Func<bool> ready) => WaitAsync(() => Task.FromResult(ready()));
            async Task WaitAsync(Func<Task<bool>> ready)
            {
                var until = Environment.TickCount64 + 12000;
                while (!await ready())
                {
                    if (Environment.TickCount64 > until) throw new TimeoutException(phase);
                    await Task.Delay(25, deadline.Token);
                }
            }
        };
        static IEnumerable<DependencyObject> Walk(DependencyObject root)
        {
            yield return root;
            for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); ++i)
                foreach (var child in Walk(VisualTreeHelper.GetChild(root, i))) yield return child;
        }
    }
}
