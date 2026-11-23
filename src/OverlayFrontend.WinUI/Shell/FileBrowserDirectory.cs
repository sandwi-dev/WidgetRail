namespace WidgetRail.OverlayFrontend.WinUI.Shell;

internal enum FileBrowserMode
{
    Open, Multiple, Folder, Save
}
internal enum FileBrowserFilter
{
    All, Images, Videos, Audio, Documents
}
internal sealed record FileBrowserRequest(FileBrowserMode Mode, string Title, string CommitLabel, string StartFolder,
    string? Extension = null, string? SuggestedName = null, int Limit = 1);
internal sealed record FileBrowserItem(string Path, string Name, bool Folder, long Length = 0);
internal sealed record FileBrowserPage(string Directory, FileBrowserItem[] Items, bool HasMore);

/// <summary>Enumerate one directory, never file contents. Keep only a bounded sorted window even on large shares.</summary>
internal static class FileBrowserDirectory
{
    internal const int PageSize = 128;
    private static readonly SemaphoreSlim readers = new(3);
    private static readonly IComparer<FileBrowserItem> order = Comparer<FileBrowserItem>.Create((left, right) =>
    {
        var kind = right.Folder.CompareTo(left.Folder);
        if (kind != 0)
            return kind;
        var name = StringComparer.OrdinalIgnoreCase.Compare(left.Name, right.Name);
        return name != 0 ? name : StringComparer.Ordinal.Compare(left.Path, right.Path);
    });

    internal static string FullPath(string path)
    {
        var value = path.Trim().Trim('"').Replace('/', '\\');
        if (!Path.IsPathFullyQualified(value) || value.StartsWith(@"\\?\", StringComparison.Ordinal) ||
            value.StartsWith(@"\\.\", StringComparison.Ordinal) || value.IndexOf(':', 2) >= 0 ||
            value.Any(char.IsControl))
            throw new ArgumentException("Enter an absolute drive or network path.");
        var full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(value));
        if (value.StartsWith(@"\\", StringComparison.Ordinal) && full.Split('\\', StringSplitOptions.RemoveEmptyEntries).Length < 2)
            throw new ArgumentException("Include the server and share name.");
        return full;
    }

    internal static bool MatchesType(string path, string? extension) => extension is null || string.Equals(Path.GetExtension(path), extension, StringComparison.OrdinalIgnoreCase);
    private static bool MatchesFilter(string name, FileBrowserFilter filter)
    {
        if (filter == FileBrowserFilter.All)
            return true;
        var extension = Path.GetExtension(name).ToLowerInvariant();
        return filter switch
        {
            FileBrowserFilter.Images => extension is ".png" or ".jpg" or ".jpeg" or ".gif" or ".bmp" or ".webp" or ".avif" or ".heic" or ".tiff",
            FileBrowserFilter.Videos => extension is ".mp4" or ".mkv" or ".mov" or ".avi" or ".webm" or ".m4v" or ".wmv",
            FileBrowserFilter.Audio => extension is ".wav" or ".mp3" or ".flac" or ".aac" or ".m4a" or ".opus" or ".ogg" or ".wma",
            FileBrowserFilter.Documents => extension is ".pdf" or ".txt" or ".md" or ".doc" or ".docx" or ".odt" or ".xlsx" or ".csv" or ".pptx",
            _ => false
        };
    }

    internal static async Task<T> InspectAsync<T>(Func<T> inspect, CancellationToken token)
    {
        await readers.WaitAsync(token).ConfigureAwait(false);
        var read = Task.Run(() =>
        {
            try
            {
                token.ThrowIfCancellationRequested();
                return inspect();
            }
            finally { readers.Release(); }
        });
        // Win32 enumeration may remain blocked when a share disappears. Keep the reader slot until it really exits.
        try
        {
            return await read.WaitAsync(token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            _ = read.ContinueWith(task => _ = task.Exception, CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted, TaskScheduler.Default);
            throw;
        }
    }

    internal static Task<FileBrowserPage> ReadAsync(string directory, FileBrowserRequest request, string query,
        FileBrowserItem? after, CancellationToken token, FileBrowserFilter filter = FileBrowserFilter.All) => InspectAsync(() =>
        {
            var location = FullPath(directory);
            var items = new SortedSet<FileBrowserItem>(order);
            var options = new EnumerationOptions { IgnoreInaccessible = false, AttributesToSkip = FileAttributes.Hidden | FileAttributes.System };
            foreach (var entry in new DirectoryInfo(location).EnumerateFileSystemInfos("*", options))
            {
                token.ThrowIfCancellationRequested();
                var folder = entry is DirectoryInfo;
                if ((!folder && (request.Mode == FileBrowserMode.Folder || !MatchesType(entry.Name, request.Extension) || !MatchesFilter(entry.Name, filter))) ||
                    !entry.Name.Contains(query, StringComparison.OrdinalIgnoreCase))
                    continue;
                long size = 0;
                if (entry is FileInfo file)
                {
                    try
                    {
                        size = file.Length;
                    }
                    catch (IOException) { continue; }
                    catch (UnauthorizedAccessException) { continue; }
                }
                var item = new FileBrowserItem(entry.FullName, entry.Name, folder, size);
                if (after is not null && order.Compare(item, after) <= 0)
                    continue;
                if (items.Count > PageSize && order.Compare(item, items.Max!) >= 0)
                    continue;
                items.Add(item);
                if (items.Count > PageSize + 1)
                    items.Remove(items.Max!);
            }
            token.ThrowIfCancellationRequested();
            return new FileBrowserPage(location, items.Take(PageSize).ToArray(), items.Count > PageSize);
        }, token);

    internal static string SavePath(string directory, string name, string? extension)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Length > 255 || name != name.Trim() || name.EndsWith('.') ||
            name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || name is "." or "..")
            throw new ArgumentException("Enter a valid file name.");
        var stem = name.Split('.')[0].TrimEnd().ToUpperInvariant();
        if (stem is "CON" or "PRN" or "AUX" or "NUL" or "CONIN$" or "CONOUT$" ||
            stem.Length == 4 && (stem.StartsWith("COM") || stem.StartsWith("LPT")) && "0123456789¹²³".Contains(stem[3]))
            throw new ArgumentException("Windows reserves this file name.");
        if (extension is not null && Path.GetExtension(name).Length == 0)
            name += extension;
        if (name.Length > 255)
            throw new ArgumentException("Shorten the file name to 255 characters including its extension.");
        if (!MatchesType(name, extension))
            throw new ArgumentException($"Use a file name ending in {extension}.");
        return FullPath(Path.Combine(directory, name));
    }
}
