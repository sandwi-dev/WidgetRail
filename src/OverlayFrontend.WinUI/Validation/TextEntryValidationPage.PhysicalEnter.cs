using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using WidgetRail.OverlayFrontend.WinUI.Presentation;
using WidgetRail.WidgetProtocol;

namespace WidgetRail.OverlayFrontend.WinUI.Validation;

internal sealed partial class TextEntryValidationPage
{
    private async Task VerifyPhysicalEnterModesAsync()
    {
        foreach (var live in new[] { false, true })
        {
            WidgetTextEntryDialog? dialog = null;
            var completions = 0;
            var accepted = false;
            var result = "";
            var liveText = "ab";
            var edits = new List<KeyboardEdit>();
            TaskCompletionSource? pendingEdit = null;
            async Task<KeyboardTextState> Edit(KeyboardEdit edit)
            {
                edits.Add(edit);
                if (pendingEdit is { } pending) await pending.Task;
                if (edit.Kind == KeyboardEditKind.Insert) liveText += edit.Text;
                return new(liveText, liveText.Length, 0);
            }
            dialog = new(new ViewNode { Id = "physical-enter", Kind = ViewNodeKind.TextEntry,
                TextEntryValue = "ab", TextEntryMaximumLength = 64, AccessibilityLabel = "Physical Enter fixture" },
                commit => { ++completions; accepted = commit; result = dialog!.TakeValue(); dialog.Hide(); },
                multiline: true, allowSubmit: true, liveEdit: live ? Edit : null);
            dialog.BindRoot(XamlRoot);
            var shown = dialog.ShowKeyboardAsync(presenter);
            try
            {
                await Ready(() => dialog.IsLoaded && dialog.InitialFocusForValidation);
                Check(dialog.HandleKeyboardKey(Windows.System.VirtualKey.Enter, shift: true),
                    (live ? "Live" : "Buffered") + " multiline Shift+Enter inserts a newline from a virtual key");
                await dialog.WaitForLiveInputAsync();
                Check(Newlines(dialog.TakeValue()) == "ab\n" && completions == 0 && !dialog.SubmitRequested,
                    "Multiline newline neither finishes nor submits the editor; live=" + live +
                    " value=" + System.Text.Json.JsonSerializer.Serialize(dialog.TakeValue()) + " completions=" + completions +
                    " submit=" + dialog.SubmitRequested);
                if (!live)
                {
                    dialog.EditorForValidation.Focus(FocusState.Keyboard);
                    dialog.SetCompositionForValidation(true);
                    Check(!dialog.HandleKeyboardKey(Windows.System.VirtualKey.Enter) &&
                        !dialog.HandleKeyboardKey(Windows.System.VirtualKey.Escape) && completions == 0,
                        "Composition guard leaves confirmation and cancellation to the focused native editor");
                    dialog.SetCompositionForValidation(false);
                }
                else
                {
                    pendingEdit = new(TaskCreationOptions.RunContinuationsAsynchronously);
                    dialog.InsertForValidation("z");
                }
                Check(dialog.HandleKeyboardKey(Windows.System.VirtualKey.Enter), "Physical Enter completes multiline editing");
                dialog.HandleKeyboardKey(Windows.System.VirtualKey.Enter);
                if (live)
                {
                    Check(completions == 0, "Live Enter waits for previously admitted edits to drain");
                    pendingEdit!.SetResult();
                }
                await shown.WaitAsync(TimeSpan.FromSeconds(5));
                Check(completions == 1 && accepted && Newlines(result) == (live ? "ab\nz" : "ab\n") && !dialog.SubmitRequested,
                    "Physical Enter commits exactly once without implicit submission");
                Check(edits.All(edit => edit.Kind != KeyboardEditKind.Key || edit.Key != "Enter"),
                    "Finishing the live editor does not send a DOM Enter");
            }
            finally { pendingEdit?.TrySetResult(); dialog.Hide(); await shown.WaitAsync(TimeSpan.FromSeconds(5)); dialog.Erase(); }
        }

        // An explicitly invoked submit key retains its separate browser behavior.
        var explicitEdits = new List<KeyboardEdit>();
        WidgetTextEntryDialog? explicitDialog = null;
        explicitDialog = new(new ViewNode { Id = "explicit-enter", Kind = ViewNodeKind.TextEntry,
            TextEntryMaximumLength = 64, AccessibilityLabel = "Explicit submit fixture" }, _ => explicitDialog!.Hide(),
            allowSubmit: true, liveEdit: edit => { explicitEdits.Add(edit); return Task.FromResult(new KeyboardTextState("", 0, 0)); });
        explicitDialog.BindRoot(XamlRoot);
        var explicitShown = explicitDialog.ShowKeyboardAsync(presenter);
        try
        {
            await Ready(() => explicitDialog.IsLoaded && explicitDialog.InitialFocusForValidation);
            var submit = (Button)FindDialog("Widget.TextEntry.Submit")!;
            ((IInvokeProvider)new ButtonAutomationPeer(submit).GetPattern(PatternInterface.Invoke)).Invoke();
            await Ready(() => explicitEdits.Count == 1);
            Check(explicitEdits[0] is { Kind: KeyboardEditKind.Key, Key: "Enter" },
                "Explicit Send Enter retains deliberate browser submission");
            explicitDialog.HandleKeyboardKey(Windows.System.VirtualKey.Escape);
            await explicitShown.WaitAsync(TimeSpan.FromSeconds(5));
        }
        finally { explicitDialog.Hide(); await explicitShown.WaitAsync(TimeSpan.FromSeconds(5)); explicitDialog.Erase(); }

        static async Task Ready(Func<bool> condition)
        {
            var deadline = Environment.TickCount64 + 5000;
            while (!condition())
            {
                if (Environment.TickCount64 >= deadline) throw new TimeoutException("Physical Enter fixture did not become ready.");
                await Task.Delay(25);
            }
        }
        // Native TextBox represents a newline as CR; live DOM editing uses LF.
        // Compare one semantic line break without changing either edit path.
        static string Newlines(string value) => value.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
    }
}
