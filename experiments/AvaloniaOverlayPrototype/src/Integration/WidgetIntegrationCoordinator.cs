using System.Collections.ObjectModel;
using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using GameBarAlternative.AvaloniaPrototype.Presentation;
using GameBarAlternative.WidgetBridge;
using GameBarAlternative.WidgetPresentationSession;
using GameBarAlternative.WidgetProtocol;
using GameBarAlternative.WidgetRuntime;
using GameBarAlternative.WidgetSdk;

namespace GameBarAlternative.AvaloniaPrototype.Integration;

public sealed record WidgetTrayItem(
    string Id,
    string Name,
    WidgetGlyph Icon,
    string InstanceId,
    string RuntimeGeneration,
    string PresentationGeneration);

public sealed partial class IntegratedShellViewModel : ObservableObject
{
    public ObservableCollection<WidgetTrayItem> Widgets { get; } = [];

    [ObservableProperty] private string? selectedWidgetId;
    [ObservableProperty] private string selectedWidgetName = "Widgets";
    [ObservableProperty] private string statusText = "Connecting to the retained widget runtime…";
    [ObservableProperty] private bool isBusy = true;
    [ObservableProperty] private bool hasFailure;
    [ObservableProperty] private WidgetPresentationFrame? currentFrame;
}

public sealed class WidgetIntegrationCoordinator : IAsyncDisposable
{
    private readonly IPresentationSessionClient session;
    private readonly IPresentationScheduler scheduler;
    private readonly CancellationTokenSource lifetime = new();
    private readonly object gate = new();
    private CancellationTokenSource? selection;
    private WidgetPresentationTarget? activeTarget;
    private long inputSequence;
    private bool visible = true;
    private bool disposed;

    public WidgetIntegrationCoordinator(
        IPresentationSessionClient session,
        IPresentationScheduler scheduler)
    {
        this.session = session;
        this.scheduler = scheduler;
        session.PresentationChanged += OnPresentationChanged;
        session.DiagnosticPublished += OnDiagnosticPublished;
    }

    public IntegratedShellViewModel ViewModel { get; } = new();

    public event EventHandler<WidgetPresentationFrame>? FramePublished;

    public WidgetPresentationFrame? CurrentFrame => ViewModel.CurrentFrame;

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token, cancellationToken);
        var catalog = await session.ListWidgetsAsync(linked.Token).ConfigureAwait(false);
        await scheduler.InvokeAsync(() =>
        {
            ViewModel.Widgets.Clear();
            foreach (var descriptor in catalog.Widgets)
            {
                ViewModel.Widgets.Add(new WidgetTrayItem(
                    descriptor.Id,
                    descriptor.Name,
                    descriptor.Icon,
                    descriptor.InstanceId,
                    descriptor.RuntimeGeneration,
                    descriptor.PresentationGeneration));
            }
            ViewModel.StatusText = catalog.Widgets.Count == 0
                ? "No widgets are available in the current catalog."
                : $"{catalog.Widgets.Count} widgets available";
            ViewModel.IsBusy = catalog.Widgets.Count > 0;
        }).ConfigureAwait(false);

        if (catalog.Widgets.FirstOrDefault() is { } first)
            await SelectWidgetAsync(first.Id, linked.Token).ConfigureAwait(false);
    }

    public async Task SelectWidgetAsync(string widgetId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(widgetId);
        CancellationTokenSource ownedSelection;
        WidgetPresentationTarget? prior;
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            selection?.Cancel();
            selection?.Dispose();
            selection = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token, cancellationToken);
            ownedSelection = selection;
            prior = activeTarget;
        }

        var target = session.GetTarget(widgetId);
        await scheduler.InvokeAsync(() =>
        {
            ViewModel.SelectedWidgetId = target.Descriptor.Id;
            ViewModel.SelectedWidgetName = target.Descriptor.Name;
            ViewModel.StatusText = $"Opening {target.Descriptor.Name}…";
            ViewModel.IsBusy = true;
            ViewModel.HasFailure = false;
        }).ConfigureAwait(false);

        try
        {
            if (prior is not null && !string.Equals(prior.Descriptor.Id, widgetId, StringComparison.Ordinal))
                await session.SetLifecycleAsync(prior, WidgetLifecycleState.Background, ownedSelection.Token)
                    .ConfigureAwait(false);

            var frame = await session.EstablishPresentationAsync(
                target,
                visible ? WidgetLifecycleState.Interactive : WidgetLifecycleState.Background,
                ownedSelection.Token).ConfigureAwait(false);
            lock (gate)
            {
                if (!ReferenceEquals(selection, ownedSelection) || ownedSelection.IsCancellationRequested) return;
                activeTarget = target;
            }
            await PublishFrameAsync(frame, ownedSelection.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ownedSelection.IsCancellationRequested)
        {
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            await PublishFailureAsync(exception.Message).ConfigureAwait(false);
        }
    }

    public async Task DispatchAsync(SemanticActionRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var frame = CurrentFrame ?? throw new InvalidOperationException("There is no current presentation frame.");
        var current = FindNode(frame.Snapshot.Root, request.SourceElementId)
            ?? throw new InvalidOperationException("The source element is not in the latest presentation snapshot.");
        var declared = string.Equals(current.ActionId, request.ActionId, StringComparison.Ordinal) ||
            string.Equals(current.ValueChangedActionId, request.ActionId, StringComparison.Ordinal);
        if (!declared)
            throw new InvalidOperationException("The action is not declared by the current source element.");
        if (request.RequestedValue is not null &&
            !string.Equals(current.ValueChangedActionId, request.ActionId, StringComparison.Ordinal))
            throw new InvalidOperationException("Only the current slider value action accepts a requested value.");
        if (request.CommittedText is not null && current.Kind != ViewNodeKind.TextEntry)
            throw new InvalidOperationException("Only the current text-entry element accepts committed text.");

        var sequence = Interlocked.Increment(ref inputSequence);
        var action = new WidgetActionEvent(
            request.ActionId,
            request.SourceElementId,
            Sequence: sequence,
            MonotonicTimestampMicroseconds: MonotonicMicroseconds(),
            RequestedValue: request.RequestedValue,
            InputScopeId: frame.Authority.ActiveInputScopeId)
        {
            CommittedText = request.CommittedText,
        };
        await session.SendActionAsync(frame.Authority, action, cancellationToken).ConfigureAwait(false);
    }

    public async Task<bool> SendControllerInputAsync(
        ControllerButton button,
        ControllerEventPhase phase,
        string? focusedElementId,
        double? requestedValue = null,
        CancellationToken cancellationToken = default)
    {
        var frame = CurrentFrame;
        if (frame is null) return false;
        var input = new ControllerInputEvent(
            button,
            phase,
            ControllerInputContext.OpenWidget,
            focusedElementId,
            Interlocked.Increment(ref inputSequence),
            MonotonicMicroseconds(),
            frame.Authority.ActiveInputScopeId,
            frame.Authority.SnapshotSequence,
            requestedValue);
        return await session.SendControllerInputAsync(frame.Authority, input, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task InvokeQuickActionAsync(
        string quickActionId,
        CancellationToken cancellationToken = default)
    {
        WidgetPresentationTarget? target;
        lock (gate) target = activeTarget;
        if (target is null) return;
        await session.InvokeQuickActionAsync(
            target,
            quickActionId,
            Interlocked.Increment(ref inputSequence),
            MonotonicMicroseconds(),
            cancellationToken).ConfigureAwait(false);
    }

    public async Task<bool> InvokeQuickActionForButtonAsync(
        ControllerButton button,
        CancellationToken cancellationToken = default)
    {
        WidgetPresentationTarget? target;
        lock (gate) target = activeTarget;
        var quickAction = target?.Descriptor.QuickActions
            .FirstOrDefault(action => action.ControllerButton == button);
        if (target is null || quickAction is null) return false;
        await session.InvokeQuickActionAsync(
            target,
            quickAction.Id,
            Interlocked.Increment(ref inputSequence),
            MonotonicMicroseconds(),
            cancellationToken).ConfigureAwait(false);
        return true;
    }

    public async Task RestartActiveAsync(CancellationToken cancellationToken = default)
    {
        WidgetPresentationTarget? target;
        lock (gate) target = activeTarget;
        if (target is null) return;
        await session.RestartAsync(target, cancellationToken).ConfigureAwait(false);
        var frame = await session.EstablishPresentationAsync(
            target, visible ? WidgetLifecycleState.Interactive : WidgetLifecycleState.Background,
            cancellationToken).ConfigureAwait(false);
        await PublishFrameAsync(frame, cancellationToken).ConfigureAwait(false);
    }

    public async Task<ReadOnlyMemory<byte>> ResolveArtworkAsync(
        WidgetPresentationAuthority authority,
        string artworkHandle,
        CancellationToken cancellationToken = default) =>
        (await session.ResolveArtworkAsync(authority, artworkHandle, cancellationToken)
            .ConfigureAwait(false)).PngBytes;

    public async Task SetVisibleAsync(bool isVisible, CancellationToken cancellationToken = default)
    {
        visible = isVisible;
        WidgetPresentationTarget? target;
        lock (gate) target = activeTarget;
        if (target is null) return;
        if (!isVisible)
        {
            selection?.Cancel();
            await session.SetLifecycleAsync(target, WidgetLifecycleState.Background, cancellationToken)
                .ConfigureAwait(false);
            return;
        }

        var frame = await session.EstablishPresentationAsync(
            target, WidgetLifecycleState.Interactive, cancellationToken).ConfigureAwait(false);
        await PublishFrameAsync(frame, cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        if (disposed) return;
        disposed = true;
        session.PresentationChanged -= OnPresentationChanged;
        session.DiagnosticPublished -= OnDiagnosticPublished;
        lifetime.Cancel();
        selection?.Cancel();
        WidgetPresentationTarget? target;
        lock (gate) target = activeTarget;
        if (target is not null)
        {
            try { await session.SetLifecycleAsync(target, WidgetLifecycleState.Background); }
            catch (Exception exception) when (exception is not OutOfMemoryException) { }
        }
        selection?.Dispose();
        lifetime.Dispose();
        await session.DisposeAsync().ConfigureAwait(false);
    }

    private void OnPresentationChanged(object? sender, WidgetPresentationChangedEventArgs args)
    {
        if (!string.Equals(args.State.WidgetId, ViewModel.SelectedWidgetId, StringComparison.Ordinal)) return;
        if (args.State.LastGood is { } frame) _ = PublishFrameAsync(frame, lifetime.Token);
        if (args.State.Failure is { } failure) _ = PublishFailureAsync(failure.Message);
    }

    private void OnDiagnosticPublished(object? sender, WidgetPresentationDiagnosticEventArgs args) =>
        _ = scheduler.InvokeAsync(() => ViewModel.StatusText = args.Diagnostic.Message);

    private Task PublishFrameAsync(WidgetPresentationFrame frame, CancellationToken cancellationToken) =>
        scheduler.InvokeAsync(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!string.Equals(ViewModel.SelectedWidgetId, frame.Authority.WidgetId, StringComparison.Ordinal)) return;
            ViewModel.CurrentFrame = frame;
            ViewModel.IsBusy = false;
            ViewModel.HasFailure = false;
            ViewModel.StatusText = $"{frame.Descriptor.Name} · snapshot {frame.Authority.SnapshotSequence}";
            FramePublished?.Invoke(this, frame);
        });

    private Task PublishFailureAsync(string message) => scheduler.InvokeAsync(() =>
    {
        ViewModel.IsBusy = false;
        ViewModel.HasFailure = true;
        ViewModel.StatusText = message;
    });

    private static ViewNode? FindNode(ViewNode root, string id)
    {
        if (string.Equals(root.Id, id, StringComparison.Ordinal)) return root;
        foreach (var child in root.Children)
        {
            if (FindNode(child, id) is { } found) return found;
        }
        return null;
    }

    private static long MonotonicMicroseconds() =>
        Stopwatch.GetTimestamp() * 1_000_000L / Stopwatch.Frequency;
}
