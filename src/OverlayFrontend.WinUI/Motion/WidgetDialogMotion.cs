using System.Numerics;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Hosting;
using WidgetRail.PlatformSettings;

namespace WidgetRail.OverlayFrontend.WinUI.Motion;

internal enum WidgetDialogMotionRole { Dialog, Preview, MediaPreview }

/// <summary>One opening per native surface lifetime, independent of ordinary snapshot updates.</summary>
internal sealed class WidgetDialogMotion : IDisposable
{
    private readonly FrameworkElement dialog;
    private readonly FrameworkElement? scrim;
    private readonly WidgetDialogMotionRole role;
    private readonly TaskCompletionSource<WidgetMotionOutcome> ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal Task<WidgetMotionOutcome> Ready => ready.Task;
    private WidgetMotionOptions options;
    private WidgetCompositionTarget? dialogTarget;
    private WidgetCompositionTarget? scrimTarget;
    private WidgetCompositionMotion? motion;
    private bool entered;
    private bool exiting;
    private bool disposed;
    private Vector2 size;
    internal Task<WidgetMotionOutcome>? Opening { get; private set; }
    internal Task<WidgetMotionOutcome>? Closing { get; private set; }

    internal WidgetDialogMotion(FrameworkElement dialog, FrameworkElement? scrim, AppearanceSettings appearance, bool systemAnimationsEnabled,
        WidgetDialogMotionRole role = WidgetDialogMotionRole.Dialog)
    {
        if (role == WidgetDialogMotionRole.Dialog) ArgumentNullException.ThrowIfNull(scrim);
        this.dialog = dialog; this.scrim = scrim; this.role = role;
        options = WidgetMotionOptions.From(appearance, systemAnimationsEnabled);
        dialog.Loaded += Loaded;
        dialog.Unloaded += Unloaded;
        dialog.SizeChanged += Resized;
        dialog.LayoutUpdated += LayoutReady;
    }

    internal void ApplyAppearance(AppearanceSettings appearance, bool systemAnimationsEnabled)
    {
        var next = WidgetMotionOptions.From(appearance, systemAnimationsEnabled);
        if (options == next) return;
        options = next;
        motion?.Cancel();
    }
    private void Loaded(object sender, RoutedEventArgs args) => Start();
    private void LayoutReady(object? sender, object args) => Start();
    private void Resized(object sender, SizeChangedEventArgs args)
    {
        if (entered && size != new Vector2((float)args.NewSize.Width, (float)args.NewSize.Height)) RetireTargets();
        else Start();
    }
    private void Unloaded(object sender, RoutedEventArgs args)
    { if (!dialog.IsLoaded) RetireTargets(); }

    private void Start()
    {
        if (disposed || entered || exiting || !dialog.IsLoaded || dialog.ActualWidth <= 0 || dialog.ActualHeight <= 0 ||
            scrim is not null && (!scrim.IsLoaded || scrim.ActualWidth <= 0 || scrim.ActualHeight <= 0)) return;
        entered = true;
        dialog.LayoutUpdated -= LayoutReady;
        CreateTargets();
        var recipe = role == WidgetDialogMotionRole.Dialog ? WidgetMotionPolicy.Dialog(options, true) :
            WidgetMotionPolicy.Preview(options, role == WidgetDialogMotionRole.MediaPreview);
        Opening = motion!.PlayAsync(scrimTarget is null ? [new(dialogTarget!, recipe)] :
            (WidgetMotionPlayback[])[new(dialogTarget!, recipe), new(scrimTarget, WidgetMotionPolicy.Dialog(options, true, scrim: true))]);
        _ = CompleteReadinessAsync(Opening);
    }

    private async Task CompleteReadinessAsync(Task<WidgetMotionOutcome> opening)
    {
        try { ready.TrySetResult(await opening.ConfigureAwait(false)); }
        catch (Exception error) { ready.TrySetException(error); }
    }

    internal Task<WidgetMotionOutcome> CloseAsync()
    {
        if (disposed) return Task.FromResult(WidgetMotionOutcome.Disposed);
        if (Closing is not null) return Closing;
        exiting = true;
        dialog.LayoutUpdated -= LayoutReady;
        if (role != WidgetDialogMotionRole.Dialog)
        {
            RetireTargets(); ready.TrySetResult(WidgetMotionOutcome.Canceled);
            return Closing = Task.FromResult(WidgetMotionOutcome.Completed);
        }
        if (!dialog.IsLoaded || dialog.ActualWidth <= 0 || dialog.ActualHeight <= 0 ||
            scrim is not null && (!scrim.IsLoaded || scrim.ActualWidth <= 0 || scrim.ActualHeight <= 0))
            return Closing = Task.FromResult(WidgetMotionOutcome.Completed);
        if (motion is null) CreateTargets();
        // Reuse the same targets so an opening interrupted by B starts from the
        // compositor's current values rather than flashing fully open first.
        return Closing = motion!.PlayAsync((WidgetMotionPlayback[])[new(dialogTarget!, WidgetMotionPolicy.Dialog(options, false)),
            new(scrimTarget!, WidgetMotionPolicy.Dialog(options, false, scrim: true))]);
    }

    private void CreateTargets()
    {
        size = new((float)dialog.ActualWidth, (float)dialog.ActualHeight);
        dialogTarget = WidgetCompositionTarget.ForClippedDialog(dialog, size);
        if (scrim is not null) scrimTarget = WidgetCompositionTarget.ForClippedDialog(scrim, new((float)scrim.ActualWidth, (float)scrim.ActualHeight));
        motion = new(ElementCompositionPreview.GetElementVisual(dialog).Compositor, dialog.DispatcherQueue);
    }

    private void RetireTargets()
    {
        motion?.Dispose(); motion = null;
        dialogTarget?.Dispose(); dialogTarget = null;
        scrimTarget?.Dispose(); scrimTarget = null;
    }
    public void Dispose()
    {
        if (disposed) return;
        dialog.Loaded -= Loaded; dialog.Unloaded -= Unloaded; dialog.SizeChanged -= Resized; dialog.LayoutUpdated -= LayoutReady;
        RetireTargets(); ready.TrySetResult(WidgetMotionOutcome.Disposed); disposed = true;
    }
}
