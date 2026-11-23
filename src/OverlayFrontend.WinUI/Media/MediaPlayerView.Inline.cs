using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using WidgetRail.WidgetBridge;
using WidgetRail.WidgetProtocol;

namespace WidgetRail.OverlayFrontend.WinUI.Media;

internal sealed partial class MediaPlayerView
{
    private readonly Button inlinePlay = new()
    {
        Content = new SymbolIcon(Symbol.Play), MinWidth = 48, MinHeight = 48,
        HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
        Visibility = Visibility.Collapsed, IsTabStop = true, UseSystemFocusVisuals = true
    };
    private bool inlineLoadStarted, inlinePlayRequested, inlineFailed;
    private const int ResumeWindowMilliseconds = 15_000;
    private sealed record InlineResume(WeakReference<MediaPlayerView> Owner, NativePublicMediaSource.BufferedMedia Bytes,
        HostMediaPlayerSource Grant, long Deadline)
    {
        internal bool Accepts(HostMediaPlayerSource? grant, long ticks, long unixTime) =>
            Grant == grant && ticks < Deadline && !(Grant.ExpiresAtUnixMilliseconds <= unixTime);
    }
    private static readonly object inlineResumeGate = new();
    private static InlineResume? inlineResume;
    private static System.Threading.Timer? inlineResumeExpiry;
    internal bool IsInlineVideo => definition?.Presentation == MediaPlayerPresentation.Inline;
    internal bool IsInlinePoster => IsInlineVideo && player is null && !inlineFailed;

    private void InitializeInlineVideo()
    {
        Label(inlinePlay, "Play video", "MediaPlayer.Inline.Play");
        Grid.SetRowSpan(inlinePlay, 2); body.Children.Add(inlinePlay);
        inlinePlay.Click += (_, _) => StartInlineVideo();
    }

    private void ConfigureInlineVideo()
    {
        inlineLoadStarted = inlinePlayRequested = inlineFailed = false;
        RefreshInlineVideo();
        ReflowTransport();
    }

    private void RefreshInlineVideo()
    {
        if (inlinePlay.Visibility == Visibility.Visible && !IsInlinePoster) ParkSourceFocus();
        inlinePlay.Visibility = IsInlinePoster ? Visibility.Visible : Visibility.Collapsed;
        inlinePlay.IsEnabled = input && IsInlinePoster;
        if (IsInlinePoster && !inlineLoadStarted) HideMediaStatus();
    }

    private void StartInlineVideo()
    {
        if (!input || !active || !IsInlinePoster || inlineLoadStarted || currentAuthority?.Invoke() != true ||
            definition is not { } expected || resolveAuthority is not { } resolver) return;
        lifetime.Cancel(); lifetime.Dispose(); lifetime = new();
        inlineLoadStarted = inlinePlayRequested = true;
        if (expected.Source.Kind == MediaPlayerSourceKind.SavedCapture && resolvePlaybackLease is not null && releasePlaybackLease is not null)
        { playbackLeaseId = Guid.NewGuid().ToString("N"); currentPlaybackRelease = releasePlaybackLease; }
        ShowLoading(video.PosterSource is null);
        if (IsLoaded) timer.Start();
        _ = LoadAsync(playbackLeaseId is { } owned ? token => resolvePlaybackLease!(owned, token) : resolver,
            expected, currentAuthority, lifetime.Token);
        InteractionChanged?.Invoke();
    }

    private void SuspendInlineVideo()
    {
        if (!IsInlineVideo || !inlineLoadStarted) return;
        var retainedDefinition = definition;
        if (active && IsLoaded && !disposed && !retainedForPageExit && publicMedia is not null && resolved is { } grant &&
            currentAuthority?.Invoke()==true && publicMedia.TryTakeBufferedSnapshot() is { } bytes)
        {
            // One still-realized attachment may retain only its checked compressed
            // blocks for a brief scroll round trip. All native readers/players
            // are retired below. A replaced row or changed definition clears it.
            var resume = new InlineResume(new(this), bytes, grant, Environment.TickCount64 + ResumeWindowMilliseconds);
            lock (inlineResumeGate)
            {
                inlineResumeExpiry?.Dispose();
                inlineResume = resume;
                inlineResumeExpiry = new(static state =>
                {
                    lock (inlineResumeGate)
                        if (ReferenceEquals(inlineResume,state)) ClearInlineResume();
                }, resume, ResumeWindowMilliseconds, Timeout.Infinite);
            }
        }
        inlineLoadStarted = inlinePlayRequested = false;
        waitingForSlot = false;
        // Cancel acquisition even before a player exists. Suspension retains
        // position/preferences through the existing memory, never a decoder.
        lifetime.Cancel();
        SuspendPlayer();
        definition = retainedDefinition;
        resolved = null;
        video.Visibility = video.PosterSource is null ? Visibility.Collapsed : Visibility.Visible;
        toolbar.Visibility = videoControls.Visibility = Visibility.Collapsed;
        RefreshInlineVideo();
        InteractionChanged?.Invoke();
    }

    private static void ClearInlineResume()
    {
        inlineResumeExpiry?.Dispose();
        inlineResumeExpiry = null;
        inlineResume = null;
    }

    private bool OwnsInlineResume(InlineResume entry) =>
        entry.Owner.TryGetTarget(out var owner) && ReferenceEquals(owner, this);

    private void ForgetInlineResume()
    {
        lock (inlineResumeGate)
            if (inlineResume is { } entry && OwnsInlineResume(entry)) ClearInlineResume();
    }

    private NativePublicMediaSource.BufferedMedia? TakeInlineResume(HostMediaPlayerSource? grant)
    {
        lock (inlineResumeGate)
        {
            if (inlineResume is not { } entry || !OwnsInlineResume(entry)) return null;
            ClearInlineResume();
            return entry.Accepts(grant, Environment.TickCount64, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds())
                ? entry.Bytes : null;
        }
    }
}
