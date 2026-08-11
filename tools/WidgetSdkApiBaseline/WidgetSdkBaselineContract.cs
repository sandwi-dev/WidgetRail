using GameBarAlternative.WidgetSdk;

internal sealed record ApiDiff(
    IReadOnlyList<string> Removed,
    IReadOnlyList<string> Added)
{
    internal static ApiDiff Compare(
        IReadOnlyCollection<string> expected,
        IReadOnlyCollection<string> current)
    {
        var expectedSet = expected.ToHashSet(StringComparer.Ordinal);
        var currentSet = current.ToHashSet(StringComparer.Ordinal);
        return new(
            expectedSet.Except(currentSet, StringComparer.Ordinal)
                .Order(StringComparer.Ordinal).ToArray(),
            currentSet.Except(expectedSet, StringComparer.Ordinal)
                .Order(StringComparer.Ordinal).ToArray());
    }
}

internal static class WidgetSdkBaselineContract
{
    internal const int MaximumSymbols = 5000;
    internal const int MaximumBaselineBytes = 1024 * 1024;

    internal static IReadOnlyList<string> GenerateCurrent()
    {
        var current = WidgetSdkPublicApi.Generate(typeof(Widget).Assembly);
        if (current.Count is <= 0 or > MaximumSymbols)
            throw new InvalidDataException(
                $"WidgetSdk public API symbol count {current.Count} is outside the 1..{MaximumSymbols} bound.");
        var serializedBytes = System.Text.Encoding.UTF8.GetByteCount(
            WidgetSdkPublicApi.Serialize(current));
        if (serializedBytes > MaximumBaselineBytes)
            throw new InvalidDataException(
                "WidgetSdk public API exceeds the 1-MiB baseline bound.");
        return current;
    }

    internal static IReadOnlyList<string> ReadBaseline(
        string path,
        string checkoutRoot)
    {
        if (!File.Exists(path))
            throw new FileNotFoundException(
                "WidgetSdk public API baseline is missing. Run the documented baseline update tool after reviewing the change.",
                path);
        var info = new FileInfo(path);
        if (info.Length is < 1 or > MaximumBaselineBytes)
            throw new InvalidDataException(
                "WidgetSdk public API baseline exceeds its 1-MiB bound.");
        var text = File.ReadAllText(path);
        if (!string.IsNullOrWhiteSpace(checkoutRoot) &&
            (text.Contains(checkoutRoot, StringComparison.OrdinalIgnoreCase) ||
             text.Contains(
                 checkoutRoot.Replace("\\", "\\\\", StringComparison.Ordinal),
                 StringComparison.OrdinalIgnoreCase)))
            throw new InvalidDataException(
                "WidgetSdk public API baseline contains an absolute checkout path.");
        try
        {
            var symbols = WidgetSdkPublicApi.Parse(text);
            if (symbols.Count is <= 0 or > MaximumSymbols)
                throw new InvalidDataException(
                    $"WidgetSdk public API baseline symbol count is outside the 1..{MaximumSymbols} bound.");
            return symbols;
        }
        catch (InvalidOperationException exception)
        {
            throw new InvalidDataException(exception.Message, exception);
        }
    }
}

internal static class WidgetSdkBaselineUpdater
{
    internal static async Task<int> UpdateAsync(
        string baselinePath,
        CancellationToken cancellationToken = default)
    {
        var fullPath = Path.GetFullPath(baselinePath);
        var parent = Path.GetDirectoryName(fullPath)
            ?? throw new InvalidOperationException("Baseline path has no parent directory.");
        if (!Directory.Exists(parent))
            throw new DirectoryNotFoundException(
                $"Baseline parent directory does not exist: {parent}");
        var symbols = WidgetSdkBaselineContract.GenerateCurrent();
        var content = WidgetSdkPublicApi.Serialize(symbols);
        var temporary = fullPath + $".tmp-{Guid.NewGuid():N}";
        try
        {
            await File.WriteAllTextAsync(
                temporary, content, new System.Text.UTF8Encoding(false), cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            File.Move(temporary, fullPath, overwrite: true);
            return symbols.Count;
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }
}
