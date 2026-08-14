using System.Text.Json;
using System.Text.Json.Serialization;

namespace GameBarAlternative.AvaloniaPrototype.Diagnostics;

internal sealed class InputTraceRecorder
{
    private const int MaximumEntries = 2048;
    private static readonly TimeSpan PublicationInterval = TimeSpan.FromMilliseconds(225);
    private static readonly TimeSpan FlushTimeout = TimeSpan.FromSeconds(2);
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() },
    };
    private readonly object gate = new();
    private readonly string? outputPath;
    private readonly Func<TimeSpan, CancellationToken, Task> publicationDelayAsync;
    private readonly List<InputTraceEntry> entries = [];
    private string sourceCommit;
    private long sequence;
    private long revision;
    private long persistedRevision = -1;
    private long persistedSequence;
    private Task publicationTask = Task.CompletedTask;
    private TaskCompletionSource<bool> publicationStateChanged = CreateStateSignal();
    private CancellationTokenSource? publicationDelayCancellation;
    private bool publisherRunning;
    private long forceThroughRevision = -1;
    private long persistenceFailureGeneration;
    private int publicationAttemptCount;
    private string? lastPersistenceFailure;

    public InputTraceRecorder(
        string? outputPath,
        string? sourceCommit = null,
        Func<TimeSpan, CancellationToken, Task>? publicationDelayAsync = null)
    {
        this.outputPath = string.IsNullOrWhiteSpace(outputPath) ? null : Path.GetFullPath(outputPath);
        this.sourceCommit = string.IsNullOrWhiteSpace(sourceCommit) ? "manual-session" : sourceCommit;
        this.publicationDelayAsync = publicationDelayAsync ??
            ((delay, cancellationToken) => Task.Delay(delay, cancellationToken));
        if (this.outputPath is not null)
        {
            lock (gate) SchedulePublicationLocked();
        }
    }

    public bool Enabled => outputPath is not null;
    internal int PublicationAttemptCount
    {
        get { lock (gate) return publicationAttemptCount; }
    }

    public void Record(
        string eventName,
        bool visible,
        bool active,
        string? focusedSemanticId = null,
        string? detail = null,
        bool? handled = null)
    {
        if (outputPath is null) return;
        lock (gate)
        {
            if (entries.Count < MaximumEntries)
            {
                entries.Add(new InputTraceEntry(
                    ++sequence,
                    DateTime.UtcNow,
                    eventName,
                    visible,
                    active,
                    focusedSemanticId,
                    detail,
                    handled));
                revision++;
            }
            SchedulePublicationLocked();
        }
    }

    public async Task<InputTraceFlushResult> FlushAsync(
        string sourceCommit,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (outputPath is null) return new(true, 0, 0, null);
        long requestedRevision;
        long requestedSequence;
        long startingFailureGeneration;
        lock (gate)
        {
            if (!string.Equals(this.sourceCommit, sourceCommit, StringComparison.Ordinal))
            {
                this.sourceCommit = sourceCommit;
                revision++;
            }
            requestedRevision = revision;
            requestedSequence = sequence;
            startingFailureGeneration = persistenceFailureGeneration;
            if (persistedRevision >= requestedRevision && persistedSequence >= requestedSequence)
                return SnapshotFlushResultLocked(requestedRevision, requestedSequence, null);
            ForcePublicationLocked(requestedRevision);
            SchedulePublicationLocked();
        }

        var waitClock = System.Diagnostics.Stopwatch.StartNew();
        while (true)
        {
            Task stateChanged;
            lock (gate)
            {
                if (persistedRevision >= requestedRevision && persistedSequence >= requestedSequence)
                    return SnapshotFlushResultLocked(requestedRevision, requestedSequence, null);
                if (persistenceFailureGeneration > startingFailureGeneration)
                    return SnapshotFlushResultLocked(requestedRevision, requestedSequence, lastPersistenceFailure);
                if (!publisherRunning)
                {
                    ForcePublicationLocked(requestedRevision);
                    SchedulePublicationLocked();
                }
                stateChanged = publicationStateChanged.Task;
            }

            var remaining = FlushTimeout - waitClock.Elapsed;
            if (remaining <= TimeSpan.Zero)
            {
                return SnapshotFlushResult(
                    requestedRevision,
                    requestedSequence,
                    $"Input trace publication exceeded the bounded {FlushTimeout.TotalSeconds:F0} second timeout.");
            }

            try
            {
                await stateChanged.WaitAsync(remaining, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (TimeoutException)
            {
                return SnapshotFlushResult(
                    requestedRevision,
                    requestedSequence,
                    $"Input trace publication exceeded the bounded {FlushTimeout.TotalSeconds:F0} second timeout.");
            }
        }
    }

    private InputTraceFlushResult SnapshotFlushResult(
        long requestedRevision,
        long requestedSequence,
        string? waitFailure)
    {
        lock (gate)
        {
            return SnapshotFlushResultLocked(requestedRevision, requestedSequence, waitFailure);
        }
    }

    private InputTraceFlushResult SnapshotFlushResultLocked(
        long requestedRevision,
        long requestedSequence,
        string? waitFailure)
    {
        var succeeded = persistedRevision >= requestedRevision &&
            persistedSequence >= requestedSequence;
        return new InputTraceFlushResult(
            succeeded,
            requestedSequence,
            persistedSequence,
            succeeded ? null : waitFailure ?? lastPersistenceFailure ?? "Latest input trace snapshot was not persisted.");
    }

    private void ForcePublicationLocked(long requestedRevision)
    {
        forceThroughRevision = Math.Max(forceThroughRevision, requestedRevision);
        publicationDelayCancellation?.Cancel();
    }

    private void SchedulePublicationLocked()
    {
        if (publisherRunning || outputPath is null) return;
        publisherRunning = true;
        publicationTask = Task.Run(PublishLoopAsync);
    }

    private async Task PublishLoopAsync()
    {
        while (true)
        {
            CancellationTokenSource? delayCancellation = null;
            lock (gate)
            {
                if (forceThroughRevision < revision)
                {
                    delayCancellation = new CancellationTokenSource();
                    publicationDelayCancellation = delayCancellation;
                }
            }

            if (delayCancellation is not null)
            {
                try
                {
                    await publicationDelayAsync(PublicationInterval, delayCancellation.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (delayCancellation.IsCancellationRequested)
                {
                    // Explicit flush bypasses the live debounce window.
                }
                finally
                {
                    lock (gate)
                    {
                        if (ReferenceEquals(publicationDelayCancellation, delayCancellation))
                            publicationDelayCancellation = null;
                    }
                    delayCancellation.Dispose();
                }
            }

            InputTraceArtifact artifact;
            long targetRevision;
            long targetSequence;
            lock (gate)
            {
                targetRevision = revision;
                targetSequence = sequence;
                artifact = CreateArtifactLocked();
                publicationAttemptCount++;
            }

            try
            {
                await PersistSnapshotAsync(artifact).ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                lock (gate)
                {
                    lastPersistenceFailure = $"{exception.GetType().Name}: {exception.Message}";
                    persistenceFailureGeneration++;
                    publisherRunning = false;
                    PulsePublicationStateLocked();
                }
                return;
            }

            lock (gate)
            {
                persistedRevision = Math.Max(persistedRevision, targetRevision);
                persistedSequence = Math.Max(persistedSequence, targetSequence);
                if (forceThroughRevision <= targetRevision) forceThroughRevision = -1;
                lastPersistenceFailure = null;
                PulsePublicationStateLocked();
                if (revision == targetRevision)
                {
                    publisherRunning = false;
                    return;
                }
            }
        }
    }

    private void PulsePublicationStateLocked()
    {
        var completed = publicationStateChanged;
        publicationStateChanged = CreateStateSignal();
        completed.TrySetResult(true);
    }

    private static TaskCompletionSource<bool> CreateStateSignal() =>
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    private InputTraceArtifact CreateArtifactLocked()
    {
        var snapshot = entries.ToArray();
        var nativeRouted = snapshot.Any(entry => entry.EventName == "native-input-routed" && entry.Handled == true);
        return new InputTraceArtifact(
            "AVP-004-REDESIGN",
            sourceCommit,
            Environment.ProcessId,
            snapshot,
            snapshot.Any(entry => entry.EventName == "native-controller-state" &&
                entry.Detail?.Contains("GameInputVisibleLease", StringComparison.Ordinal) == true),
            snapshot.Any(entry => entry.EventName == "native-controller-state" &&
                entry.Detail?.Contains("connected=True", StringComparison.OrdinalIgnoreCase) == true &&
                entry.Detail?.Contains("GameInputVisibleLease", StringComparison.Ordinal) == true),
            nativeRouted,
            false,
            nativeRouted,
            []);
    }

    private async Task PersistSnapshotAsync(InputTraceArtifact artifact)
    {
        if (outputPath is null) return;
        var directory = Path.GetDirectoryName(outputPath)!;
        Directory.CreateDirectory(directory);
        var temporaryPath = Path.Combine(
            directory,
            $"{Path.GetFileName(outputPath)}.{Environment.ProcessId}.{Guid.NewGuid():N}.tmp");
        try
        {
            await File.WriteAllTextAsync(
                temporaryPath,
                JsonSerializer.Serialize(artifact, JsonOptions)).ConfigureAwait(false);
            if (File.Exists(outputPath)) File.Replace(temporaryPath, outputPath, null);
            else File.Move(temporaryPath, outputPath);
        }
        finally
        {
            try
            {
                if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
            }
            catch (Exception cleanupFailure) when (cleanupFailure is not OutOfMemoryException)
            {
                // Best effort only: cleanup must never replace the publication result.
            }
        }
    }

    private sealed record InputTraceArtifact(
        string Assignment,
        string SourceCommit,
        int CandidateProcessId,
        IReadOnlyList<InputTraceEntry> Entries,
        bool NativeVisibleLeaseObserved,
        bool NativeConnectedVisibleLeaseObserved,
        bool NativeRoutedSemanticInputObserved,
        bool DeterministicSharedRouterProofObserved,
        bool RoutedSemanticInputObserved,
        IReadOnlyList<string> HandledCategories);

    private sealed record InputTraceEntry(
        long Sequence,
        DateTime TimestampUtc,
        string EventName,
        bool Visible,
        bool Active,
        string? FocusedSemanticId,
        string? Detail,
        bool? Handled);
}

internal sealed record InputTraceFlushResult(
    bool Succeeded,
    long RequestedSequence,
    long PersistedSequence,
    string? Failure);
