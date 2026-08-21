using System.Globalization;
using WidgetRail.WidgetSdk;

namespace WidgetRail.Samples.FullApplicationWidget;

internal sealed record ReferenceDocument(
    string Id,
    string Title,
    string Section,
    string Summary);

internal sealed class ReferenceLibrary
{
    internal const int DocumentCount = 10_000;
    private readonly ReferenceDocument[] _documents = Enumerable.Range(0, DocumentCount)
        .Select(index => new ReferenceDocument(
            $"document-{index:D5}",
            $"Document {index:D5}",
            $"Section {index / 100:D3}",
            $"Deterministic private record {index:D5}."))
        .ToArray();
    private int _failNext;
    private Func<CancellationToken, ValueTask>? _beforeLoad;

    internal int Count => _documents.Length;
    internal int LoadCount { get; private set; }

    internal void FailNext() => Interlocked.Exchange(ref _failNext, 1);
    internal void BeforeNextLoad(Func<CancellationToken, ValueTask> callback) =>
        _beforeLoad = callback;

    internal async ValueTask<WidgetCursorPage<ReferenceDocument>> LoadAsync(
        WidgetCollectionCursor? cursor,
        WidgetCursorDirection? direction,
        int limit,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        LoadCount++;
        if (Interlocked.Exchange(ref _failNext, 0) != 0)
            throw new InvalidOperationException("Deterministic reference failure.");
        if (Interlocked.Exchange(ref _beforeLoad, null) is { } beforeLoad)
            await beforeLoad(cancellationToken).ConfigureAwait(false);
        var offset = cursor is null ? 0 : Parse(cursor.Value.Value);
        if (offset < 0 || offset >= _documents.Length)
            throw new InvalidOperationException("The reference cursor is outside the private model.");
        var items = _documents.Skip(offset).Take(limit).ToArray();
        var beforeOffset = offset <= 0 ? (int?)null : Math.Max(0, offset - limit);
        var afterOffset = offset + items.Length >= _documents.Length
            ? (int?)null : offset + items.Length;
        return new(
            items,
            beforeOffset is null ? null : Cursor(beforeOffset.Value),
            afterOffset is null ? null : Cursor(afterOffset.Value))
        {
            FirstItemIndex = offset,
            TotalItemCount = _documents.Length,
        };
    }

    internal ReferenceDocument? Find(string id) =>
        _documents.FirstOrDefault(item => string.Equals(item.Id, id, StringComparison.Ordinal));

    private static WidgetCollectionCursor Cursor(int offset) =>
        new(offset.ToString(CultureInfo.InvariantCulture));

    private static int Parse(string value) =>
        int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var offset)
            ? offset
            : throw new InvalidOperationException("The reference cursor is malformed.");
}
