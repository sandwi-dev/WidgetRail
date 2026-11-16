using WidgetRail.WidgetProtocol;
namespace WidgetRail.WidgetSdk;
public sealed partial class WidgetIndexedCollection<TQuery,TItem>
{
    async ValueTask<int?> IWidgetIndexedCollection.LocateAsync(IndexedCollectionRangeRequest request,CancellationToken cancellationToken)
    {
        IndexedCollectionContract.ValidateRequest(request);
        Query captured; CancellationTokenSource lifetime;
        lock(gate)
        {
            captured=current;
            if(request.Kind!=IndexedCollectionRequestKind.Locate || request.Source!=captured.Descriptor || options.FindIndex is null)
                throw new InvalidOperationException("The indexed lookup query is unavailable.");
            if(activeLoads>=4)throw new InvalidOperationException("The indexed source read limit has been reached.");
            cancellationToken.ThrowIfCancellationRequested();widgetLifetime.ThrowIfCancellationRequested();
            lifetime=CancellationTokenSource.CreateLinkedTokenSource(cancellationToken,widgetLifetime,captured.Lifetime.Token);
            lifetime.CancelAfter(options.ReadTimeout);++activeLoads;
        }
        var token=lifetime.Token;
        var lookup=Task.Run(async()=>await options.FindIndex!(captured.Value,new(request.LocateKey!),token).ConfigureAwait(false),token);
        _=lookup.ContinueWith(task=>{_=task.Exception;lock(gate)--activeLoads;lifetime.Dispose();},CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,TaskScheduler.Default);
        int? result;
        try{result=await lookup.WaitAsync(token).ConfigureAwait(false);}
        catch(OperationCanceledException)when(!cancellationToken.IsCancellationRequested&&!widgetLifetime.IsCancellationRequested)
        {
            lock(gate)if(!ReferenceEquals(current,captured))throw new InvalidOperationException("The indexed lookup query changed.");
            throw new TimeoutException("The indexed lookup timed out.");
        }
        cancellationToken.ThrowIfCancellationRequested();widgetLifetime.ThrowIfCancellationRequested();
        lock(gate)if(!ReferenceEquals(current,captured))throw new InvalidOperationException("The indexed lookup query changed before publication.");
        if(result is { } index && (index<0||index>=captured.Descriptor.Count))throw new InvalidOperationException("The indexed lookup returned an invalid position.");
        return result;
    }
}
