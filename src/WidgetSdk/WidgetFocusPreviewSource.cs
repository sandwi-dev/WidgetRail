using WidgetRail.WidgetProtocol;

namespace WidgetRail.WidgetSdk;

/// <summary>One immutable context supplies previews on demand. Publishing a context retires prior content and commands.</summary>
public sealed record WidgetFocusPreviewOptions<TContext> where TContext : notnull
{
    /// <summary>Builds one complete preview. Honor cancellation; never mutate the captured context.</summary>
    public required Func<TContext, string, CancellationToken, ValueTask<WidgetElement>> Resolve { get; init; }
    /// <summary>Handles an explicitly admitted action against the same context and key used to build its content.</summary>
    public Func<TContext, string, WidgetActionEvent, CancellationToken, ValueTask>? OnAction { get; init; }
    /// <summary>Resolves opaque artwork handles declared in this preview using its captured context.</summary>
    public Func<TContext, string, WidgetArtworkHandle, CancellationToken, ValueTask<WidgetEncodedArtwork?>>? ResolveArtwork { get; init; }
    /// <summary>Optional file-or-byte resolver; existing byte resolution remains the fallback.</summary>
    public Func<TContext, string, WidgetArtworkHandle, CancellationToken, ValueTask<WidgetArtworkSource?>>? ResolveArtworkSource { get; init; }
    /// <summary>Provider deadline; defaults to five seconds, with an allowed range of 250 ms to ten seconds.</summary>
    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(5);
}

/// <summary>Lightweight keys are published with rows; content is constructed only for requested keys.</summary>
public sealed class WidgetFocusPreviewSource<TContext> : IWidgetIndexedCollection where TContext : notnull
{
    private sealed record Context(TContext Value, IndexedCollectionDescriptor Descriptor, CancellationTokenSource Lifetime);
    private readonly object gate = new();
    private readonly WidgetFocusPreviewOptions<TContext> options;
    private readonly Action changed;
    private readonly CancellationToken widgetLifetime;
    private Context current;
    private int active;

    internal WidgetFocusPreviewSource(string id, TContext initial, WidgetFocusPreviewOptions<TContext> options,
        Action changed, CancellationToken lifetime)
    {
        StableIdentifier.Validate(id, nameof(id));
        ArgumentNullException.ThrowIfNull(initial);
        ArgumentNullException.ThrowIfNull(options.Resolve);
        if (options.Timeout < TimeSpan.FromMilliseconds(250) || options.Timeout > TimeSpan.FromSeconds(10))
            throw new ArgumentOutOfRangeException(nameof(options.Timeout));
        this.options = options;
        this.changed = changed;
        widgetLifetime = lifetime;
        current = new(initial, new(id, 1, 0, 1), new());
    }

    public IndexedCollectionDescriptor Descriptor { get { lock (gate) return current.Descriptor; } }

    public void Publish(TContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        Context old;
        lock (gate)
        {
            widgetLifetime.ThrowIfCancellationRequested();
            old = current;
            current = new(context, old.Descriptor with { QueryGeneration = checked(old.Descriptor.QueryGeneration + 1) }, new());
        }
        _ = RetireAsync(old.Lifetime);
        changed();
    }

    private static async Task RetireAsync(CancellationTokenSource lifetime)
    {
        try { await lifetime.CancelAsync().ConfigureAwait(false); }
        catch (AggregateException) { /* Provider callbacks cannot prevent context retirement. */ }
        finally { lifetime.Dispose(); }
    }

    async ValueTask<WidgetIndexedRead> IWidgetIndexedCollection.ReadAsync(
        IndexedCollectionRangeRequest request, CancellationToken cancellationToken)
    {
        Context captured;
        CancellationTokenSource lifetime;
        lock (gate)
        {
            captured = current;
            if (request.Kind != IndexedCollectionRequestKind.FocusPreview || request.Source != captured.Descriptor || request.PreviewKey is null)
                throw new InvalidOperationException("Preview context retired.");
            if (active != 0)
                throw new InvalidOperationException("The previous preview provider is still completing.");
            lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, widgetLifetime, captured.Lifetime.Token);
            lifetime.CancelAfter(options.Timeout);
            active++;
        }
        var token = lifetime.Token;
        var key = request.PreviewKey;
        var pending = Task.Run(async () => await options.Resolve(captured.Value, key, token).ConfigureAwait(false), token);
        // A provider that ignores cancellation keeps its slot until it really terminates.
        _ = pending.ContinueWith(task =>
        {
            _ = task.Exception;
            lock (gate) active--;
            lifetime.Dispose();
        }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        var content = await pending.WaitAsync(token).ConfigureAwait(false);
        token.ThrowIfCancellationRequested();
        cancellationToken.ThrowIfCancellationRequested();
        widgetLifetime.ThrowIfCancellationRequested();
        lock (gate)
            if (!ReferenceEquals(current, captured)) throw new OperationCanceledException("Preview context changed.");
        var count = 0;
        var root = WidgetDeclarationSnapshot.Freeze(content.ToProtocolNode(), 1, ref count);
        var binding = new WidgetIndexedItemBinding(new(key, root),
            (action, ct) => options.OnAction?.Invoke(captured.Value, key, action, ct) ?? ValueTask.CompletedTask,
            options.ResolveArtwork is { } resolver ? (handle, ct) => resolver(captured.Value, key, handle, ct) : null,
            options.ResolveArtworkSource is { } sourceResolver ? (handle, ct) => sourceResolver(captured.Value, key, handle, ct) : null);
        return new(Array.AsReadOnly(new[] { binding }), count);
    }
}

public abstract partial class Widget
{
    protected WidgetFocusPreviewSource<TContext> CreateFocusPreviewSource<TContext>(
        string id, TContext context, WidgetFocusPreviewOptions<TContext> options) where TContext : notnull
    {
        ArgumentNullException.ThrowIfNull(options);
        var source = new WidgetFocusPreviewSource<TContext>(id, context, options,
            () => { RetireIndexedLeases(); Invalidate(); }, WidgetLifetimeToken);
        lock (indexedSourcesGate)
            if (!indexedSources.TryAdd(id, source)) throw new ArgumentException("Content source IDs must be unique within a widget.", nameof(id));
        return source;
    }
}
