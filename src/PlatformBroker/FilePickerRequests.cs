using WidgetRail.WidgetProtocol;
namespace WidgetRail.PlatformBroker;

public sealed record HostFilePickerRequest(string Id,BrokerWidgetIdentity Widget,string Title,bool Multiple)
{
    public string? CommitLabel { get; init; }
}

/// <summary>Trusted host completion of a native picker; package payloads never supply selected paths.</summary>
public static class FilePickerRequests
{
    private sealed record Request(HostFilePickerRequest View,Func<bool> Current,TaskCompletionSource<string[]> Completion);
    private static readonly object Gate=new();
    private static Request? pending;
    public static HostFilePickerRequest? Pending {get{lock(Gate){RetireStale();return pending?.View;}}}
    internal static async Task<string[]> ChooseAsync(BrokerWidgetIdentity widget,WidgetFileChoiceRequest choice,Func<bool> current,CancellationToken token)
    {
        Request request;
        lock(Gate)
        {
            RetireStale();
            if(pending is not null)throw new BrokerException("file_picker_busy","A file picker is already open.");
            pending=request=new(new HostFilePickerRequest(Guid.NewGuid().ToString("N"),widget,choice.Title,choice.Multiple) { CommitLabel = choice.CommitLabel },
                current,new(TaskCreationOptions.RunContinuationsAsynchronously));
        }
        try{return await request.Completion.Task.WaitAsync(TimeSpan.FromMinutes(2),token).ConfigureAwait(false);}
        catch(TimeoutException){return [];}
        finally{lock(Gate){if(ReferenceEquals(pending,request))pending=null;}}
    }
    public static bool IsCurrent(string id)
    {lock(Gate){RetireStale();return pending?.View.Id==id;}}
    public static bool Complete(string id,IReadOnlyList<string>? paths)
    {
        lock(Gate)
        {
            RetireStale();
            if(pending is not {} request||request.View.Id!=id)return false;
            var selected=paths?.ToArray()??[];
            if(selected.Length>(request.View.Multiple?WidgetFileContract.MaximumFiles:1)||selected.Any(path=>
                string.IsNullOrWhiteSpace(path)||!Path.IsPathFullyQualified(path)||path.Length>32760||path.Any(char.IsControl)))
                throw new BrokerException("invalid_payload","The selected files are invalid.");
            return request.Completion.TrySetResult(selected);
        }
    }
    private static void RetireStale()
    {
        if(pending is {} request&&!request.Current())
        {pending=null;request.Completion.TrySetCanceled();}
    }
}
