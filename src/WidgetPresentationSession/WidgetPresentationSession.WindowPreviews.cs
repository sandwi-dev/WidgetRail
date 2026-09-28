using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Text.Json;
using WidgetRail.WidgetBridge;
using WidgetRail.WidgetProtocol;

namespace WidgetRail.WidgetPresentationSession;

public sealed partial class WidgetPresentationSession
{
    private sealed record WindowPreviewEpoch(WidgetPresentationAuthority Owner, WidgetHostWindowTarget Target);
    private readonly Dictionary<string, Dictionary<string, WindowPreviewEpoch>> _windowPreviewEpochs = new(StringComparer.Ordinal);
    private readonly Dictionary<string, object> _windowPreviewPermissions = new(StringComparer.Ordinal);
    private bool _previewPermissionsPending;
    private const int MaximumWindowPreviewTargets = 64;
    private static readonly TimeSpan PreviewGrantLifetime = TimeSpan.FromSeconds(2);

    /// <summary>
    /// Resolve a bounded consent refresh for a genuine displayed frame. Compatible
    /// snapshots can advance; removing/replacing a target permanently retires that
    /// target's old grant. Only one permission request is admitted at a time.
    /// </summary>
    public async Task<WidgetWindowPreviewGrant> RefreshWindowPreviewPermissionsAsync(WidgetPresentationFrame displayed,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(displayed);
        cancellationToken.ThrowIfCancellationRequested();
        Dictionary<string, WindowPreviewEpoch> captured;
        CancellationToken lifetime;
        lock (_gate)
        {
            DemandPreviewFrameLocked(displayed);
            if (_previewPermissionsPending) throw PreviewStale("Window preview permission refresh is already pending.");
            captured = _windowPreviewEpochs.GetValueOrDefault(displayed.Authority.WidgetId)?
                .Where(pair => displayed.WindowPreviews.GetValueOrDefault(pair.Key) == pair.Value.Target)
                .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal) ?? [];
            lifetime = _lifetime.Token;
            _previewPermissionsPending = true;
        }
        var completed = false;
        try
        {
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, lifetime);
            deadline.CancelAfter(PreviewGrantLifetime);
            var exchange = RequestAsync(BridgeMessageTypes.WindowPreviewPermissions, new { widgetId = displayed.Authority.WidgetId },
                BridgeMessageTypes.WindowPreviewPermissions, deadline.Token);
            _ = exchange.ContinueWith(static task => { _ = task.Exception; }, CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
            var response = await exchange.WaitAsync(deadline.Token).ConfigureAwait(false);
            RequireObjectProperties(response.Payload, "allowedWindowIds");
            var entries = response.Payload.GetProperty("allowedWindowIds");
            if (entries.ValueKind != JsonValueKind.Array || entries.GetArrayLength() > MaximumWindowPreviewTargets)
                throw new BridgeProtocolException("Window preview permission inventory exceeds its bounds.");
            var allowed = new HashSet<string>(StringComparer.Ordinal);
            foreach (var entry in entries.EnumerateArray())
            {
                if (entry.ValueKind != JsonValueKind.String || entry.GetString() is not { } id ||
                    !ProtocolValidationIdentifierContext.IsSafeIdentifier(id) || !allowed.Add(id))
                    throw new BridgeProtocolException("Window preview permission identity is invalid or duplicated.");
            }
            lock (_gate)
            {
                deadline.Token.ThrowIfCancellationRequested();
                DemandPreviewFrameLocked(displayed);
                var current = _windowPreviewEpochs.GetValueOrDefault(displayed.Authority.WidgetId);
                var retained = captured.Where(pair => allowed.Contains(pair.Key) && current is not null &&
                    ReferenceEquals(current.GetValueOrDefault(pair.Key), pair.Value)).ToArray();
                var permissionEpoch = new object();
                _windowPreviewPermissions[displayed.Authority.WidgetId] = permissionEpoch;
                completed = true;
                return new(this, displayed.Authority, retained.ToDictionary(pair => pair.Key, pair => (object)pair.Value, StringComparer.Ordinal),
                    retained.ToDictionary(pair => pair.Key, pair => pair.Value.Target, StringComparer.Ordinal), permissionEpoch, Stopwatch.GetTimestamp());
            }
        }
        finally
        {
            lock (_gate)
            {
                _previewPermissionsPending = false;
                if (!completed) _windowPreviewPermissions.Remove(displayed.Authority.WidgetId);
            }
        }
    }

    public bool IsWindowPreviewCurrent(WidgetWindowPreviewGrant grant, string windowId)
    {
        ArgumentNullException.ThrowIfNull(grant);
        lock (_gate)
            return !_disposed && _terminalFailure is null && ReferenceEquals(grant.Owner, this) &&
                Stopwatch.GetElapsedTime(grant.IssuedAt) < PreviewGrantLifetime &&
                ReferenceEquals(_windowPreviewPermissions.GetValueOrDefault(grant.Authority.WidgetId), grant.PermissionEpoch) &&
                grant.Epochs.TryGetValue(windowId, out var epoch) &&
                _windowPreviewEpochs.TryGetValue(grant.Authority.WidgetId, out var current) &&
                ReferenceEquals(current.GetValueOrDefault(windowId), epoch);
    }

    private void DemandPreviewFrameLocked(WidgetPresentationFrame displayed)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ThrowIfTerminalLocked();
        if (!_options.WindowPreviews || !_publishedInputFrames.TryGetValue(displayed, out _) ||
            _states.GetValueOrDefault(displayed.Authority.WidgetId) is not { Failure: null, LastGood: { } current } ||
            !SameIndexedOwner(displayed.Authority, current.Authority))
            throw PreviewStale("The displayed window-preview owner is unavailable.");
    }
    private static WidgetPresentationSessionException PreviewStale(string message) => new("window_preview_stale", message);

    private IReadOnlyDictionary<string, WidgetHostWindowTarget> ReadWindowPreviewInventory(JsonElement payload, ViewSnapshot snapshot)
    {
        var result = new Dictionary<string, WidgetHostWindowTarget>(StringComparer.Ordinal);
        if (!payload.TryGetProperty("windowPreviews", out var inventory) || inventory.ValueKind == JsonValueKind.Null)
            return new ReadOnlyDictionary<string, WidgetHostWindowTarget>(result);
        if (!_options.WindowPreviews || inventory.ValueKind != JsonValueKind.Object)
            throw new BridgeProtocolException("Window preview identities were not negotiated.");
        var declared = new HashSet<string>(StringComparer.Ordinal);
        Collect(snapshot.Root);
        foreach (var entry in inventory.EnumerateObject())
        {
            if (result.Count >= MaximumWindowPreviewTargets || !declared.Contains(entry.Name) ||
                !result.TryAdd(entry.Name, ReadHostWindowTarget(entry.Name, entry.Value)))
                throw new BridgeProtocolException("Window preview inventory is undeclared, duplicated or too large.");
        }
        return new ReadOnlyDictionary<string, WidgetHostWindowTarget>(result);
        void Collect(ViewNode node)
        {
            if (node.Kind == ViewNodeKind.WindowPreview && node.WindowId is { } id) declared.Add(id);
            foreach (var child in node.Children) Collect(child);
            if (node.FocusPresentation is { } focus) Collect(focus);
            if (node.DefaultFocusPresentation is { } fallback) Collect(fallback);
        }
    }

    private void ReconcileWindowPreviewsLocked(WidgetPresentationState state)
    {
        if (state.Failure is not null || state.LastGood is not { } frame || frame.WindowPreviews.Count == 0)
        { RetireWindowPreviewsLocked(state.WidgetId); return; }
        if (!_windowPreviewEpochs.TryGetValue(state.WidgetId, out var epochs)) _windowPreviewEpochs.Add(state.WidgetId, epochs = new(StringComparer.Ordinal));
        foreach (var id in epochs.Keys.ToArray())
            if (!frame.WindowPreviews.ContainsKey(id)) epochs.Remove(id);
        foreach (var (id, target) in frame.WindowPreviews)
            if (!epochs.TryGetValue(id, out var epoch) || !SameIndexedOwner(epoch.Owner, frame.Authority) || epoch.Target != target)
                epochs[id] = new(frame.Authority, target);
    }
    private void RetireWindowPreviewsLocked(string? widgetId = null)
    {
        if (widgetId is null) { _windowPreviewEpochs.Clear(); _windowPreviewPermissions.Clear(); }
        else { _windowPreviewEpochs.Remove(widgetId); _windowPreviewPermissions.Remove(widgetId); }
    }
}
