using System.Text.Json;
using System.Text.Json.Serialization;

namespace GameBarAlternative.AvaloniaPrototype.Diagnostics;

internal sealed class InputTraceRecorder
{
    private const int MaximumEntries = 2048;
    private static readonly TimeSpan FlushTimeout = TimeSpan.FromSeconds(2);
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() },
    };
    private readonly object gate = new();
    private readonly string? outputPath;
    private readonly List<InputTraceEntry> entries = [];
    private string sourceCommit;
    private long sequence;
    private long revision;
    private long persistedRevision = -1;
    private long persistedSequence;
    private Task publicationTask = Task.CompletedTask;
    private bool publisherRunning;
    private string? lastPersistenceFailure;

    public InputTraceRecorder(string? outputPath, string? sourceCommit = null)
    {
        this.outputPath = string.IsNullOrWhiteSpace(outputPath) ? null : Path.GetFullPath(outputPath);
        this.sourceCommit = string.IsNullOrWhiteSpace(sourceCommit) ? "manual-session" : sourceCommit;
        if (this.outputPath is not null)
        {
            lock (gate) SchedulePublicationLocked();
        }
    }

    public bool Enabled => outputPath is not null;

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
        if (outputPath is null) return new(true, 0, 0, null);
        Task publisher;
        long requestedRevision;
        long requestedSequence;
        lock (gate)
        {
            if (!string.Equals(this.sourceCommit, sourceCommit, StringComparison.Ordinal))
            {
                this.sourceCommit = sourceCommit;
                revision++;
            }
            requestedRevision = revision;
            requestedSequence = sequence;
            SchedulePublicationLocked();
            publisher = publicationTask;
        }

        try
        {
            await publisher.WaitAsync(FlushTimeout, cancellationToken).ConfigureAwait(false);
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
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return SnapshotFlushResult(requestedRevision, requestedSequence, exception.Message);
        }

        return SnapshotFlushResult(requestedRevision, requestedSequence, null);
    }

    private InputTraceFlushResult SnapshotFlushResult(
        long requestedRevision,
        long requestedSequence,
        string? waitFailure)
    {
        lock (gate)
        {
            var succeeded = persistedRevision >= requestedRevision &&
                persistedSequence >= requestedSequence;
            return new InputTraceFlushResult(
                succeeded,
                requestedSequence,
                persistedSequence,
                succeeded ? null : waitFailure ?? lastPersistenceFailure ?? "Latest input trace snapshot was not persisted.");
        }
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
            InputTraceArtifact artifact;
            long targetRevision;
            long targetSequence;
            lock (gate)
            {
                targetRevision = revision;
                targetSequence = sequence;
                artifact = CreateArtifactLocked();
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
                    publisherRunning = false;
                }
                return;
            }

            lock (gate)
            {
                persistedRevision = Math.Max(persistedRevision, targetRevision);
                persistedSequence = Math.Max(persistedSequence, targetSequence);
                lastPersistenceFailure = null;
                if (revision == targetRevision)
                {
                    publisherRunning = false;
                    return;
                }
            }
        }
    }

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
