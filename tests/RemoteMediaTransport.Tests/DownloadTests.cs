using System.Net;
using System.Net.Http.Headers;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WidgetRail.RemoteMediaTransport;

namespace WidgetRail.RemoteMediaTransport.Tests;

[TestClass]
public sealed class DownloadTests
{
    private static readonly Uri Source = new("https://cdn.example/media?signature=synthetic-secret");
    private static readonly PublicMediaPolicy Policy = new(["https://cdn.example", "https://media.example"]);

    [TestMethod]
    public async Task RedirectsAreRevalidatedAndCarryOnlyAnonymousGetHeaders()
    {
        var requests=0;
        await using var client=new PublicMediaClient(new Handler((request,_)=>
        {
            ++requests;
            Assert.AreEqual(HttpMethod.Get,request.Method); Assert.AreEqual(HttpVersion.Version11,request.Version);
            Assert.AreEqual(HttpVersionPolicy.RequestVersionExact,request.VersionPolicy);
            Assert.IsNull(request.Headers.Authorization); Assert.IsNull(request.Headers.Referrer);
            Assert.IsFalse(request.Headers.Contains("Cookie")); Assert.IsFalse(request.Headers.Contains("Proxy-Authorization"));
            Assert.AreEqual("identity",request.Headers.AcceptEncoding.Single().Value);
            return Task.FromResult(requests==1?Redirect("https://media.example/resolved"):Ok([1,2,3]));
        }),new());
        var result=await client.DownloadAsync(Source,Policy,3);
        CollectionAssert.AreEqual(new byte[]{1,2,3},result.Bytes.ToArray()); Assert.AreEqual(2,requests);
    }

    [TestMethod]
    [DataRow("https://127.0.0.1/private")][DataRow("http://cdn.example/downgrade")]
    [DataRow("https://cdn.example.evil.example/track")][DataRow("https://cdn.example:444/track")]
    [DataRow("https://user:secret@cdn.example/track")]
    public async Task RejectedRedirectNeverReachesAnotherRequest(string destination)
    {
        var calls=0;
        await using var client=new PublicMediaClient(new Handler((_,_)=>{++calls;return Task.FromResult(Redirect(destination));}),new());
        var error=await Assert.ThrowsAsync<PublicMediaException>(()=>client.DownloadAsync(Source,Policy,64));
        Assert.AreEqual(PublicMediaFailure.DestinationRejected,error.Failure); Assert.AreEqual(1,calls);
    }

    [TestMethod]
    public async Task RelativeRedirectWorksAndCyclesStopBeforeAnotherRequest()
    {
        var calls=0;
        await using var client=new PublicMediaClient(new Handler((request,_)=>
        {
            ++calls;
            return Task.FromResult(request.RequestUri!.AbsolutePath=="/media"?Redirect("/next"):Redirect(Source.AbsoluteUri));
        }),new());
        var error=await Assert.ThrowsAsync<PublicMediaException>(()=>client.DownloadAsync(Source,Policy,64));
        Assert.AreEqual(PublicMediaFailure.ResponseRejected,error.Failure);Assert.AreEqual(2,calls);
    }

    [TestMethod]
    public async Task RedirectCountIsBoundedEvenWithoutACycle()
    {
        var calls=0;
        await using var client=new PublicMediaClient(new Handler((_,_)=>Task.FromResult(Redirect("/hop"+(++calls)))),new(){MaximumRedirects=2});
        await Assert.ThrowsAsync<PublicMediaException>(()=>client.DownloadAsync(Source,Policy,64));
        Assert.AreEqual(3,calls);
    }

    [TestMethod]
    public async Task UnknownLengthReadsOnlyLimitPlusOneByte()
    {
        var stream=new CountedStream(new byte[1000]);
        await using var client=new PublicMediaClient(new Handler((_,_)=>Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=new StreamContent(stream)})),new());
        var error=await Assert.ThrowsAsync<PublicMediaException>(()=>client.DownloadAsync(Source,Policy,100));
        Assert.AreEqual(PublicMediaFailure.TooLarge,error.Failure); Assert.AreEqual(101,stream.ReadBytes);
        Assert.IsTrue(stream.WasDisposed);
    }

    [TestMethod]
    public async Task DeclaredOversizeIsRejectedBeforeReading()
    {
        var stream=new CountedStream(new byte[1000]);
        var response=new HttpResponseMessage(HttpStatusCode.OK){Content=new StreamContent(stream)};
        response.Content.Headers.ContentLength=1000;
        await using var client=new PublicMediaClient(new Handler((_,_)=>Task.FromResult(response)),new());
        var error=await Assert.ThrowsAsync<PublicMediaException>(()=>client.DownloadAsync(Source,Policy,100));
        Assert.AreEqual(PublicMediaFailure.TooLarge,error.Failure);Assert.AreEqual(0,stream.ReadBytes);Assert.IsTrue(stream.WasDisposed);
    }

    [TestMethod]
    public async Task CompressionEmptyAndTruncatedBodiesAreRejected()
    {
        foreach(var kind in new[]{"compression","empty","truncated","partial"})
        {
            var response=Ok(kind=="empty"?[]:[1,2,3]);
            if(kind=="compression")response.Content.Headers.ContentEncoding.Add("gzip");
            if(kind=="truncated")response.Content.Headers.ContentLength=6;
            if(kind=="partial")response.StatusCode=HttpStatusCode.PartialContent;
            await using var client=new PublicMediaClient(new Handler((_,_)=>Task.FromResult(response)),new());
            var error=await Assert.ThrowsAsync<PublicMediaException>(()=>client.DownloadAsync(Source,Policy,100));
            Assert.AreEqual(PublicMediaFailure.ResponseRejected,error.Failure,kind);
        }
    }

    [TestMethod]
    public async Task TimeoutIsBoundedAndSignedUrlsNeverEnterErrors()
    {
        await using(var client=new PublicMediaClient(new Handler(async(_,token)=>{await Task.Delay(Timeout.Infinite,token);return Ok([1]);}),
            new(){ConnectTimeout=TimeSpan.FromMilliseconds(10),OverallTimeout=TimeSpan.FromMilliseconds(80)}))
        {
            var error=await Assert.ThrowsAsync<PublicMediaException>(()=>client.DownloadAsync(Source,Policy,100));
            Assert.AreEqual(PublicMediaFailure.Timeout,error.Failure);
        }
        await using(var client=new PublicMediaClient(new Handler((_,_)=>throw new HttpRequestException("Failure at "+Source.AbsoluteUri)),new()))
        {
            var error=await Assert.ThrowsAsync<PublicMediaException>(()=>client.DownloadAsync(Source,Policy,100));
            Assert.AreEqual(PublicMediaFailure.Network,error.Failure);Assert.IsNull(error.InnerException);
            Assert.IsFalse(error.ToString().Contains("synthetic-secret",StringComparison.Ordinal));
            Assert.IsFalse(error.ToString().Contains("cdn.example",StringComparison.Ordinal));
        }
    }

    [TestMethod]
    public async Task QueueAndConcurrencyAreBoundedAndCancelledWaiterReleasesAdmission()
    {
        var entered=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);var sends=0;
        await using var client=new PublicMediaClient(new Handler(async(_,token)=>
        {
            Interlocked.Increment(ref sends);entered.TrySetResult();await release.Task.WaitAsync(token);return Ok([1]);
        }),new(){MaximumConcurrentRequests=1,MaximumQueuedRequests=1});
        var first=client.DownloadAsync(Source,Policy,64);await entered.Task.WaitAsync(TimeSpan.FromSeconds(3));
        using var cancel=new CancellationTokenSource();var queued=client.DownloadAsync(Source,Policy,64,cancel.Token);
        var busy=await Assert.ThrowsAsync<PublicMediaException>(()=>client.DownloadAsync(Source,Policy,64));
        Assert.AreEqual(PublicMediaFailure.Busy,busy.Failure); Assert.AreEqual(1,sends);
        cancel.Cancel();await Assert.ThrowsAsync<OperationCanceledException>(async()=>await queued);
        var replacement=client.DownloadAsync(Source,Policy,64);release.TrySetResult();
        await Task.WhenAll(first,replacement);Assert.AreEqual(2,sends);
    }

    [TestMethod]
    public async Task TeardownCancelsActiveAndQueuedWorkAndIsIdempotent()
    {
        var entered=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var client=new PublicMediaClient(new Handler(async(_,token)=>{entered.TrySetResult();await Task.Delay(Timeout.Infinite,token);return Ok([1]);}),
            new(){MaximumConcurrentRequests=1,MaximumQueuedRequests=1});
        var first=client.DownloadAsync(Source,Policy,64);await entered.Task.WaitAsync(TimeSpan.FromSeconds(3));
        var second=client.DownloadAsync(Source,Policy,64);
        await Task.WhenAll(client.DisposeAsync().AsTask(),client.DisposeAsync().AsTask()).WaitAsync(TimeSpan.FromSeconds(3));
        await Assert.ThrowsAsync<OperationCanceledException>(async()=>await first);
        await Assert.ThrowsAsync<OperationCanceledException>(async()=>await second);
        await Assert.ThrowsAsync<ObjectDisposedException>(()=>client.DownloadAsync(Source,Policy,64));
    }

    private static HttpResponseMessage Ok(byte[] data)=>new(HttpStatusCode.OK){Content=new ByteArrayContent(data)};
    private static HttpResponseMessage Redirect(string location)
    {
        var response=new HttpResponseMessage(HttpStatusCode.Found);response.Headers.Location=new(location,UriKind.RelativeOrAbsolute);return response;
    }
    private sealed class Handler(Func<HttpRequestMessage,CancellationToken,Task<HttpResponseMessage>> send):HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token)=>send(request,token);
    }
    private sealed class CountedStream(byte[] bytes):MemoryStream(bytes)
    {
        public override bool CanSeek=>false;
        internal int ReadBytes;internal bool WasDisposed;
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer,CancellationToken token=default)
        {var read=await base.ReadAsync(buffer,token);ReadBytes+=read;return read;}
        protected override void Dispose(bool disposing){WasDisposed=true;base.Dispose(disposing);}
    }
}
