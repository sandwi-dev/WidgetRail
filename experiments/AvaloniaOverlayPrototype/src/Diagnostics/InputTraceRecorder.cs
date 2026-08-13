using System.Text.Json;
using System.Text.Json.Serialization;

namespace GameBarAlternative.AvaloniaPrototype.Diagnostics;

internal sealed class InputTraceRecorder
{
    private const int MaximumEntries = 2048;
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

    public InputTraceRecorder(string? outputPath, string? sourceCommit = null)
    {
        this.outputPath = string.IsNullOrWhiteSpace(outputPath) ? null : Path.GetFullPath(outputPath);
        this.sourceCommit = string.IsNullOrWhiteSpace(sourceCommit) ? "manual-session" : sourceCommit;
        if (this.outputPath is not null)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(this.outputPath)!);
            lock (gate) PersistSnapshotLocked();
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
            if (entries.Count >= MaximumEntries) return;
            entries.Add(new InputTraceEntry(
                ++sequence,
                DateTime.UtcNow,
                eventName,
                visible,
                active,
                focusedSemanticId,
                detail,
                handled));
            PersistSnapshotLocked();
        }
    }

    public Task FlushAsync(string sourceCommit, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (outputPath is null) return Task.CompletedTask;
        lock (gate)
        {
            this.sourceCommit = sourceCommit;
            PersistSnapshotLocked();
        }
        return Task.CompletedTask;
    }

    private void PersistSnapshotLocked()
    {
        if (outputPath is null) return;
        var snapshot = entries.ToArray();
        var nativeRouted = snapshot.Any(entry => entry.EventName == "native-input-routed" && entry.Handled == true);
        var artifact = new InputTraceArtifact(
            "AVP-004-INTEGRATION",
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
        var temporaryPath = outputPath + ".tmp";
        try
        {
            File.WriteAllText(temporaryPath, JsonSerializer.Serialize(artifact, JsonOptions));
            if (File.Exists(outputPath)) File.Replace(temporaryPath, outputPath, null);
            else File.Move(temporaryPath, outputPath);
        }
        finally
        {
            if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
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
