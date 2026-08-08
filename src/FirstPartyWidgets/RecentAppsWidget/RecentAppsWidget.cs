using System.Security.Cryptography;
using System.Text;
using GameBarAlternative.WidgetProtocol;
using GameBarAlternative.WidgetSdk;

namespace GameBarAlternative.FirstPartyWidgets.RecentApps;

public enum RecentAppsViewState
{
    Initial,
    Loading,
    Ready,
    Empty,
    PermissionDenied,
    LifecycleDenied,
    ChannelClosed,
    ServiceUnavailable,
    Error,
}

/// <summary>
/// Controller-first recent foreground applications. The worker sees only
/// sanitized names and opaque host IDs; activation remains in the trusted provider.
/// </summary>
public sealed class RecentAppsWidget : Widget
{
    private static readonly WidgetSurfaceHints CompactSurface = new()
    {
        Mode = WidgetSurfaceMode.Compact,
        PreferredWidth = 520,
        PreferredHeight = 500,
        MinimumWidth = 320,
        MinimumHeight = 340,
    };

    private readonly object _gate = new();
    private IReadOnlyList<WidgetRecentActivity> _activities = [];
    private IReadOnlyDictionary<string, string> _activityByElementId =
        new Dictionary<string, string>(StringComparer.Ordinal);
    private RecentAppsViewState _viewState = RecentAppsViewState.Initial;
    private string _status = "Recent activity loads when this widget becomes visible";
    private string? _selectedActivityId;
    private string? _pendingActivityId;
    private CancellationTokenSource? _runLifetime;
    private long _runGeneration;

    public RecentAppsViewState ViewState { get { lock (_gate) return _viewState; } }
    public IReadOnlyList<WidgetRecentActivity> Activities
    {
        get { lock (_gate) return _activities.ToArray(); }
    }
    public bool ControlBusy { get { lock (_gate) return _pendingActivityId is not null; } }
    public string Status { get { lock (_gate) return _status; } }

    public override WidgetView Render()
    {
        IReadOnlyList<WidgetRecentActivity> activities;
        RecentAppsViewState state;
        string status;
        string? selectedId;
        string? pendingId;
        lock (_gate)
        {
            activities = _activities;
            state = _viewState;
            status = _status;
            selectedId = _selectedActivityId;
            pendingId = _pendingActivityId;
        }

        var header = UI.Stack("recent.header",
                UI.Text("CONTROL CENTER", "recent.eyebrow", "Control Center")
                    .Classes("recent-eyebrow"),
                UI.Text("Recent Apps", "recent.title", "Recently observed applications")
                    .Classes("recent-title"),
                UI.Text(status, "recent.status", status).Classes(
                    "recent-status", state == RecentAppsViewState.Ready ? "is-live" :
                    state is RecentAppsViewState.PermissionDenied or RecentAppsViewState.Error
                        ? "is-error" : "is-neutral"))
            .Classes("recent-header");

        if (state != RecentAppsViewState.Ready || activities.Count == 0)
            return RenderState(header, state);

        var elementIds = activities.Select(activity => ElementId(activity.ActivityId)).ToArray();
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        var rows = new WidgetElement[activities.Count];
        for (var index = 0; index < activities.Count; index++)
        {
            var activity = activities[index];
            var id = elementIds[index];
            map[id] = activity.ActivityId;
            var mostRecent = activity.IsMostRecent;
            var pending = string.Equals(activity.ActivityId, pendingId, StringComparison.Ordinal);
            var kind = activity.Kind switch
            {
                WidgetRecentActivityKind.Game => "GAME",
                WidgetRecentActivityKind.Application => "APP",
                _ => "ACTIVITY",
            };
            var detail = pending ? "SWITCHING…" : mostRecent ? "MOST RECENT" : "RUNNING";
            var button = UI.Button(activity.DisplayName, "recent.activate", id)
                .Icon(WidgetGlyph.Play,
                    $"{activity.DisplayName}. {kind}. {detail}. Press A to switch")
                // Recency and controller selection are independent states. The
                // recency class/meta labels the latest observation, while only
                // the persisted controller target gets IsSelected.
                .Selected(string.Equals(activity.ActivityId, selectedId, StringComparison.Ordinal))
                .Busy(pending)
                .Disabled(LifecycleState != WidgetLifecycleState.Interactive || pending || !activity.IsRunning)
                .FocusUp(elementIds[Math.Max(0, index - 1)])
                .FocusDown(elementIds[Math.Min(activities.Count - 1, index + 1)])
                .FocusLeft(id)
                .FocusRight(id)
                .Classes("recent-item-button", mostRecent ? "is-most-recent" : "is-running");
            rows[index] = UI.Stack(id + ".row",
                    button,
                    UI.Row(id + ".meta",
                            UI.Text(kind, id + ".kind", kind).Classes("recent-item-kind"),
                            UI.Text(detail, id + ".state", detail).Classes(
                                "recent-item-state", mostRecent ? "is-most-recent" : "is-running"))
                        .Classes("recent-item-meta"))
                .Classes("recent-item", mostRecent ? "is-most-recent" : "is-running");
        }
        lock (_gate) _activityByElementId = map;

        var root = UI.Stack("recent.root",
                header,
                UI.Row("recent.section.heading",
                        UI.Text("RECENTLY OBSERVED", "recent.section.label", "Recently observed applications")
                            .Classes("recent-section-label"),
                        UI.Text($"{activities.Count} running", "recent.section.count",
                                $"{activities.Count} running applications")
                            .Classes("recent-section-count"))
                    .Classes("recent-section-heading"),
                UI.VerticalScroll("recent.scroll", rows).Classes("recent-list"))
            .InputScope("recent-apps")
            .Classes("recent-apps-widget");
        var selected = activities.FirstOrDefault(activity =>
            string.Equals(activity.ActivityId, selectedId, StringComparison.Ordinal)) ?? activities[0];
        return new WidgetView(root, ElementId(selected.ActivityId), Surface: CompactSurface);
    }

    protected override ValueTask OnActivatedAsync(CancellationToken activeLifetime)
    {
        StartActiveRun(activeLifetime);
        return ValueTask.CompletedTask;
    }

    protected override ValueTask OnLifecycleStateChangedAsync(
        WidgetLifecycleState previous,
        WidgetLifecycleState current,
        CancellationToken stateLifetime)
    {
        if (previous is WidgetLifecycleState.Visible or WidgetLifecycleState.Interactive ||
            current is WidgetLifecycleState.Visible or WidgetLifecycleState.Interactive)
            Invalidate();
        return ValueTask.CompletedTask;
    }

    protected override ValueTask OnDeactivatedAsync(CancellationToken transitionToken)
    {
        StopActiveRun();
        return ValueTask.CompletedTask;
    }

    protected override ValueTask OnDestroyingAsync(CancellationToken shutdownToken)
    {
        StopActiveRun();
        return ValueTask.CompletedTask;
    }

    public override async ValueTask OnActionAsync(
        WidgetActionEvent action,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(action);
        if (action.ActionId == "retry")
        {
            if (IsActive) StartActiveRun(ActiveCancellationToken);
            return;
        }
        if (action.ActionId != "recent.activate") return;
        string? activityId;
        lock (_gate) _activityByElementId.TryGetValue(action.SourceElementId, out activityId);
        if (activityId is null) return;
        if (LifecycleState != WidgetLifecycleState.Interactive)
        {
            SetFeedback("Enter the widget before switching apps", error: false);
            return;
        }
        lock (_gate)
        {
            if (_pendingActivityId is not null) return;
            _pendingActivityId = activityId;
            _selectedActivityId = activityId;
            _status = "Switching application…";
        }
        var commandGeneration = Volatile.Read(ref _runGeneration);
        Invalidate();
        try
        {
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken, ActiveCancellationToken);
            await HostServices.RecentActivity.ActivateAsync(activityId, linked.Token)
                .ConfigureAwait(false);
            lock (_gate)
            {
                if (_runGeneration != commandGeneration) return;
                _pendingActivityId = null;
                _status = "Switched to running application";
            }
            Invalidate();
        }
        catch (OperationCanceledException) when (ActiveCancellationToken.IsCancellationRequested)
        {
        }
        catch (WidgetCapabilityUnavailableException)
        {
            lock (_gate)
            {
                if (_runGeneration == commandGeneration)
                    _status = "Recent activity service unavailable";
            }
            Invalidate();
        }
        catch (WidgetCapabilityException exception)
        {
            lock (_gate)
            {
                if (_runGeneration != commandGeneration) return;
                _pendingActivityId = null;
                if (exception.ErrorCode == "resource_not_found")
                    _activities = _activities.Where(item => item.ActivityId != activityId).ToArray();
                _status = exception.ErrorCode switch
                {
                    "resource_not_found" => "That application is no longer running",
                    "activation_denied" => "Windows did not allow the app switch",
                    "permission_denied" => "App switching permission is off",
                    "lifecycle_denied" => "Enter the widget before switching apps",
                    _ => "Could not switch applications",
                };
                _viewState = _activities.Count == 0 ? RecentAppsViewState.Empty : RecentAppsViewState.Ready;
            }
            Invalidate();
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            lock (_gate)
            {
                if (_runGeneration == commandGeneration)
                    _status = "Could not switch applications";
            }
            Invalidate();
        }
        finally
        {
            var changed = false;
            lock (_gate)
            {
                if (_runGeneration == commandGeneration &&
                    string.Equals(_pendingActivityId, activityId, StringComparison.Ordinal))
                {
                    _pendingActivityId = null;
                    changed = true;
                }
            }
            if (changed) Invalidate();
        }
    }

    private WidgetView RenderState(StackElement header, RecentAppsViewState state)
    {
        var (title, help) = state switch
        {
            RecentAppsViewState.Initial or RecentAppsViewState.Loading =>
                ("Watching for activity", "Recently observed running applications will appear here."),
            RecentAppsViewState.Empty =>
                ("No recent apps yet", "Switch to an application, then reopen the overlay."),
            RecentAppsViewState.PermissionDenied =>
                ("Recent activity access is off", "Allow Recent Apps in Settings > Permissions."),
            RecentAppsViewState.LifecycleDenied =>
                ("Recent activity paused", "Return to this widget to refresh recent applications."),
            RecentAppsViewState.ChannelClosed =>
                ("Activity service disconnected", "Retry the trusted Windows activity service."),
            RecentAppsViewState.ServiceUnavailable =>
                ("Activity service unavailable", "The Windows foreground observer is unavailable."),
            _ => ("Recent activity unavailable", "Try again. No process details were exposed."),
        };
        var retry = UI.Button("Try again", "retry", "recent.retry")
            .Icon(WidgetGlyph.Refresh, "Reload recent applications")
            .Disabled(!IsActive)
            .Classes("recent-retry");
        var root = UI.Stack("recent.root",
                header,
                UI.Stack("recent.state",
                        UI.Text(title, "recent.state.title", title).Classes("recent-state-title"),
                        UI.Text(help, "recent.state.help", help).Classes("recent-state-help"),
                        retry)
                    .Classes("recent-state-card"))
            .InputScope("recent-apps")
            .Classes("recent-apps-widget", "has-state");
        return new WidgetView(root, "recent.retry", Surface: CompactSurface);
    }

    private void StartActiveRun(CancellationToken activeLifetime)
    {
        var prior = Interlocked.Exchange(ref _runLifetime, null);
        prior?.Cancel();
        prior?.Dispose();
        var lifetime = CancellationTokenSource.CreateLinkedTokenSource(activeLifetime);
        _runLifetime = lifetime;
        var generation = Interlocked.Increment(ref _runGeneration);
        lock (_gate)
        {
            _viewState = RecentAppsViewState.Loading;
            _status = "Loading recently observed applications…";
        }
        Invalidate();
        _ = ObserveAsync(generation, lifetime.Token);
    }

    private void StopActiveRun()
    {
        var lifetime = Interlocked.Exchange(ref _runLifetime, null);
        lifetime?.Cancel();
        lifetime?.Dispose();
    }

    private async Task ObserveAsync(long generation, CancellationToken cancellationToken)
    {
        try
        {
            await using var subscription = await HostServices.RecentActivity
                .OpenSubscriptionAsync(cancellationToken).ConfigureAwait(false);
            var snapshot = await HostServices.RecentActivity.GetRecentAsync(cancellationToken)
                .ConfigureAwait(false);
            Apply(snapshot, generation);
            await foreach (var change in subscription.ReadAllAsync(cancellationToken)
                               .WithCancellation(cancellationToken).ConfigureAwait(false))
                Apply(change.Activities, generation);
            SetError(RecentAppsViewState.ChannelClosed, "Activity service disconnected", generation);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (WidgetCapabilityUnavailableException)
        {
            SetError(RecentAppsViewState.ServiceUnavailable, "Activity service unavailable", generation);
        }
        catch (WidgetCapabilityException exception)
        {
            var state = exception.ErrorCode switch
            {
                "permission_denied" or "capability_not_declared" or "capability_revoked" =>
                    RecentAppsViewState.PermissionDenied,
                "lifecycle_denied" => RecentAppsViewState.LifecycleDenied,
                "channel_closed" => RecentAppsViewState.ChannelClosed,
                "platform_unavailable" => RecentAppsViewState.ServiceUnavailable,
                _ => RecentAppsViewState.Error,
            };
            SetError(state, "Recent activity could not be loaded", generation);
        }
        catch (Exception)
        {
            SetError(RecentAppsViewState.Error, "Recent activity could not be loaded", generation);
        }
    }

    private void Apply(IReadOnlyList<WidgetRecentActivity>? incoming, long generation)
    {
        var normalized = Normalize(incoming);
        lock (_gate)
        {
            if (_runGeneration != generation) return;
            _activities = normalized;
            if (_selectedActivityId is null ||
                !normalized.Any(item => item.ActivityId == _selectedActivityId))
                _selectedActivityId = normalized.FirstOrDefault()?.ActivityId;
            _pendingActivityId = _pendingActivityId is not null &&
                normalized.Any(item => item.ActivityId == _pendingActivityId)
                ? _pendingActivityId : null;
            _viewState = normalized.Count == 0 ? RecentAppsViewState.Empty : RecentAppsViewState.Ready;
            _status = normalized.Count == 0
                ? "Waiting for foreground activity"
                : $"{normalized.Count} recently observed · updated live";
        }
        Invalidate();
    }

    private static IReadOnlyList<WidgetRecentActivity> Normalize(
        IReadOnlyList<WidgetRecentActivity>? incoming)
    {
        if (incoming is null) return [];
        var ids = new HashSet<string>(StringComparer.Ordinal);
        return incoming.Where(item => item is not null && item.IsRunning &&
                !string.IsNullOrWhiteSpace(item.ActivityId) && ids.Add(item.ActivityId) &&
                !string.IsNullOrWhiteSpace(item.DisplayName))
            .Take(16)
            .Select(item => item with
            {
                DisplayName = item.DisplayName.Trim().Length > 160
                    ? item.DisplayName.Trim()[..160] : item.DisplayName.Trim(),
            })
            .ToArray();
    }

    private void SetError(RecentAppsViewState state, string status, long generation)
    {
        lock (_gate)
        {
            if (_runGeneration != generation) return;
            _viewState = state;
            _status = status;
            _activities = [];
            _pendingActivityId = null;
        }
        Invalidate();
    }

    private void SetFeedback(string status, bool error)
    {
        lock (_gate) _status = status;
        Invalidate();
    }

    private static string ElementId(string opaqueId)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(opaqueId));
        return "recent.item." + Convert.ToHexString(hash.AsSpan(0, 10)).ToLowerInvariant();
    }
}
