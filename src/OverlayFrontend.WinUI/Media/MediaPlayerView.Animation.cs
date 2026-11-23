using Microsoft.UI.Xaml;
using WidgetRail.OverlayFrontend.WinUI.Presentation;
using WidgetRail.WidgetProtocol;

namespace WidgetRail.OverlayFrontend.WinUI.Media;

internal sealed partial class MediaPlayerView
{
    private static int residentAnimations;
    internal static int ResidentAnimations => Volatile.Read(ref residentAnimations);
    private bool ownsAnimationSlot, animationDemandStarted, animationWantsPlayback, animationManualPlayback;
    private NativeMotionVisibility? animationVisibility;
    private bool PassiveAnimation => definition?.Presentation == MediaPlayerPresentation.Animation;

    private void ConfigureAnimation(MediaPlayerDefinition next)
    {
        animationDemandStarted = false;
        animationWantsPlayback = next.Options.AutoPlay; animationManualPlayback = false;
        if (playbackMemory is { } saved && saved.Definition.Source == next.Source && saved.Definition.Revision == next.Revision &&
            saved.Definition.Options.IsAnimation && next.Options.IsAnimation)
        { animationWantsPlayback = saved.AnimationWantsPlayback; animationManualPlayback = saved.AnimationManualPlayback; }
        IsTabStop = !PassiveAnimation;
        IsHitTestVisible = !PassiveAnimation;
        ApplyFrameLayout(IsExpanded);
    }

    private void ObserveMediaPlacement(FrameworkElement placement)
    {
        // EffectiveViewportChanged describes geometry, not source identity. Keep
        // its observer for the element lifetime: a replacement row lease can
        // reuse exactly the same bounds and need not emit another viewport event.
        animationVisibility?.Dispose();
        animationVisibility = new(placement,
            () => !disposed && active && !retainedForPageExit && !editorPreviewPaused &&
                (IsInlineVideo || definition?.Options.IsAnimation == true && animationWantsPlayback && (animationsAllowed || animationManualPlayback)) && currentAuthority?.Invoke() == true,
            visible =>
            {
                // Explicit inline playback shares this element's viewport
                // lifetime, but never inherits decorative-motion autoplay.
                if (IsInlineVideo) { if (!visible && !changingPlacement) SuspendInlineVideo(); }
                else ReconcileAnimation(visible);
            });
    }

    private void ReconcileAnimation(bool visible)
    {
        if (definition?.Options.IsAnimation != true) return;
        if (!PassiveAnimation)
        {
            if (player is not { } interactive) return;
            if (visible && !IsPlaying) interactive.Play();
            else if (!visible && IsPlaying) interactive.Pause();
            return;
        }
        // Inline motion has a separate two-decoder budget. Releasing it outside
        // the viewport keeps long histories bounded and leaves interactive video
        // slots available. The decoded poster remains owned by artwork demand.
        if (!visible)
        {
            if (!animationDemandStarted) return;
            animationDemandStarted = false; waitingForSlot = false;
            lifetime.Cancel(); ReleasePlayer(); resolved = null;
            video.Visibility = video.PosterSource is null ? Visibility.Collapsed : Visibility.Visible;
            HideMediaStatus();
            return;
        }
        if (player is not null) { if (!IsPlaying) player.Play(); return; }
        if (animationDemandStarted || definition is not { } next || resolveAuthority is not { } resolver || currentAuthority is not { } current) return;
        lifetime.Dispose(); lifetime = new();
        animationDemandStarted = true;
        // A resumed demand may wait for its slot before LoadAsync reaches
        // SetActive. Keep the bounded retry timer alive in that case too.
        if (IsLoaded) timer.Start();
        _ = LoadAsync(resolver, next, current, lifetime.Token);
    }

    private bool TryAcquirePlayerSlot()
    {
        if (PassiveAnimation)
        {
            if (Interlocked.Increment(ref residentAnimations) <= 2) { ownsAnimationSlot = true; return true; }
            Interlocked.Decrement(ref residentAnimations); return false;
        }
        if (Interlocked.Increment(ref residentPlayers) <= 4) { ownsPlayerSlot = true; return true; }
        Interlocked.Decrement(ref residentPlayers); return false;
    }
}
