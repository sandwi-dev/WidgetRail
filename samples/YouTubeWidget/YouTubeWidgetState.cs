using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

namespace WidgetRail.Samples.YouTubeWidget;

internal enum YouTubeRoute
{
    Setup,
    Search,
    Link,
    Player,
    PlayerSettings,
    PlaybackRatePicker,
    Captions,
}

/// <summary>The control that initiated the in-flight playback command.</summary>
internal enum PendingMediaControl
{
    None,
    TogglePlayback,
    SeekBackward,
    SeekForward,
    Timeline,
    Volume,
    PlaybackRate,
    Muted,
    Loop,
}

/// <summary>Which provider mutation one setup request performs.</summary>
internal enum YouTubeSetupOperation { Configure, Delete }

/// <summary>One typed intent projected into the embedded-media command surface.</summary>
internal enum YouTubePlaybackIntent
{
    CommitLink,
    SelectResult,
    TogglePlayback,
    SeekBackward,
    SeekForward,
    SeekAbsolute,
    SetVolume,
    SetPlaybackRate,
    SetMuted,
    SetLoop,
}

internal sealed record YouTubePlaybackRequest(
    YouTubePlaybackIntent Intent,
    bool IsActive = true,
    double SeekStep = 0,
    double? RequestedValue = null,
    string? Text = null,
    string? ReturnFocusId = null,
    string? VideoId = null);

/// <summary>
/// One admitted setup mutation. The secret travels with the request and its
/// execution only; it is never projected into rendered state.
/// </summary>
internal sealed record YouTubeSetupRequest(
    YouTubeSetupOperation Operation,
    string Secret = "");

/// <summary>API-key configuration state for the search provider.</summary>
internal sealed record YouTubeSetupState
{
    public static YouTubeSetupState Initial { get; } = new();

    /// <summary>Whether the configuration probe has reported at least once.</summary>
    public bool ConfigurationKnown { get; init; }
    public bool Configured { get; init; }
    public bool Busy { get; init; }
    public string? Error { get; init; }
}

/// <summary>Query state for the cursor-paged search resource, which owns its own items.</summary>
internal sealed record YouTubeSearchState
{
    public static YouTubeSearchState Initial { get; } = new();

    public string QueryDraft { get; init; } = string.Empty;
    public string ActiveQuery { get; init; } = string.Empty;

    /// <summary>The result row to restore focus to after returning from the player.</summary>
    public string? ReturnFocusId { get; init; }
}

/// <summary>Embedded-media playback state and the one command awaiting the adapter.</summary>
internal sealed record YouTubePlaybackState
{
    public static YouTubePlaybackState Initial { get; } = new();

    public string Link { get; init; } = string.Empty;
    public string? VideoId { get; init; }
    public string? ValidationError { get; init; }
    public string? PlaybackError { get; init; }
    public EmbeddedMediaPlaybackState State { get; init; } = EmbeddedMediaPlaybackState.Ready;
    public double Position { get; init; }
    public double Duration { get; init; }
    public double Volume { get; init; } = 0.8;
    public double PlaybackRate { get; init; } = 1.0;
    public bool Muted { get; init; }
    public bool Loop { get; init; }
    public bool HasPlaybackObservation { get; init; }
    public EmbeddedMediaPlaybackCommand? PendingCommand { get; init; }
    public PendingMediaControl PendingControl { get; init; }

    /// <summary>
    /// The settled semantic retained across a seek's Loading window. A run of held
    /// seeks reports Loading throughout; only the first still sees a settled
    /// semantic worth capturing, and later ones inherit it rather than erase it.
    /// </summary>
    public EmbeddedMediaPlaybackState? SeekBufferingSemantic { get; init; }

    /// <summary>Whether the current command's pending feedback threshold elapsed.</summary>
    public bool PendingFeedbackVisible { get; init; }

    /// <summary>A bounded preference rejection that does not poison playback.</summary>
    public string? PreferenceError { get; init; }

    public string? Error => ValidationError ?? PlaybackError;

    /// <summary>The play/pause semantic the transport should present.</summary>
    public EmbeddedMediaPlaybackState PlaybackSemantic => SeekBufferingSemantic ?? State;

    /// <summary>The control that may render busy, scoped to the command that initiated it.</summary>
    public PendingMediaControl BusyControl =>
        PendingCommand is not null && PendingFeedbackVisible
            ? PendingControl
            : PendingMediaControl.None;

    /// <summary>Whether a seek is buffering rather than the media itself loading.</summary>
    public bool IsSeekBuffering =>
        State == EmbeddedMediaPlaybackState.Loading &&
        (PendingCommand?.Kind == EmbeddedMediaPlaybackCommandKind.Seek ||
         SeekBufferingSemantic is not null);

    /// <summary>Whether the media itself is loading, as opposed to a seek buffering.</summary>
    public bool IsMediaLoading =>
        !IsSeekBuffering &&
        (State == EmbeddedMediaPlaybackState.Loading ||
         PendingCommand?.Kind is EmbeddedMediaPlaybackCommandKind.Load or
             EmbeddedMediaPlaybackCommandKind.Cue);

    /// <summary>
    /// Projects one command for the adapter to execute. The seek-buffering
    /// semantic survives only a seek: any other command settles the transport.
    /// </summary>
    public YouTubePlaybackState WithQueuedCommand(
        long sequence,
        EmbeddedMediaPlaybackCommandKind kind,
        string videoId,
        PendingMediaControl control = PendingMediaControl.None,
        double? position = null,
        double? volume = null,
        double? playbackRate = null,
        bool? muted = null,
        bool? loop = null)
    {
        if (sequence <= 0) throw new ArgumentOutOfRangeException(nameof(sequence));
        return this with
        {
            PlaybackError = null,
            PreferenceError = null,
            SeekBufferingSemantic = kind == EmbeddedMediaPlaybackCommandKind.Seek
                ? SeekBufferingSemantic
                : null,
            PendingCommand = new EmbeddedMediaPlaybackCommand
            {
                Sequence = sequence,
                Kind = kind,
                MediaKey = videoId,
                PositionSeconds = position,
                Volume = volume,
                PlaybackRate = playbackRate,
                Muted = muted,
                Loop = loop,
            },
            PendingControl = control,
            PendingFeedbackVisible = false,
        };
    }

    /// <summary>Accepts a committed link, loading it when the strict parser admits it.</summary>
    public YouTubePlaybackState WithCommittedLink(string committedText, long sequence)
    {
        var link = committedText.Trim();
        var accepted = this with
        {
            Link = link,
            PlaybackError = null,
            SeekBufferingSemantic = null,
        };
        if (!YouTubeLinkParser.TryParse(link, out var videoId))
            return accepted with
            {
                ValidationError = "Enter a supported youtube.com or youtu.be video link.",
            };
        return (accepted with
        {
            ValidationError = null,
            VideoId = videoId,
            Position = 0,
            Duration = 0,
        }).WithQueuedCommand(sequence, EmbeddedMediaPlaybackCommandKind.Load, videoId);
    }

    /// <summary>Loads a chosen search result without going through the link entry.</summary>
    public YouTubePlaybackState WithSelectedVideo(string videoId, long sequence) =>
        (this with
        {
            Link = "https://www.youtube.com/watch?v=" + videoId,
            VideoId = videoId,
            ValidationError = null,
            PlaybackError = null,
            SeekBufferingSemantic = null,
            Position = 0,
            Duration = 0,
        }).WithQueuedCommand(sequence, EmbeddedMediaPlaybackCommandKind.Load, videoId);

    /// <summary>
    /// Applies one adapter report already admitted by WidgetOutOfBandCommand.
    /// </summary>
    public YouTubePlaybackState WithPlaybackEvent(
        EmbeddedMediaPlaybackEvent playbackEvent,
        bool completesPending)
    {
        var preferenceFailure = completesPending &&
            playbackEvent.State == EmbeddedMediaPlaybackState.Error &&
            PendingCommand?.Kind is EmbeddedMediaPlaybackCommandKind.SetPlaybackRate or
                EmbeddedMediaPlaybackCommandKind.SetMuted or
                EmbeddedMediaPlaybackCommandKind.SetLoop &&
            IsPreferenceCommandFailure(playbackEvent.ErrorCode);
        if (preferenceFailure)
        {
            return this with
            {
                PreferenceError = playbackEvent.ErrorCode == "command-unsupported"
                    ? "That setting is unavailable for the current YouTube video."
                    : "YouTube could not apply that player setting.",
                PendingCommand = null,
                PendingControl = PendingMediaControl.None,
                PendingFeedbackVisible = false,
            };
        }
        var matchingSeekLoading =
            playbackEvent.State == EmbeddedMediaPlaybackState.Loading &&
            PendingCommand is { Kind: EmbeddedMediaPlaybackCommandKind.Seek } currentSeek &&
            playbackEvent.CommandSequence == currentSeek.Sequence;
        var seekBuffering = matchingSeekLoading
            ? State is EmbeddedMediaPlaybackState.Playing or EmbeddedMediaPlaybackState.Paused
                ? State
                : SeekBufferingSemantic
            : playbackEvent.State == EmbeddedMediaPlaybackState.Loading
                ? SeekBufferingSemantic
                : null;
        return this with
        {
            SeekBufferingSemantic = seekBuffering,
            HasPlaybackObservation = true,
            State = playbackEvent.State,
            Position = Math.Max(0, playbackEvent.PositionSeconds),
            Duration = Math.Max(0, playbackEvent.DurationSeconds),
            Volume = Math.Clamp(playbackEvent.Volume, 0, 1),
            PlaybackRate = Math.Clamp(playbackEvent.PlaybackRate,
                ProtocolConstants.MinimumEmbeddedMediaPlaybackRate,
                ProtocolConstants.MaximumEmbeddedMediaPlaybackRate),
            Muted = playbackEvent.Muted,
            Loop = playbackEvent.Loop,
            PreferenceError = null,
            PlaybackError = playbackEvent.State == EmbeddedMediaPlaybackState.Error
                ? PlaybackErrorText(playbackEvent.ErrorCode)
                : null,
            PendingCommand = completesPending ? null : PendingCommand,
            PendingControl = completesPending ? PendingMediaControl.None : PendingControl,
            PendingFeedbackVisible = completesPending ? false : PendingFeedbackVisible,
        };
    }

    /// <summary>Marks the initiating control busy once its feedback threshold elapses.</summary>
    public YouTubePlaybackState WithPendingFeedback(long commandSequence) =>
        PendingCommand?.Sequence == commandSequence
            ? this with { PendingFeedbackVisible = true }
            : this;

    /// <summary>Retires transient presentation state that must not outlive visibility.</summary>
    public YouTubePlaybackState WithTransientStateCleared() =>
        this with { SeekBufferingSemantic = null, PendingFeedbackVisible = false };

    public static string PlaybackErrorText(string? errorCode) => errorCode switch
    {
        "invalid-video" => "YouTube rejected this video ID.",
        "video-private-or-missing" => "This video is private, unavailable, or no longer exists.",
        "embedding-disabled" => "The video owner does not allow embedded playback.",
        "client-identity-rejected" => "YouTube could not verify this desktop client.",
        "player-api-load-failed" => "The YouTube player could not be loaded. Check the network and retry.",
        "playback-unavailable" => "YouTube could not play this video in the embedded player.",
        "command-unsupported" => "This YouTube player control is unavailable.",
        "media-key-mismatch" =>
            "The playback session changed before this control completed. Return to the player and retry.",
        _ => "YouTube playback failed. Try another public embeddable video.",
    };

    private static bool IsPreferenceCommandFailure(string? errorCode) => errorCode is
        "command-unsupported" or
        "player-operation-timeout" or
        "authority-replaced" or
        "player-operation-overlap" or
        "media-key-mismatch";
}

/// <summary>
/// The widget's whole serialized state. Every transition here is a pure function
/// of the previous value, so each is exercised without constructing a widget.
/// </summary>
internal sealed record YouTubeWidgetState
{
    public static YouTubeWidgetState Initial { get; } = new();

    public YouTubeRoute Route { get; init; } = YouTubeRoute.Setup;
    public YouTubeRoute PlayerSettingsReturnRoute { get; init; } = YouTubeRoute.Player;
    public YouTubeSetupState Setup { get; init; } = YouTubeSetupState.Initial;
    public YouTubeSearchState Search { get; init; } = YouTubeSearchState.Initial;
    public YouTubePlaybackState Playback { get; init; } = YouTubePlaybackState.Initial;

    /// <summary>Whether this route actually renders the live native transport.</summary>
    public bool RendersLiveTransport =>
        Route is YouTubeRoute.Player or YouTubeRoute.Link;

    /// <summary>
    /// Whether the transport controls are part of this view at all. A command
    /// already in flight does not belong here: withdrawing the declaration while
    /// one is pending would retract a held button binding between its repeats, so
    /// a hold could never outlive its own first action. Pending work gates
    /// dispatch, not declaration.
    /// </summary>
    public bool CanDeclareTransportAction(bool isActive) =>
        RendersLiveTransport &&
        isActive &&
        Playback.VideoId is not null &&
        Playback.Error is null &&
        Playback.State != EmbeddedMediaPlaybackState.Error &&
        Playback.PendingCommand?.Kind is not (EmbeddedMediaPlaybackCommandKind.Load or
            EmbeddedMediaPlaybackCommandKind.Cue) &&
        (Playback.State != EmbeddedMediaPlaybackState.Loading ||
         Playback.SeekBufferingSemantic is not null);

    /// <summary>
    /// Single-flight admission. Buffering does not appear here either: a seek puts
    /// the player into Loading, so refusing to dispatch while Loading would stall a
    /// hold after its first step. One command at a time is the whole rule, and the
    /// completing event refreshes position before the next command is admitted.
    /// </summary>
    public bool CanDispatchTransportAction(bool isActive) =>
        Playback.PendingCommand is null && CanDeclareTransportAction(isActive);

    /// <summary>Whether the retained player may admit a settings mutation.</summary>
    public bool CanDispatchPlayerSetting(bool isActive) =>
        (Route is YouTubeRoute.PlayerSettings or YouTubeRoute.PlaybackRatePicker) &&
        isActive &&
        Playback.VideoId is not null &&
        Playback.Error is null &&
        Playback.State != EmbeddedMediaPlaybackState.Error &&
        !Playback.IsMediaLoading &&
        Playback.PendingCommand is null;

    // Leaving the Player route no longer has to retire a fullscreen flag: the
    // host drops its own activation as soon as the admitted snapshot stops
    // declaring the capability for this exact surface.
    public YouTubeWidgetState WithRoute(YouTubeRoute route) => this with { Route = route };

    public YouTubeWidgetState WithPlayerSettingsRoute() => this with
    {
        PlayerSettingsReturnRoute = RendersLiveTransport ? Route : PlayerSettingsReturnRoute,
        Route = YouTubeRoute.PlayerSettings,
    };

    /// <summary>The route to fall back to, which needs a key before search is reachable.</summary>
    public YouTubeWidgetState WithConfiguredRoute() =>
        WithRoute(Setup.Configured ? YouTubeRoute.Search : YouTubeRoute.Setup);

    /// <summary>Opening setup deliberately clears the last failure it reported.</summary>
    public YouTubeWidgetState WithSetupRoute() => this with
    {
        Route = YouTubeRoute.Setup,
        Setup = Setup with { Error = null },
    };

    public YouTubeWidgetState WithConfigurationSummary(bool configured) => this with
    {
        Setup = Setup with
        {
            ConfigurationKnown = true,
            Configured = configured,
            Error = null,
        },
        Route = configured ? YouTubeRoute.Search : YouTubeRoute.Setup,
    };

    public YouTubeWidgetState WithConfigurationFailure(string message) => this with
    {
        Setup = Setup with { ConfigurationKnown = true, Configured = false, Error = message },
        Route = YouTubeRoute.Setup,
    };

    /// <summary>The optimistic projection both setup mutations share.</summary>
    public YouTubeWidgetState WithSetupInFlight() => this with
    {
        Setup = Setup with { Busy = true, Error = null },
    };

    /// <summary>
    /// Removes only the projection owned by a canceled setup command. The
    /// current configuration fields may have changed independently while the
    /// command was running, so only Busy and Error return to the first baseline.
    /// </summary>
    public YouTubeWidgetState WithSetupProjectionRolledBack(
        YouTubeSetupState baseline) => this with
    {
        Setup = Setup with { Busy = baseline.Busy, Error = baseline.Error },
    };

    public YouTubeWidgetState WithConfiguredKey() => this with
    {
        Setup = Setup with
        {
            Configured = true,
            ConfigurationKnown = true,
            Busy = false,
            Error = null,
        },
        Route = YouTubeRoute.Search,
    };

    public YouTubeWidgetState WithDeletedKey() => this with
    {
        Setup = Setup with { Configured = false, Busy = false },
        Search = Search with { QueryDraft = string.Empty, ActiveQuery = string.Empty },
    };

    public YouTubeWidgetState WithSetupFailure(WidgetCommandError error) => this with
    {
        Setup = Setup with { Busy = false, Error = error.Message },
    };

    public YouTubeWidgetState WithQueryDraft(string committedText) => this with
    {
        Search = Search with { QueryDraft = committedText.Trim() },
    };

    public YouTubeWidgetState WithActiveQuery(string query) => this with
    {
        Search = Search with { ActiveQuery = query },
    };

    public YouTubeWidgetState WithCommittedLink(string committedText, long sequence)
    {
        var playback = Playback.WithCommittedLink(committedText, sequence);
        return this with
        {
            Playback = playback,
            Route = playback.ValidationError is null ? YouTubeRoute.Player : Route,
        };
    }

    public YouTubeWidgetState WithSelectedResult(
        string returnFocusId,
        string videoId,
        long sequence) => this with
    {
        Search = Search with { ReturnFocusId = returnFocusId },
        Route = YouTubeRoute.Player,
        Playback = Playback.WithSelectedVideo(videoId, sequence),
    };

    public YouTubeWidgetState WithPlayback(
        Func<YouTubePlaybackState, YouTubePlaybackState> transition) => this with
    {
        Playback = transition(Playback),
    };
}
