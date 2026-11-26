using WidgetRail.WidgetProtocol;
namespace WidgetRail.PlatformBroker;

public sealed record HostFilePickerRequest(string Id,BrokerWidgetIdentity Widget,string Title,bool Multiple)
{
    public string? CommitLabel { get; init; }
    public bool IsWindowChoice { get; init; }
}

/// <summary>Trusted host completion of a native picker; package payloads never supply selected paths.</summary>
public static class FilePickerRequests
{
    private sealed record Request(HostFilePickerRequest View,Func<bool> Current,TaskCompletionSource<string[]> Completion);
    private static readonly object Gate=new();
    private static Request? pending;
    public static HostFilePickerRequest? Pending {get{lock(Gate){RetireStale();return pending?.View;}}}
    internal static Task<string[]> ChooseAsync(BrokerWidgetIdentity widget,WidgetFileChoiceRequest choice,Func<bool> current,CancellationToken token) =>
        ChooseAsync(new(Guid.NewGuid().ToString("N"), widget, choice.Title, choice.Multiple) { CommitLabel = choice.CommitLabel }, current, token);

    internal static async Task<string?> ChooseWindowAsync(BrokerWidgetIdentity widget, string title, Func<bool> current, CancellationToken token)
    {
        var selected = await ChooseAsync(new(Guid.NewGuid().ToString("N"), widget, title, false) { IsWindowChoice = true }, current, token).ConfigureAwait(false);
        return selected.SingleOrDefault();
    }

    private static async Task<string[]> ChooseAsync(HostFilePickerRequest prompt,Func<bool> current,CancellationToken token)
    {
        Request request;
        lock(Gate)
        {
            RetireStale();
            if(pending is not null)throw new BrokerException("file_picker_busy","Another picker is already open.");
            pending=request=new(prompt,current,new(TaskCreationOptions.RunContinuationsAsynchronously));
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
            if (request.View.IsWindowChoice && selected.Length != 0) return false;
            if(selected.Length>(request.View.Multiple?WidgetFileContract.MaximumFiles:1)||selected.Any(path=>
                string.IsNullOrWhiteSpace(path)||!Path.IsPathFullyQualified(path)||path.Length>32760||path.Any(char.IsControl)))
                throw new BrokerException("invalid_payload","The selected files are invalid.");
            return request.Completion.TrySetResult(selected);
        }
    }
    public static bool CompleteWindow(string id, string? match)
    {
        lock (Gate)
        {
            RetireStale();
            if (pending is not { } request || request.View.Id != id || !request.View.IsWindowChoice) return false;
            if (match != null && (match.Length != 64 || match.Any(c => !(c is >= '0' and <= '9' or >= 'a' and <= 'f'))))
                throw new BrokerException("invalid_payload", "The selected window is invalid.");
            return request.Completion.TrySetResult(match == null ? [] : [match]);
        }
    }
    private static void RetireStale()
    {
        if(pending is {} request&&!request.Current())
        {pending=null;request.Completion.TrySetCanceled();}
    }
}
