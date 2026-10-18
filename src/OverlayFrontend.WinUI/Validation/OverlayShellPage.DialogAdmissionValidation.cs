using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using WidgetRail.WidgetPresentationSession;

namespace WidgetRail.OverlayFrontend.WinUI.Shell;

internal sealed partial class OverlayShellPage
{
    internal void EnableDialogAdmissionValidation(string path)
    {
        Loaded += async (_, _) =>
        {
            var checks = new List<string>();
            Exception? failure = null;
            try
            {
                if (startup is not null) await startup;
                await Until(() => foreground && activeWidget is not null && !switching);
                foreach (var boundary in new[] { "priming", "disconnect", "foreground loss" })
                {
                    using var operation = new CancellationTokenSource();
                    localInstallCancellation = operation;
                    var dialog = new LocalWidgetInstallDialog { XamlRoot = XamlRoot };
                    localInstallDialog = dialog;
                    var showing = dialog.ShowAsync().AsTask();
                    try
                    {
                        await Until(() => dialog.IsLoaded && FindPrimary(dialog) is not null);
                        // The review callback is real, but no package installation is run.
                        var review = dialog.ReviewAsync(new LocalWidgetInstallResult("approval-required", "test.widget", "1.0.0", "Admission regression fixture"), CancellationToken.None);
                        await Until(() => FindPrimary(dialog) is { IsEnabled: true });
                        FindPrimary(dialog)!.Focus(FocusState.Keyboard);
                        Receive(Sample());
                        var rejected = Sample();
                        rejected.State.RightTrigger = 255;
                        if (boundary == "priming") rejected.Primed = 1;
                        else if (boundary == "disconnect") rejected.Connected = 0;
                        else SetForeground(false);
                        Receive(rejected);
                        if (boundary == "foreground loss") SetForeground(true);
                        // A is a real fresh edge; a trigger from the previous owner
                        // remains held. The dialog must wait for a neutral sample.
                        var freshAWithHeldTrigger = Sample(0x1000, pressed: 0x1000);
                        freshAWithHeldTrigger.State.RightTrigger = 255;
                        Receive(freshAWithHeldTrigger);
                        await Task.Delay(50);
                        Check(!review.IsCompleted, boundary + " cannot retain approval admission across a held trigger");
                        var released = Sample(released: 0x1000); released.RightTriggerReleased = 1;
                        Receive(released);
                        FindPrimary(dialog)!.Focus(FocusState.Keyboard);
                        Receive(Sample(0x1000, pressed: 0x1000));
                        Check(await review.WaitAsync(TimeSpan.FromSeconds(2)), boundary + " permits fresh A after neutral input");
                        dialog.Complete(new("installed", "test.widget", "1.0.0", "Fixture complete; no package installed"));
                        Receive(Sample(0x1000, pressed: 0x1000));
                        Check(!showing.IsCompleted, "completion does not reuse approval A");
                        Receive(Sample(released: 0x1000));
                        Receive(Sample(0x2000, pressed: 0x2000));
                        Receive(Sample(released: 0x2000));
                        await showing.WaitAsync(TimeSpan.FromSeconds(2));
                        Check(true, "fresh B closes completion");
                    }
                    finally { dialog.Hide(); localInstallDialog = null; localInstallCancellation = null; }
                }
                foreach (var kind in new[] { "host choice", "webapp editor" })
                {
                    var originalWidget = activeWidget;
                    var originalInteractive = interactive;
                    Task showing = kind == "host choice"
                        ? ShowHostChoiceAsync("Admission fixture", "No external action is performed.", (HostDialogChoice[])[new("test", "Test")], lifetime.Token)
                        : EditUserWebAppAsync(url: "https://example.invalid", title: "Admission fixture");
                    await Until(() => kind == "host choice" ? hostChoiceDialog is { IsLoaded: true } : webAppEditor is not null);
                    Receive(Sample(0x2000, pressed: 0x2000));
                    Check(!showing.IsCompleted, kind + " consumes the opening gesture while unarmed");
                    Receive(Sample(released: 0x2000));
                    var primed = Sample(); primed.Primed = 1; primed.State.RightTrigger = 255;
                    Receive(primed);
                    var held = Sample(0x2000, pressed: 0x2000); held.State.RightTrigger = 255;
                    Receive(held);
                    Check(!showing.IsCompleted, kind + " waits for release after priming");
                    var neutral = Sample(released: 0x2000); neutral.RightTriggerReleased = 1;
                    Receive(neutral);
                    Receive(Sample(0x2000, pressed: 0x2000));
                    await showing.WaitAsync(TimeSpan.FromSeconds(2));
                    Receive(Sample(released: 0x2000));
                    Check(activeWidget == originalWidget && interactive == originalInteractive,
                        kind + " closes on fresh B without replay into the underlying widget");
                }
                var automationChoice = ShowHostChoiceAsync("Automation fixture", "No controller is required.",
                    (HostDialogChoice[])[new("selected", "Select")], lifetime.Token, directDecisionButtons: true);
                await Until(() => hostChoiceDialog is { IsLoaded: true } choice && FindPrimary(choice) is not null);
                var action = FindPrimary(hostChoiceDialog!)!;
                ((IInvokeProvider)FrameworkElementAutomationPeer.CreatePeerForElement(action).GetPattern(PatternInterface.Invoke)).Invoke();
                Check(await automationChoice.WaitAsync(TimeSpan.FromSeconds(2)) == "selected",
                    "native button invocation bypasses controller-only arming without requiring a controller");
            }
            catch (Exception error) { failure = error; }
            finally
            {
                Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
                File.WriteAllText(path, JsonSerializer.Serialize(new { passed = failure is null, checks, error = failure?.ToString() }));
            }
            void Check(bool valid, string message)
            { if (!valid) throw new InvalidOperationException(message); checks.Add(message); }
            async Task Until(Func<bool> ready)
            {
                for (var attempt = 0; attempt < 400; attempt++)
                { if (ready()) return; await Task.Delay(25, lifetime.Token); }
                throw new TimeoutException("Dialog admission fixture did not settle.");
            }
        };
    }

    private static Button? FindPrimary(DependencyObject root)
    {
        if (root is Button { Name: "PrimaryButton" } primary) return primary;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            if (FindPrimary(VisualTreeHelper.GetChild(root, i)) is { } found) return found;
        return null;
    }
}
