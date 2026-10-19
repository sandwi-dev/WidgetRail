using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace WidgetRail.OverlayFrontend.WinUI.Presentation;

internal sealed partial class WidgetTextEntryDialog
{
    private Func<KeyboardEdit, Task<KeyboardTextState>>? liveEdit;
    private readonly Queue<KeyboardEdit> liveQueue = new();
    private Task? liveDrain;
    private bool finishingLive;
    private bool liveFailed;
    private string? overflowHeading;
    internal bool IsLive => liveEdit is not null;
    // Window-relative rendered field bounds; editing and navigation remain shared.
    private Windows.Foundation.Rect? fieldAnchor;
    internal event Action? FieldAnchorChanged;
    internal Windows.Foundation.Rect? FieldAnchor
    {
        get => fieldAnchor;
        set { if (fieldAnchor == value) return; fieldAnchor = value; FieldAnchorChanged?.Invoke(); }
    }
    internal string DismissLabel => IsLive ? "Close" : "Cancel";
    internal Task WaitForLiveInputAsync() => liveDrain ?? Task.CompletedTask;

    private void SendLive(KeyboardEdit edit)
    {
        if (completed || finishingLive || liveFailed || liveEdit is null) return;
        if (liveQueue.Count >= 64)
        {
            // Bound held-key input during a stalled renderer without disabling
            // the focused key. Tell the user instead of silently dropping it.
            overflowHeading ??= heading.Text;
            heading.Text = "Page is catching up. Wait before typing more.";
            heading.Visibility = Visibility.Visible;
            repeat.Reset(); return;
        }
        liveQueue.Enqueue(edit);
        if (liveDrain is null || liveDrain.IsCompleted) liveDrain = DrainLiveAsync();
    }

    private async Task DrainLiveAsync()
    {
        try
        {
            while (!completed && liveEdit is { } send && liveQueue.TryDequeue(out var edit))
            {
                var state = await send(edit);
                if (completed) break;
                protectedInput = state.Sensitive;
                text!.Text = state.Display;
                text.Select(Math.Clamp(state.Caret, 0, state.Display.Length),
                    Math.Clamp(state.SelectionLength, 0, state.Display.Length - Math.Clamp(state.Caret, 0, state.Display.Length)));
                RefreshCaret();
            }
            if (!completed && overflowHeading is { } title) { heading.Text = title; overflowHeading = null; heading.Visibility = Visibility.Collapsed; }
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            // Do not surface renderer exception text: it may contain typed text.
            liveQueue.Clear(); repeat.Reset();
            heading.Text = "This field is no longer available. Close the keyboard and select it again.";
            heading.Visibility = Visibility.Visible;
            liveFailed = true;
        }
    }

    private async Task FinishLiveAsync(bool commit)
    {
        finishingLive = true; repeat.Reset();
        if (liveDrain is { } pending) await pending;
        if (completed) return;
        completed = true;
        finish?.Invoke(commit);
    }

    private bool HandleLiveKey(Windows.System.VirtualKey key, bool control, bool shift)
    {
        if (liveEdit is null) return false;
        if (key == Windows.System.VirtualKey.Space) { Insert(" "); return true; }
        if (key == Windows.System.VirtualKey.Enter)
        {
            if (shift && multiline) Insert("\n");
            else SendLive(new(KeyboardEditKind.Key, Key: "Enter", KeyCode: 13));
            return true;
        }
        string? name = key switch
        {
            Windows.System.VirtualKey.Back => "Backspace", Windows.System.VirtualKey.Delete => "Delete",
            Windows.System.VirtualKey.Left => "ArrowLeft", Windows.System.VirtualKey.Right => "ArrowRight",
            Windows.System.VirtualKey.Up => "ArrowUp", Windows.System.VirtualKey.Down => "ArrowDown",
            Windows.System.VirtualKey.Home => "Home", Windows.System.VirtualKey.End => "End",
            Windows.System.VirtualKey.Insert when shift => "Insert",
            Windows.System.VirtualKey.A when control => "a", Windows.System.VirtualKey.C when control => "c",
            Windows.System.VirtualKey.V when control => "v", Windows.System.VirtualKey.X when control => "x",
            Windows.System.VirtualKey.Z when control => "z", Windows.System.VirtualKey.Y when control => "y",
            _ => null,
        };
        if (name is null) return false;
        SendLive(new(KeyboardEditKind.Key, Key: name, KeyCode: (int)key, Modifiers: (control ? 2 : 0) | (shift ? 8 : 0)));
        return true;
    }
}
