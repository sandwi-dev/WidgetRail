using System.Net;
using System.Net.Http.Headers;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WidgetRail.RemoteMediaTransport;

namespace WidgetRail.RemoteMediaTransport.Tests;

[TestClass]
public sealed class RangeTests
{
    private static readonly Uri Source = new("https://media.example/clip");
    private static readonly PublicMediaPolicy Policy = new(["https://media.example"]);
    private static PublicMediaStreamLimits Limits => new() { BlockBytes=4,MaximumCachedBytes=8,MaximumFallbackBytes=16,
        MaximumLength=128,MaximumTransferredBytes=128,MaximumQueuedReads=1 };

    [TestMethod]
    public async Task RandomReadsCrossBlocksSeekBackwardAndEvictWithinBudget()
    {
        using var server=new RangeServer();await using var client=new PublicMediaClient(server,new());
        await using var source=await PublicMediaBlockSource.OpenAsync(client,Source,Policy,Limits);
        var result=new byte[10];Assert.AreEqual(10,await source.ReadAtAsync(2,result));
        CollectionAssert.AreEqual(server.Data[2..12],result);Assert.IsTrue(source.CachedBytes<=8);
        var again=new byte[2];Assert.AreEqual(2,await source.ReadAtAsync(2,again));CollectionAssert.AreEqual(server.Data[2..4],again);
        CollectionAssert.AreEqual(new long[]{0,4,8,0},server.Starts.ToArray());Assert.IsTrue(source.CachedBytes<=8);
        Assert.AreEqual(16,source.TransferredBytes);
        Assert.IsNull(server.Versions[0]);Assert.IsTrue(server.Versions.Skip(1).All(version=>version=="\"v1\""));
    }

    [TestMethod]
    public async Task SeekingUsesTheResourceThatIssuedTheValidator()
    {
        using var server=new RedirectingMediaServer();
        await using var client=new PublicMediaClient(server,new());
        await using var source=await PublicMediaBlockSource.OpenAsync(client,Source,Policy,Limits);
        var bytes=new byte[4];
        Assert.AreEqual(4,await source.ReadAtAsync(8,bytes));
        CollectionAssert.AreEqual(new byte[]{8,9,10,11},bytes);
        CollectionAssert.AreEqual(new[]{Source,server.File,server.File},server.Requests.ToArray());
        CollectionAssert.AreEqual(new string?[]{null,null,"\"v1\""},server.Validators.ToArray());

        server.FileMoved=true;
        var failure=await Assert.ThrowsAsync<PublicMediaException>(async()=>await source.ReadAtAsync(12,bytes));
        Assert.AreEqual(PublicMediaFailure.ResponseRejected,failure.Failure);
        await Assert.ThrowsAsync<PublicMediaException>(async()=>await source.ReadAtAsync(0,bytes));
        Assert.AreEqual(4,server.Requests.Count);
        Assert.AreEqual(server.File,server.Requests[^1]);
    }

    private sealed class RedirectingMediaServer():DelegatingHandler(new RangeServer())
    {
        internal Uri File { get; }=new(Source,"media?revision=one");
        internal bool FileMoved;
        internal List<Uri> Requests { get; }=[];
        internal List<string?> Validators { get; }=[];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token)
        {
            var uri=request.RequestUri!;
            Requests.Add(uri);
            Validators.Add(request.Headers.IfRange?.EntityTag?.Tag);
            var destination=uri==Source?File:uri==File && FileMoved?new Uri(Source,"media?revision=two"):null;
            if(destination is null)return base.SendAsync(request,token);
            var redirect=new HttpResponseMessage(HttpStatusCode.TemporaryRedirect);
            redirect.Headers.Location=destination;
            return Task.FromResult(redirect);
        }
    }

    [TestMethod]
    public async Task LastPartialBlockAndEofDoNotReadBeyondLength()
    {
        using var server=new RangeServer(10);await using var client=new PublicMediaClient(server,new());
        await using var source=await PublicMediaBlockSource.OpenAsync(client,Source,Policy,Limits);
        var result=new byte[8];Assert.AreEqual(2,await source.ReadAtAsync(8,result));
        CollectionAssert.AreEqual(server.Data[8..10],result[..2]);Assert.AreEqual(0,await source.ReadAtAsync(10,result));
        Assert.AreEqual(0,await source.ReadAtAsync(long.MaxValue,result));Assert.AreEqual(2,server.Calls);
    }

    [TestMethod]
    public async Task SmallNonRangeServerBecomesOneOwnedSnapshot()
    {
        using var server=new RangeServer(12){IgnoreRange=true};await using var client=new PublicMediaClient(server,new());
        var source=await PublicMediaBlockSource.OpenAsync(client,Source,Policy,Limits);
        var result=new byte[6];Assert.AreEqual(6,await source.ReadAtAsync(6,result));CollectionAssert.AreEqual(server.Data[6..],result);
        Assert.AreEqual(1,server.Calls);Assert.AreEqual(12,source.CachedBytes);
        await source.DisposeAsync();Assert.AreEqual(0,source.CachedBytes);
    }

    [TestMethod]
    public async Task MissingOrWeakValidatorFallsBackOnlyToABoundedWholeSnapshot()
    {
        foreach(var weak in new[]{false,true})
        {
            using var server=new RangeServer(12){OmitVersion=!weak,WeakVersion=weak};await using var client=new PublicMediaClient(server,new());
            await using var source=await PublicMediaBlockSource.OpenAsync(client,Source,Policy,Limits);
            var result=new byte[12];Assert.AreEqual(12,await source.ReadAtAsync(0,result));CollectionAssert.AreEqual(server.Data,result);
            Assert.AreEqual(2,server.Calls);Assert.AreEqual(16,source.TransferredBytes);
        }
    }

    [TestMethod]
    public async Task LargeUnversionedOrNonRangeResourcesFailWithoutWholeVideoBuffering()
    {
        foreach(var ignoreRange in new[]{false,true})
        {
            using var server=new RangeServer(){OmitVersion=true,IgnoreRange=ignoreRange};await using var client=new PublicMediaClient(server,new());
            await Assert.ThrowsAsync<PublicMediaException>(()=>PublicMediaBlockSource.OpenAsync(client,Source,Policy,Limits));
            Assert.AreEqual(1,server.Calls);
        }
    }

    [TestMethod]
    [DataRow("wrong-start")][DataRow("wrong-end")][DataRow("unknown-length")][DataRow("changed-length")]
    [DataRow("changed-version")][DataRow("full-response")][DataRow("missing-version")][DataRow("truncated")][DataRow("oversized")]
    public async Task InvalidContinuationCannotBeJoinedToCachedBytes(string mode)
    {
        using var server=new RangeServer(){ContinuationFailure=mode};await using var client=new PublicMediaClient(server,new());
        await using var source=await PublicMediaBlockSource.OpenAsync(client,Source,Policy,Limits);
        await Assert.ThrowsAsync<PublicMediaException>(async()=>await source.ReadAtAsync(4,new byte[4]));
        await Assert.ThrowsAsync<PublicMediaException>(async()=>await source.ReadAtAsync(0,new byte[4]));
        Assert.AreEqual(2,server.Calls);
    }

    [TestMethod]
    public async Task OversizedLogicalFileIsRejectedBeforeRetainingIt()
    {
        using var server=new RangeServer();await using var client=new PublicMediaClient(server,new());
        var error=await Assert.ThrowsAsync<PublicMediaException>(()=>PublicMediaBlockSource.OpenAsync(client,Source,Policy,Limits with{MaximumLength=16}));
        Assert.AreEqual(PublicMediaFailure.TooLarge,error.Failure);Assert.AreEqual(1,server.Calls);
    }

    [TestMethod]
    public async Task TransferBudgetStopsNewNetworkWorkButKeepsCachedReads()
    {
        using var server=new RangeServer();await using var client=new PublicMediaClient(server,new());
        await using var source=await PublicMediaBlockSource.OpenAsync(client,Source,Policy,Limits with{MaximumTransferredBytes=8});
        await source.ReadAtAsync(4,new byte[4]);
        var error=await Assert.ThrowsAsync<PublicMediaException>(async()=>await source.ReadAtAsync(8,new byte[4]));
        Assert.AreEqual(PublicMediaFailure.TooLarge,error.Failure);Assert.AreEqual(2,server.Calls);
        Assert.AreEqual(4,await source.ReadAtAsync(0,new byte[4]));Assert.AreEqual(2,server.Calls);
    }

    [TestMethod]
    public async Task PlaybackResumeReusesOnlyCheckedBlocksOnAnIndependentLifetime()
    {
        using var server=new RangeServer();
        await using var client=new PublicMediaClient(server,new());
        using var previousLifetime=new CancellationTokenSource();
        var previous=await PublicMediaBlockSource.OpenAsync(client,Source,Policy,Limits,previousLifetime.Token);
        var output=new byte[4];
        await previous.ReadAtAsync(4,output);
        await previous.ReadAtAsync(8,output);
        var snapshot=previous.TryTakeBufferedSnapshot();
        Assert.IsNotNull(snapshot);
        Assert.IsNull(previous.TryTakeBufferedSnapshot());
        await Assert.ThrowsAsync<PublicMediaException>(async()=>await previous.ReadAtAsync(12,output));
        previousLifetime.Cancel();
        await previous.DisposeAsync();
        Assert.IsNull(previous.TryTakeBufferedSnapshot());

        await using var resumed=snapshot.Open(CancellationToken.None);
        Assert.Throws<InvalidOperationException>(()=>snapshot.Open(CancellationToken.None));
        var calls=server.Calls;
        Assert.AreEqual(12,resumed.TransferredBytes);
        Assert.AreEqual(8,resumed.CachedBytes);
        await resumed.ReadAtAsync(4,output);
        CollectionAssert.AreEqual(new byte[]{4,5,6,7},output);
        Assert.AreEqual(calls,server.Calls);
        server.ContinuationFailure="changed-version";
        await Assert.ThrowsAsync<PublicMediaException>(async()=>await resumed.ReadAtAsync(0,output));
        Assert.AreEqual("\"v1\"",server.Versions.Last());
        Assert.IsNull(resumed.TryTakeBufferedSnapshot());
    }

    [TestMethod]
    public async Task TeardownCancelsInflightAndQueuedReadsAndReleasesCache()
    {
        var entered=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var server=new RangeServer { BeforeContinuation=async token=>{entered.TrySetResult();await Task.Delay(Timeout.Infinite,token);} };
        await using var client=new PublicMediaClient(server,new());
        var source=await PublicMediaBlockSource.OpenAsync(client,Source,Policy,Limits);
        var reading=source.ReadAtAsync(4,new byte[4]).AsTask();await entered.Task.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.IsNull(source.TryTakeBufferedSnapshot(),"Hiding the player must not wait for a network read");
        var queued=source.ReadAtAsync(8,new byte[4]).AsTask();
        var busy=await Assert.ThrowsAsync<PublicMediaException>(async()=>await source.ReadAtAsync(12,new byte[4]));
        Assert.AreEqual(PublicMediaFailure.Busy,busy.Failure);
        await Task.WhenAll(source.DisposeAsync().AsTask(),source.DisposeAsync().AsTask()).WaitAsync(TimeSpan.FromSeconds(3));
        await Assert.ThrowsAsync<OperationCanceledException>(async()=>await reading);
        await Assert.ThrowsAsync<OperationCanceledException>(async()=>await queued);Assert.AreEqual(0,source.CachedBytes);
        await Assert.ThrowsAsync<ObjectDisposedException>(async()=>await source.ReadAtAsync(0,new byte[4]));
    }

    private sealed class RangeServer(int length=24):HttpMessageHandler
    {
        internal byte[] Data=Enumerable.Range(0,length).Select(index=>(byte)index).ToArray();
        internal bool IgnoreRange,OmitVersion,WeakVersion;
        internal string? ContinuationFailure;
        internal Func<CancellationToken,Task>? BeforeContinuation;
        internal int Calls;
        internal List<long> Starts=[];
        internal List<string?> Versions=[];
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token)
        {
            ++Calls;
            Assert.IsNull(request.Headers.Authorization);Assert.IsFalse(request.Headers.Contains("Cookie"));
            if(Calls>1&&BeforeContinuation is {} before)await before(token);
            if(request.Headers.Range is null||IgnoreRange||Calls>1&&ContinuationFailure=="full-response")
                return new(HttpStatusCode.OK){Content=new ByteArrayContent(Data)};
            var range=request.Headers.Range.Ranges.Single();var start=range.From!.Value;var end=Math.Min(range.To!.Value,Data.Length-1);
            Starts.Add(start);Versions.Add(request.Headers.IfRange?.EntityTag?.Tag);
            var bytes=Data[(int)start..((int)end+1)];var total=Data.Length;var version="\"v1\"";
            if(Calls>1)
            {
                switch(ContinuationFailure)
                {
                    case "wrong-start":++start;break;
                    case "wrong-end":--end;break;
                    case "changed-length":++total;break;
                    case "changed-version":version="\"v2\"";break;
                    case "truncated":bytes=bytes[..^1];break;
                    case "oversized":bytes=[..bytes,99];break;
                }
            }
            var response=new HttpResponseMessage(HttpStatusCode.PartialContent){Content=new ByteArrayContent(bytes)};
            response.Content.Headers.ContentRange=Calls>1&&ContinuationFailure=="unknown-length"?new(start,end):new(start,end,total);
            if(!OmitVersion&&!(Calls>1&&ContinuationFailure=="missing-version"))response.Headers.ETag=new(version,WeakVersion);
            return response;
        }
    }
}
