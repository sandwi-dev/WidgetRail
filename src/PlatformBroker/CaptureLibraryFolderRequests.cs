namespace WidgetRail.PlatformBroker;

public sealed record HostCaptureFolderRequest(string Id, BrokerWidgetIdentity Widget, string Path)
{ public string? FileName { get; init; } }

/// <summary>Only the library can choose the folder. The interactive frontend owns the Windows handoff.</summary>
internal static class CaptureLibraryFolderRequests
{
    private static readonly object gate = new();
    private static HostCaptureFolderRequest? request;
    private static TaskCompletionSource<bool>? completion;
    internal static HostCaptureFolderRequest? Pending { get { lock(gate) return request; } }
    internal static async Task<bool> OpenAsync(BrokerWidgetIdentity widget,string path,CancellationToken token,string? fileName = null)
    {
        TaskCompletionSource<bool> waiting;
        lock(gate)
        {
            if(request is not null) throw new BrokerException("capture_library_busy","A folder request is already pending.");
            request=new(Guid.NewGuid().ToString("N"),widget,path) { FileName = fileName };
            completion=waiting=new(TaskCreationOptions.RunContinuationsAsynchronously);
        }
        try { return await waiting.Task.WaitAsync(TimeSpan.FromSeconds(15),token).ConfigureAwait(false); }
        catch(TimeoutException) { return false; }
        finally { lock(gate) { if(ReferenceEquals(waiting,completion)) { request=null;completion=null; } } }
    }
    internal static void Complete(string id,bool opened)
    { lock(gate) { if(request?.Id==id) completion?.TrySetResult(opened); } }
}
