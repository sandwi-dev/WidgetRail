namespace WidgetRail.OverlayFrontend.WinUI.Input;

internal enum NativeDialogPhase { Inactive, Acquiring, Open, Completing, Failed }

/// <summary>Failure to discover a modal is never evidence that it was dismissed.</summary>
internal sealed class NativeDialogLifetime
{
    internal NativeDialogPhase Phase { get; private set; }
    internal bool HasOpened { get; private set; }
    private bool cancellationRequested;
    private int absent;
    private long lastConfirmed;
    internal void Begin(long now) { Phase = NativeDialogPhase.Acquiring; HasOpened = cancellationRequested = false; absent = 0; lastConfirmed = now; }
    internal void RequestCancellation() { cancellationRequested = true; absent = 0; }
    internal void Observe(bool? present, long now)
    {
        if (Phase is NativeDialogPhase.Inactive or NativeDialogPhase.Completing) return;
        if (present == true) { Phase = NativeDialogPhase.Open; HasOpened = true; absent = 0; lastConfirmed = now; }
        else if (present == false && (HasOpened || cancellationRequested))
        { if (++absent >= 2) Phase = NativeDialogPhase.Completing; }
        else absent = 0;
        if (Phase != NativeDialogPhase.Completing && now - lastConfirmed >= 10_000) Phase = NativeDialogPhase.Failed;
    }
    internal void End() { Phase = NativeDialogPhase.Inactive; absent = 0; }
}
