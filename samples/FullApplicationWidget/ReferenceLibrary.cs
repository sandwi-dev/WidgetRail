namespace WidgetRail.Samples.FullApplicationWidget;

internal sealed record ReferenceDocument(string Id, string Title, string Section, string Summary);

internal sealed class ReferenceLibrary
{
    internal const int DocumentCount = 10_000;
    private readonly IReadOnlyList<ReferenceDocument> _documents = Array.AsReadOnly(Enumerable.Range(0, DocumentCount)
        .Select(index => new ReferenceDocument($"document-{index:D5}", $"Document {index:D5}",
            $"Section {index / 100:D3}", $"Deterministic private record {index:D5}.")).ToArray());
    private int _failNext;
    private int _loadCount;
    private Func<CancellationToken, ValueTask>? _beforeLoad;
    internal int Count => _documents.Count;
    internal IReadOnlyList<ReferenceDocument> Documents => _documents;
    internal int LoadCount => Volatile.Read(ref _loadCount);
    internal void FailNext() => Interlocked.Exchange(ref _failNext, 1);
    internal void BeforeNextLoad(Func<CancellationToken, ValueTask> callback) => _beforeLoad = callback;

    internal async ValueTask<IReadOnlyList<ReferenceDocument>> ReadAsync(IReadOnlyList<ReferenceDocument> captured,
        int start, int count, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (start < 0 || count < 1 || count > 64 || (long)start + count > captured.Count)
            throw new ArgumentOutOfRangeException(nameof(start));
        Interlocked.Increment(ref _loadCount);
        if (Interlocked.Exchange(ref _failNext, 0) != 0) throw new InvalidOperationException("Deterministic reference failure.");
        if (Interlocked.Exchange(ref _beforeLoad, null) is { } beforeLoad) await beforeLoad(cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        var items = new ReferenceDocument[count];
        for (var index = 0; index < count; ++index) items[index] = captured[start + index];
        return items;
    }
}
