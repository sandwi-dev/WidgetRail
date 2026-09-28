using System.Security.Cryptography;
using System.Text.Json;
using WidgetRail.WidgetBridge;
using WidgetRail.WidgetProtocol;

namespace WidgetRail.WidgetPresentationSession;

public sealed partial class WidgetPresentationSession
{
    private sealed class MediaDocumentEpoch(WidgetPresentationAuthority owner, EmbeddedMediaSession declaration)
    {
        internal WidgetPresentationAuthority Owner { get; } = owner;
        internal EmbeddedMediaSession Identity { get; } = CopyMedia(declaration);
        internal CancellationTokenSource Retirement { get; } = new();
        internal SemaphoreSlim Events { get; } = new(1, 1);
        internal long LastEventSequence;
        internal int PendingEvents;
    }
    private readonly Dictionary<string, MediaDocumentEpoch> _mediaDocuments = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim _mediaResolveSlots = new(4, 4);
    private int _pendingMediaResolves;

    /// <summary>
    /// Resolve the exact current declaration through sealed bridge package authority.
    /// No paths are read locally. A compatible newer snapshot may retain the completed
    /// document, but a retired document can never acquire a later same-named session.
    /// </summary>
    public async Task<WidgetPresentationEmbeddedMediaDocument> ResolveEmbeddedMediaAsync(
        WidgetPresentationAuthority authority, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        MediaDocumentEpoch epoch;
        CancellationToken lifetimeToken;
        EmbeddedMediaSession declaration;
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            _ = ValidateAuthority(authority);
            if (!_mediaDocuments.TryGetValue(authority.WidgetId, out epoch!))
                throw Stale("embedded_media_unavailable", authority.WidgetId, "The current view declares no media document.");
            declaration = CopyMedia(_states[authority.WidgetId].LastGood!.Snapshot.EmbeddedMediaSession!);
            if (_pendingMediaResolves >= 16)
                throw Stale("embedded_media_saturated", authority.WidgetId, "Embedded media resolution is saturated.");
            lifetimeToken = _lifetime.Token;
            ++_pendingMediaResolves;
        }
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, lifetimeToken, epoch.Retirement.Token);
        deadline.CancelAfter(_options.EmbeddedMediaTimeout);
        bool entered = false;
        try
        {
            await _mediaResolveSlots.WaitAsync(deadline.Token).ConfigureAwait(false);
            entered = true;
            lock (_gate) { DemandMediaEpochLocked(authority.WidgetId, epoch); _ = ValidateAuthority(authority); }
            var response = await MediaRequestAsync(BridgeMessageTypes.ResolveEmbeddedMedia,
                new BridgeEmbeddedMediaRequest(authority.WidgetId, authority.WidgetInstanceId,
                    authority.RuntimeGeneration, authority.PresentationGeneration, authority.SnapshotSequence, declaration.Id),
                BridgeMessageTypes.EmbeddedMediaSession, deadline.Token).ConfigureAwait(false);
            var resources = ValidateMediaBundle(response.Payload, authority, declaration);
            deadline.Token.ThrowIfCancellationRequested();
            lock (_gate) DemandMediaEpochLocked(authority.WidgetId, epoch);
            return new(this, epoch, authority, declaration.Id, declaration.EntryAsset, Array.AsReadOnly(resources));
        }
        finally
        {
            if (entered) _mediaResolveSlots.Release();
            lock (_gate) --_pendingMediaResolves;
        }
    }

    /// <summary>
    /// Null means permanently retired (including removal followed by reappearance).
    /// A scope change, viewport removal/parking, geometry or playback update is compatible.
    /// Returned declarations are defensive, read-only copies; subscribe to PresentationChanged
    /// and reconcile on the frontend's UI thread rather than polling or replaying snapshots.
    /// </summary>
    public WidgetPresentationEmbeddedMediaState? GetEmbeddedMediaState(WidgetPresentationEmbeddedMediaDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        lock (_gate)
        {
            if (!IsMediaDocumentCurrentLocked(document)) return null;
            var frame = _states[document.Authority.WidgetId].LastGood!;
            return new(frame.Authority, CopyMedia(frame.Snapshot.EmbeddedMediaSession!));
        }
    }

    /// <summary>
    /// Forward a validated observation from this exact adapter. Event.Sequence is
    /// monotonically increasing per document. Once a send starts its sequence is
    /// consumed even if cancellation leaves delivery uncertain; never retry it.
    /// A command terminal must match the current exact command, including preference values.
    /// The bridge independently retains final playback authority.
    /// </summary>
    public async Task SendEmbeddedMediaPlaybackEventAsync(WidgetPresentationEmbeddedMediaDocument document,
        EmbeddedMediaPlaybackEvent playbackEvent, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(playbackEvent);
        cancellationToken.ThrowIfCancellationRequested();
        ValidateMediaEvent(playbackEvent);
        MediaDocumentEpoch epoch;
        CancellationToken lifetimeToken;
        lock (_gate)
        {
            if (!IsMediaDocumentCurrentLocked(document)) throw RetiredMedia(document.Authority.WidgetId);
            epoch = (MediaDocumentEpoch)document.Epoch;
            if (epoch.PendingEvents >= 16) throw Stale("embedded_media_saturated", document.Authority.WidgetId, "Media observations are saturated.");
            lifetimeToken = _lifetime.Token;
            ++epoch.PendingEvents;
        }
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, lifetimeToken, epoch.Retirement.Token);
        deadline.CancelAfter(_options.EmbeddedMediaTimeout);
        bool entered = false;
        try
        {
            await epoch.Events.WaitAsync(deadline.Token).ConfigureAwait(false);
            entered = true;
            WidgetPresentationAuthority authority;
            lock (_gate)
            {
                DemandMediaEpochLocked(document.Authority.WidgetId, epoch);
                var frame = _states[document.Authority.WidgetId].LastGood!;
                var media = frame.Snapshot.EmbeddedMediaSession!;
                if (playbackEvent.SessionId != media.Id || playbackEvent.Sequence <= epoch.LastEventSequence)
                    throw new BridgeProtocolException("Embedded media observation session or sequence is invalid.");
                ValidateMediaCommandTerminal(media.PendingCommand, playbackEvent);
                authority = frame.Authority;
                epoch.LastEventSequence = playbackEvent.Sequence;
            }
            await MediaRequestAsync(BridgeMessageTypes.EmbeddedMediaPlaybackEvent,
                new BridgeEmbeddedMediaPlaybackEventRequest(authority.WidgetId, authority.WidgetInstanceId,
                    authority.RuntimeGeneration, authority.PresentationGeneration, authority.SnapshotSequence, playbackEvent),
                BridgeMessageTypes.Acknowledged, deadline.Token).ConfigureAwait(false);
            deadline.Token.ThrowIfCancellationRequested();
            lock (_gate) DemandMediaEpochLocked(document.Authority.WidgetId, epoch);
        }
        finally
        {
            if (entered) epoch.Events.Release();
            lock (_gate) --epoch.PendingEvents;
        }
    }

    private async Task<BridgeEnvelope> MediaRequestAsync<T>(string type, T request, string expected, CancellationToken token)
    {
        // The transport preserves correlation for a canceled in-flight request.
        // Bound the caller wait even when server work cannot itself be canceled.
        var exchange = RequestAsync(type, request, expected, token);
        _ = exchange.ContinueWith(static task => { _ = task.Exception; }, CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        return await exchange.WaitAsync(token).ConfigureAwait(false);
    }

    private void ReconcileMediaDocumentLocked(WidgetPresentationState state)
    {
        var frame = state.LastGood;
        if (state.Failure is not null || frame?.Snapshot.EmbeddedMediaSession is not { } media)
        { RetireMediaDocumentLocked(state.WidgetId); return; }
        if (_mediaDocuments.TryGetValue(state.WidgetId, out var prior) &&
            SameMediaOwner(prior.Owner, frame.Authority) && SameMediaIdentity(prior.Identity, media)) return;
        RetireMediaDocumentLocked(state.WidgetId);
        _mediaDocuments[state.WidgetId] = new(frame.Authority, media);
    }
    private void RetireMediaDocumentLocked(string? widgetId = null)
    {
        if (widgetId is null)
        {
            foreach (var id in _mediaDocuments.Keys.ToArray()) RetireMediaDocumentLocked(id);
        }
        else if (_mediaDocuments.Remove(widgetId, out var retired))
            retired.Retirement.Cancel(); // Only internal linked waits observe this token.
    }
    private bool IsMediaDocumentCurrentLocked(WidgetPresentationEmbeddedMediaDocument document) =>
        !_disposed && _terminalFailure is null && ReferenceEquals(document.Owner, this) &&
        _mediaDocuments.TryGetValue(document.Authority.WidgetId, out var epoch) && ReferenceEquals(epoch, document.Epoch) &&
        _states.TryGetValue(document.Authority.WidgetId, out var state) && state.LastGood is { } frame &&
        SameMediaOwner(epoch.Owner, frame.Authority);
    private void DemandMediaEpochLocked(string widgetId, MediaDocumentEpoch epoch)
    {
        if (_disposed || _terminalFailure is not null || !_mediaDocuments.TryGetValue(widgetId, out var current) || !ReferenceEquals(current, epoch))
            throw RetiredMedia(widgetId);
    }
    private static WidgetPresentationSessionException RetiredMedia(string widgetId) =>
        Stale("embedded_media_stale", widgetId, "The embedded media document has retired.");
    private static bool SameMediaOwner(WidgetPresentationAuthority left, WidgetPresentationAuthority right) =>
        left.WidgetId == right.WidgetId && left.WidgetInstanceId == right.WidgetInstanceId && left.RuntimeGeneration == right.RuntimeGeneration &&
        left.PresentationGeneration == right.PresentationGeneration && left.SessionGeneration == right.SessionGeneration;
    private static bool SameMediaIdentity(EmbeddedMediaSession left, EmbeddedMediaSession right) =>
        left.Id == right.Id && left.EntryAsset == right.EntryAsset && left.Resources.SequenceEqual(right.Resources) &&
        left.AllowedFrameOrigins.SequenceEqual(right.AllowedFrameOrigins, StringComparer.Ordinal) &&
        left.AllowedFrameDomainFamilies.SequenceEqual(right.AllowedFrameDomainFamilies, StringComparer.Ordinal);
    private static EmbeddedMediaSession CopyMedia(EmbeddedMediaSession media) => media with
    {
        Resources = Array.AsReadOnly(media.Resources.Select(resource => resource with { }).ToArray()),
        Commands = Array.AsReadOnly(media.Commands.ToArray()),
        AllowedFrameOrigins = Array.AsReadOnly(media.AllowedFrameOrigins.ToArray()),
        AllowedFrameDomainFamilies = Array.AsReadOnly(media.AllowedFrameDomainFamilies.ToArray()),
        SupportedPresentations = Array.AsReadOnly(media.SupportedPresentations.ToArray()),
        PendingCommand = media.PendingCommand is { } command ? command with { } : null,
    };

    private static WidgetPresentationEmbeddedMediaResource[] ValidateMediaBundle(JsonElement payload,
        WidgetPresentationAuthority authority, EmbeddedMediaSession expected)
    {
        string[] required = ["widgetId", "instanceId", "runtimeGeneration", "presentationGeneration", "sequence",
            "sessionId", "entryAsset", "surface", "aspectRatio", "accessibleName", "commands", "allowedFrameOrigins",
            "allowedFrameDomainFamilies", "supportedPresentations", "resources"];
        RequireAllowedProperties(payload, required.Concat(["pendingCommand", "mediaSeekStepSeconds"]).ToHashSet(StringComparer.Ordinal), required);
        BridgeEmbeddedMediaBundle bundle;
        try { bundle = payload.Deserialize<BridgeEmbeddedMediaBundle>(BridgeJson.Options) ?? throw new BridgeProtocolException("Empty media bundle."); }
        catch (JsonException error) { throw new BridgeProtocolException("Malformed media bundle.", error); }
        if (bundle.WidgetId != authority.WidgetId || bundle.InstanceId != authority.WidgetInstanceId ||
            bundle.RuntimeGeneration != authority.RuntimeGeneration || bundle.PresentationGeneration != authority.PresentationGeneration ||
            bundle.Sequence != authority.SnapshotSequence || bundle.SessionId != expected.Id)
            throw new BridgeProtocolException("Media bundle authority differs from its request.");
        if (bundle.Resources is null || bundle.Resources.Count is < 1 or > ProtocolConstants.MaximumEmbeddedMediaResourceCount || bundle.Resources.Any(resource => resource is null))
            throw new BridgeProtocolException("Media resource inventory is invalid.");
        var actual = new EmbeddedMediaSession
        {
            Id = bundle.SessionId, EntryAsset = bundle.EntryAsset, AccessibleName = bundle.AccessibleName,
            Surface = bundle.Surface, AspectRatio = bundle.AspectRatio, Commands = bundle.Commands,
            Resources = bundle.Resources.Select(resource => new EmbeddedMediaResource { Path = resource.Path, ContentType = resource.ContentType }).ToArray(),
            AllowedFrameOrigins = bundle.AllowedFrameOrigins, AllowedFrameDomainFamilies = bundle.AllowedFrameDomainFamilies,
            PendingCommand = bundle.PendingCommand, SupportedPresentations = bundle.SupportedPresentations, MediaSeekStepSeconds = bundle.MediaSeekStepSeconds,
        };
        var validation = ViewSnapshotValidator.Validate(new ViewSnapshot
        {
            Sequence = authority.SnapshotSequence, WidgetInstanceId = authority.WidgetInstanceId, ActiveInputScopeId = "media-validation",
            Root = new ViewNode { Id = "media-validation", Kind = ViewNodeKind.Stack }, EmbeddedMediaSession = actual,
        });
        if (validation.Count != 0 || !SameMediaIdentity(expected, actual) || actual.Surface != expected.Surface ||
            actual.AspectRatio != expected.AspectRatio || actual.AccessibleName != expected.AccessibleName ||
            !actual.Commands.SequenceEqual(expected.Commands) || !actual.SupportedPresentations.SequenceEqual(expected.SupportedPresentations) ||
            actual.PendingCommand != expected.PendingCommand || actual.MediaSeekStepSeconds != expected.MediaSeekStepSeconds)
            throw new BridgeProtocolException("Media bundle declaration does not match the admitted protocol contract.");
        var resources = new List<WidgetPresentationEmbeddedMediaResource>();
        long total = 0;
        foreach (var resource in bundle.Resources)
        {
            if (resource.Sha256 is not { Length: 64 } || !resource.Sha256.All(Uri.IsHexDigit) || resource.ContentBase64 is null ||
                resource.ContentBase64.Length > ((ProtocolConstants.MaximumEmbeddedMediaResourceBytes + 2) / 3) * 4)
                throw new BridgeProtocolException("Media resource transfer metadata exceeds its bounds.");
            byte[] bytes;
            try { bytes = Convert.FromBase64String(resource.ContentBase64); }
            catch (FormatException error) { throw new BridgeProtocolException("Media resource base64 is invalid.", error); }
            total += bytes.Length;
            if (bytes.Length > ProtocolConstants.MaximumEmbeddedMediaResourceBytes || total > ProtocolConstants.MaximumEmbeddedMediaAggregateBytes ||
                !Convert.ToHexString(SHA256.HashData(bytes)).Equals(resource.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new BridgeProtocolException("Media resource bytes fail size or SHA256 validation.");
            resources.Add(new(resource.Path, resource.ContentType, resource.Sha256, bytes));
        }
        return resources.ToArray();
    }

    private static void ValidateMediaEvent(EmbeddedMediaPlaybackEvent value)
    {
        static bool Identifier(string? text) => text is not null && ProtocolValidationIdentifierContext.IsSafeIdentifier(text);
        if (!Identifier(value.SessionId) || !Identifier(value.MediaKey) || value.Sequence <= 0 || value.CommandSequence < 0 ||
            !Enum.IsDefined(value.State) || !double.IsFinite(value.PositionSeconds) || !double.IsFinite(value.DurationSeconds) ||
            !double.IsFinite(value.Volume) || !double.IsFinite(value.PlaybackRate) || value.PositionSeconds < 0 || value.DurationSeconds < 0 ||
            value.PositionSeconds > value.DurationSeconds || value.DurationSeconds > 86_400 || value.Volume is < 0 or > 1 ||
            value.PlaybackRate < ProtocolConstants.MinimumEmbeddedMediaPlaybackRate || value.PlaybackRate > ProtocolConstants.MaximumEmbeddedMediaPlaybackRate ||
            value.ErrorCode is { } error && !Identifier(error))
            throw new BridgeProtocolException("Embedded media observation is invalid.");
    }
    private static void ValidateMediaCommandTerminal(EmbeddedMediaPlaybackCommand? command, EmbeddedMediaPlaybackEvent value)
    {
        if (value.CommandSequence == 0) return;
        if (command is null || command.Sequence != value.CommandSequence || command.MediaKey != value.MediaKey ||
            (value.ErrorCode is null && ((command.Kind == EmbeddedMediaPlaybackCommandKind.SetPlaybackRate && command.PlaybackRate != value.PlaybackRate) ||
            (command.Kind == EmbeddedMediaPlaybackCommandKind.SetMuted && command.Muted != value.Muted) ||
            (command.Kind == EmbeddedMediaPlaybackCommandKind.SetLoop && command.Loop != value.Loop))))
            throw new BridgeProtocolException("Embedded media command terminal is stale or mismatched.");
    }
}
