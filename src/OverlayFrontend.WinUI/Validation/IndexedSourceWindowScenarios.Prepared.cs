using System.Collections.Specialized;
using Microsoft.UI.Dispatching;
using WidgetRail.OverlayFrontend.WinUI.Collections;
using WidgetRail.WidgetUi.State.Collections;

namespace WidgetRail.OverlayFrontend.WinUI.Validation;

internal static partial class IndexedSourceWindowScenarios
{
    private static async Task<int> PreparedAsync(DispatcherQueue dispatcher)
    {
        var checks=0;
        var oldKeys=new[]{"a","b","c","d"};
        var nextKeys=new[]{"x","a","b","c"};
        await using var source=NewSource();
        for(var index=0;index<oldKeys.Length;index++)((IndexedItem<string>)source[index]).SetValue(oldKeys[index],"old:"+oldKeys[index]);
        using var keep=source.Retain(1);
        var retained=source[1];
        var query=source.Query with{Generation=2};
        var leases=new[]{new Lease(),new Lease()};
        await using var ready=Prepare(query,nextKeys,leases);
        var additions=0;
        source.CollectionChanged+=(_,args)=>
        {
            if(args.Action!=NotifyCollectionChangedAction.Add)return;
            additions++;
            var item=(IndexedItem<string>)args.NewItems![0]!;
            Check(item.Key==nextKeys[args.NewStartingIndex]&&item.Value=="new:"+item.Key,
                "Inserted items expose prepared data in the structural notification");
        };
        source.RebaseWindow(query,0,oldKeys,nextKeys,ready);
        Check(additions==1&&ReferenceEquals(source[2],retained),"Prepared commit retains surviving native occurrence identity");
        Check(Enumerable.Range(0,4).All(index=>((IndexedItem<string>)source[index]).Value=="new:"+nextKeys[index]),
            "Prepared successor replaces every old row payload before returning");
        await ready.DisposeAsync();
        Check(leases.All(lease=>lease.Releases==0),"Consumed preparation cannot release source-owned leases");

        var rejectedLease=new Lease();
        await using(var incomplete=new PreparedIndexedWindow<string>(new[]{new IndexedRangeResult<string>(query with{Generation=3},1,0,
            new[]{new KeyedCollectionItem<string>("y","new:y")},0,rejectedLease)}))
        {
            try{source.RebaseWindow(query with{Generation=3},0,nextKeys,new[]{"y","x","a","b"},incomplete);
                throw new InvalidOperationException("Incomplete preparation was admitted.");}
            catch(InvalidDataException){checks++;}
            Check(source.Query==query&&ReferenceEquals(source[2],retained),"Rejected preparation leaves the current query and slots unchanged");
        }
        Check(rejectedLease.Releases==1,"Rejected preparation releases its range exactly once");
        var futureKeys=new[]{"y","x","a","b"};
        var foreignLeases=new[]{new Lease(),new Lease()};
        await using(var foreign=Prepare(query,futureKeys,foreignLeases))
        {
            try{source.RebaseWindow(query with{Generation=3},0,nextKeys,futureKeys,foreign);
                throw new InvalidOperationException("A prepared old query was admitted.");}
            catch(InvalidDataException){checks++;}
        }
        Check(source.Query==query&&foreignLeases.All(lease=>lease.Releases==1),
            "Late prepared query cannot mutate the current source and drains its own ranges");
        var identityLeases=new[]{new Lease(),new Lease()};
        await using(var wrongIdentity=Prepare(query with{Generation=3},new[]{"y","x","a","c"},identityLeases))
        {
            try{source.RebaseWindow(query with{Generation=3},0,nextKeys,futureKeys,wrongIdentity);
                throw new InvalidOperationException("A prepared foreign occurrence was admitted.");}
            catch(InvalidDataException){checks++;}
        }
        Check(source.Query==query&&identityLeases.All(lease=>lease.Releases==1),
            "Full-coverage preparation still validates exact occurrence identities before mutation");
        var abandonedLeases=new[]{new Lease(),new Lease()};
        var abandoned=Prepare(query with{Generation=3},futureKeys,abandonedLeases);
        await abandoned.DisposeAsync();await abandoned.DisposeAsync();
        Check(abandonedLeases.All(lease=>lease.Releases==1)&&source.Query==query,
            "Abandoned preparation releases once without publishing rows");
        keep.Dispose();await source.DisposeAsync();
        Check(leases.All(lease=>lease.Releases==1),"Prepared range ownership drains exactly once on source teardown");

        await using var reentrant=NewSource();
        var reentrantLeases=new[]{new Lease(),new Lease()};
        await using var reentrantReady=Prepare(reentrant.Query with{Generation=2},nextKeys,reentrantLeases);
        reentrant.CollectionChanged+=(_,args)=>{if(args.Action==NotifyCollectionChangedAction.Add)reentrant.Dispose();};
        reentrant.RebaseWindow(reentrant.Query with{Generation=2},0,oldKeys,nextKeys,reentrantReady);
        await reentrant.DisposeAsync();
        Check(reentrantLeases.All(lease=>lease.Releases==1),"Reentrant teardown during insertion owns and drains all prepared ranges");
        return checks;

        IndexedItemsSource<string> NewSource()=>new(new(new("prepared","instance","rows"),1),4,dispatcher,
            (_,_)=>throw new InvalidOperationException("Prepared data should not require a range refetch."),pageSize:2);
        PreparedIndexedWindow<string> Prepare(IndexedQueryIdentity identity,string[] keys,Lease[] owners)=>new(
            owners.Select((lease,page)=>new IndexedRangeResult<string>(identity,page+1,page*2,
                keys.Skip(page*2).Take(2).Select(key=>new KeyedCollectionItem<string>(key,"new:"+key)).ToArray(),0,lease)).ToArray());
        void Check(bool value,string message){if(!value)throw new InvalidOperationException(message);checks++;}
    }
}
