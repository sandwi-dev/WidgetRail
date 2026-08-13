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
    private long sequence;

    public InputTraceRecorder(string? outputPath)
    {
        this.outputPath = string.IsNullOrWhiteSpace(outputPath) ? null : Path.GetFullPath(outputPath);
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
        }
    }

    public async Task FlushAsync(string sourceCommit, CancellationToken cancellationToken = default)
    {
        if (outputPath is null) return;
        InputTraceEntry[] snapshot;
        lock (gate) snapshot = entries.ToArray();
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
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
        await File.WriteAllTextAsync(
            outputPath,
            JsonSerializer.Serialize(artifact, JsonOptions),
            cancellationToken).ConfigureAwait(false);
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
