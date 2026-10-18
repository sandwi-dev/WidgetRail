using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
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
