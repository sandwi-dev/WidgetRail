using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

namespace WidgetRail.Samples.YouTubeWidget;

internal enum YouTubeRoute { Setup, Search, Link, Player }

/// <summary>The control that initiated the in-flight playback command.</summary>
internal enum PendingMediaControl
{
    None,
    TogglePlayback,
    SeekBackward,
    SeekForward,
    Timeline,
    Volume,
}

/// <summary>Which provider mutation one setup request performs.</summary>
internal enum YouTubeSetupOperation { Configure, Delete }

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
    public long CommandSequence { get; init; }
    public long EventSequence { get; init; }
    public EmbeddedMediaPlaybackCommand? PendingCommand { get; init; }
    public PendingMediaControl PendingControl { get; init; }

    /// <summary>
    /// The settled semantic retained across a seek's Loading window. A run of held
    /// seeks reports Loading throughout; only the first still sees a settled
    /// semantic worth capturing, and later ones inherit it rather than erase it.
    /// </summary>
    public EmbeddedMediaPlaybackState? SeekBufferingSemantic { get; init; }

    /// <summary>The command whose pending feedback threshold has elapsed.</summary>
    public long? BusyCommandSequence { get; init; }

    public string? Error => ValidationError ?? PlaybackError;

    /// <summary>The play/pause semantic the transport should present.</summary>
    public EmbeddedMediaPlaybackState PlaybackSemantic => SeekBufferingSemantic ?? State;

    /// <summary>The control that may render busy, scoped to the command that initiated it.</summary>
    public PendingMediaControl BusyControl =>
        PendingCommand is { } pending && BusyCommandSequence == pending.Sequence
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
        EmbeddedMediaPlaybackCommandKind kind,
        string videoId,
        PendingMediaControl control = PendingMediaControl.None,
        double? position = null,
        double? volume = null)
    {
        var sequence = CommandSequence + 1;
        return this with
        {
            PlaybackError = null,
            SeekBufferingSemantic = kind == EmbeddedMediaPlaybackCommandKind.Seek
                ? SeekBufferingSemantic
                : null,
            CommandSequence = sequence,
            PendingCommand = new EmbeddedMediaPlaybackCommand
            {
                Sequence = sequence,
                Kind = kind,
                MediaKey = videoId,
                PositionSeconds = position,
                Volume = volume,
            },
            PendingControl = control,
            BusyCommandSequence = null,
        };
    }

    /// <summary>Accepts a committed link, loading it when the strict parser admits it.</summary>
    public YouTubePlaybackState WithCommittedLink(string committedText)
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
        }).WithQueuedCommand(EmbeddedMediaPlaybackCommandKind.Load, videoId);
    }

    /// <summary>Loads a chosen search result without going through the link entry.</summary>
    public YouTubePlaybackState WithSelectedVideo(string videoId) =>
        (this with
        {
            Link = "https://www.youtube.com/watch?v=" + videoId,
            VideoId = videoId,
            ValidationError = null,
            PlaybackError = null,
            SeekBufferingSemantic = null,
            Position = 0,
            Duration = 0,
        }).WithQueuedCommand(EmbeddedMediaPlaybackCommandKind.Load, videoId);

    /// <summary>
    /// Admits one adapter report. Stale sequences, foreign media keys, and
    /// reports correlated to a command this state never issued are ignored.
    /// </summary>
    public YouTubePlaybackState WithPlaybackEvent(EmbeddedMediaPlaybackEvent playbackEvent)
    {
        if (playbackEvent.Sequence <= EventSequence ||
            VideoId is not { } videoId ||
            !string.Equals(playbackEvent.MediaKey, videoId, StringComparison.Ordinal))
            return this;
        if (playbackEvent.CommandSequence > 0 &&
            (PendingCommand is not { } correlated ||
             correlated.Sequence != playbackEvent.CommandSequence ||
             !string.Equals(correlated.MediaKey, videoId, StringComparison.Ordinal)))
            return this;

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
        var completesPending = PendingCommand is { } current &&
            playbackEvent.CommandSequence == current.Sequence;

        return this with
        {
            SeekBufferingSemantic = seekBuffering,
            EventSequence = playbackEvent.Sequence,
            State = playbackEvent.State,
            Position = Math.Max(0, playbackEvent.PositionSeconds),
            Duration = Math.Max(0, playbackEvent.DurationSeconds),
            Volume = Math.Clamp(playbackEvent.Volume, 0, 1),
            PlaybackError = playbackEvent.State == EmbeddedMediaPlaybackState.Error
                ? PlaybackErrorText(playbackEvent.ErrorCode)
                : null,
            PendingCommand = completesPending ? null : PendingCommand,
            PendingControl = completesPending ? PendingMediaControl.None : PendingControl,
            BusyCommandSequence = completesPending ? null : BusyCommandSequence,
        };
    }

    /// <summary>Marks the initiating control busy once its feedback threshold elapses.</summary>
    public YouTubePlaybackState WithPendingFeedback(long commandSequence) =>
        PendingCommand?.Sequence == commandSequence
            ? this with { BusyCommandSequence = commandSequence }
            : this;

    /// <summary>Retires transient presentation state that must not outlive visibility.</summary>
    public YouTubePlaybackState WithTransientStateCleared() =>
        this with { SeekBufferingSemantic = null, BusyCommandSequence = null };

    public static string PlaybackErrorText(string? errorCode) => errorCode switch
    {
        "invalid-video" => "YouTube rejected this video ID.",
        "video-private-or-missing" => "This video is private, unavailable, or no longer exists.",
        "embedding-disabled" => "The video owner does not allow embedded playback.",
        "client-identity-rejected" => "YouTube could not verify this desktop client.",
        "player-api-load-failed" => "The YouTube player could not be loaded. Check the network and retry.",
        "playback-unavailable" => "YouTube could not play this video in the embedded player.",
        "command-unsupported" => "This YouTube player control is unavailable.",
        _ => "YouTube playback failed. Try another public embeddable video.",
    };
}

/// <summary>
/// The widget's whole serialized state. Every transition here is a pure function
/// of the previous value, so each is exercised without constructing a widget.
/// </summary>
internal sealed record YouTubeWidgetState
{
    public static YouTubeWidgetState Initial { get; } = new();

    public YouTubeRoute Route { get; init; } = YouTubeRoute.Setup;
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

    // Leaving the Player route no longer has to retire a fullscreen flag: the
    // host drops its own activation as soon as the admitted snapshot stops
    // declaring the capability for this exact surface.
    public YouTubeWidgetState WithRoute(YouTubeRoute route) => this with { Route = route };

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

    public YouTubeWidgetState WithCommittedLink(string committedText)
    {
        var playback = Playback.WithCommittedLink(committedText);
        return this with
        {
            Playback = playback,
            Route = playback.ValidationError is null ? YouTubeRoute.Player : Route,
        };
    }

    public YouTubeWidgetState WithSelectedResult(string returnFocusId, string videoId) => this with
    {
        Search = Search with { ReturnFocusId = returnFocusId },
        Route = YouTubeRoute.Player,
        Playback = Playback.WithSelectedVideo(videoId),
    };

    public YouTubeWidgetState WithPlayback(
        Func<YouTubePlaybackState, YouTubePlaybackState> transition) => this with
    {
        Playback = transition(Playback),
    };
}
