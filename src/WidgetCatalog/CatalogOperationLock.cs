namespace GameBarAlternative.WidgetCatalog;

internal sealed class CatalogOperationLock
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan RetryDelay = TimeSpan.FromMilliseconds(50);
    private readonly string _root;
    private readonly string _path;

    public CatalogOperationLock(string root)
    {
        _root = root;
        _path = Path.Combine(root, ".catalog-operation.lock");
    }

    public async Task<FileStream> AcquireAsync(CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(_root);
        FileSystemSafety.EnsureNoReparsePoints(_root, _root);
        var deadline = DateTime.UtcNow + Timeout;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                return new FileStream(
                    _path,
                    FileMode.OpenOrCreate,
                    FileAccess.ReadWrite,
                    FileShare.None,
                    bufferSize: 1,
                    FileOptions.Asynchronous | FileOptions.WriteThrough);
            }
            catch (IOException) when (DateTime.UtcNow < deadline)
            {
                await Task.Delay(RetryDelay, cancellationToken);
            }
            catch (IOException exception)
            {
                throw new WidgetPackageException(
                    "catalog_busy",
                    "The widget catalog is busy in another process. Wait for the other operation to finish and retry.",
                    exception);
            }
        }
    }
}
