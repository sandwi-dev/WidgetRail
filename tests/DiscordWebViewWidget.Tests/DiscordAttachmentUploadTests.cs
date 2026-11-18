using System.Net;
using WidgetRail.Samples.DiscordWebView.Domain;
using WidgetRail.WidgetProtocol;

internal static class DiscordAttachmentUploadTests
{
    private const string UploadUrl="https://discord-attachments-uploads-prd.storage.googleapis.com/pending/file?signature=private-token";
    private static DiscordUploadTarget Target(long length)=>new("0","capture.mp4",length,UploadUrl,"pending/capture.mp4");

    internal static async Task RunAsync(Action<bool,string> check)
    {
        check(DiscordAttachmentUpload.ValidUrl(UploadUrl) && DiscordAttachmentUpload.ValidUrl(UploadUrl.Replace(".com/",".com:443/",StringComparison.Ordinal)),
            "Attachment upload admits only the expected HTTPS receipt origin with its default port");
        foreach(var value in new[]{"http://discord-attachments-uploads-prd.storage.googleapis.com/file",
            "https://discord-attachments-uploads-prd.storage.googleapis.com:444/file",
            "https://user:password@discord-attachments-uploads-prd.storage.googleapis.com/file",
            "https://discord-attachments-uploads-prd.storage.googleapis.com.evil.invalid/file",
            "https://discord-attachments-uploads-prd.storage.googleapis.com/",
            UploadUrl+"#fragment","file:///C:/capture.mp4"})
            check(!DiscordAttachmentUpload.ValidUrl(value),"Unapproved attachment URL is rejected before network activity");

        var length=16L*1024*1024+19;var progress=new List<long>();
        using(var source=new GeneratedStream(length))
        using(var handler=new FixtureHandler(async(request,token)=>
        {
            check(source.ReadCalls==0,"Upload stream is not read or buffered before the request is admitted");
            check(request.Method==HttpMethod.Put && request.RequestUri?.AbsoluteUri==UploadUrl,
                "Upload sends one PUT to the exact approved receipt URL");
            check(request.Headers.Authorization is null && !request.Headers.Contains("Cookie") &&
                request.Content?.Headers.ContentType?.MediaType=="application/octet-stream" && request.Content.Headers.ContentLength==length,
                "Upload supplies the exact byte length without Discord credentials or cookies");
            using var destination=new CountingSink();
            await request.Content!.CopyToAsync(destination,token);
            check(destination.Count==length && source.Position==length && source.MaximumRead<=64*1024 && source.ReadCalls>100,
                "A large attachment is streamed through bounded reads without retaining its whole contents");
            return new(HttpStatusCode.OK);
        }))
        using(var client=new HttpClient(handler))
        {
            await DiscordAttachmentUpload.PutAsync(Target(length),source,default,client,progress.Add);
            check(handler.Requests==1,"Successful upload requires exactly one request");
            check(progress.Count>100 && progress[^1]==length && progress.Zip(progress.Skip(1)).All(pair=>pair.First<pair.Second),
                "Upload progress follows completed bounded writes and reaches the exact admitted length monotonically");
        }

        using(var handler=new FixtureHandler((_,_)=>Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK))))
        using(var client=new HttpClient(handler))
        {
            using var mismatch=new GeneratedStream(51);
            await RejectAsync(()=>DiscordAttachmentUpload.PutAsync(Target(50),mismatch,default,client),"Length mismatch is rejected before issuing a request");
            using var offset=new GeneratedStream(50);offset.Position=1;
            await RejectAsync(()=>DiscordAttachmentUpload.PutAsync(Target(50),offset,default,client),"Nonzero source position cannot silently upload a suffix");
            using var valid=new GeneratedStream(50);
            await RejectAsync(()=>DiscordAttachmentUpload.PutAsync(Target(50) with{UploadUrl="https://evil.invalid/file"},valid,default,client),
                "An unapproved destination is rejected by the upload operation itself");
            check(handler.Requests==0,"Invalid source or destination cannot reach HttpClient");
        }

        foreach(var status in new[]{HttpStatusCode.Redirect,HttpStatusCode.Forbidden,HttpStatusCode.InternalServerError})
        {
            using var handler=new FixtureHandler((_,_)=>
            {
                var response=new HttpResponseMessage(status);response.Headers.Location=new Uri("https://evil.invalid/redirect");return Task.FromResult(response);
            });
            using var client=new HttpClient(handler);using var source=new GeneratedStream(50);
            try{await DiscordAttachmentUpload.PutAsync(Target(50),source,default,client);check(false,"Non-success upload must fail");}
            catch(InvalidOperationException exception)
            {check(!exception.ToString().Contains("private-token",StringComparison.Ordinal) && !exception.ToString().Contains(UploadUrl,StringComparison.Ordinal),
                "Rejected upload does not expose its signed URL");}
            check(handler.Requests==1,"Redirect and failure responses are not retried or followed by upload code");
        }

        using(var handler=new FixtureHandler((_,_)=>throw new HttpRequestException("Transport failed for "+UploadUrl)))
        using(var client=new HttpClient(handler))
        using(var source=new GeneratedStream(50))
        {
            try{await DiscordAttachmentUpload.PutAsync(Target(50),source,default,client);check(false,"Transport failure must surface");}
            catch(Exception exception)
            {check(!exception.ToString().Contains("private-token",StringComparison.Ordinal) && !exception.ToString().Contains(UploadUrl,StringComparison.Ordinal),
                "Transport failure does not expose the signed URL in its message or inner exception");}
        }
        using(var cancellation=new CancellationTokenSource())
        using(var handler=new FixtureHandler(async(_,token)=>{cancellation.Cancel();await Task.Delay(Timeout.Infinite,token);return new(HttpStatusCode.OK);}))
        using(var client=new HttpClient(handler))
        using(var source=new GeneratedStream(50))
        {
            try{await DiscordAttachmentUpload.PutAsync(Target(50),source,cancellation.Token,client);check(false,"Cancelled upload must stop");}
            catch(OperationCanceledException){check(true,"Upload preserves cancellation instead of turning it into a retryable provider error");}
            check(handler.Requests==1,"Cancelled upload never retries");
        }

        var now=DateTimeOffset.FromUnixTimeMilliseconds(1000000);
        WidgetFileReference[] files=[new(new string('a',32),"capture.mp4","video/mp4",50,now.AddHours(1).ToUnixTimeMilliseconds()),
            new(new string('b',32),"capture.png","image/png",20,now.AddHours(1).ToUnixTimeMilliseconds())];
        var batch=new DiscordUploadBatch("1","10",new string('c',32),now.AddMinutes(15).ToUnixTimeMilliseconds(),
            [Target(50),new("1","capture.png",20,UploadUrl,"pending/capture.png")]);
        batch.Validate("1","10",files,now);check(true,"Upload receipt matches the exact admitted file order and metadata");
        Reject(()=>batch.Validate("2","10",files,now),"Upload receipt cannot cross accounts");
        Reject(()=>batch.Validate("1","20",files,now),"Upload receipt cannot cross conversations");
        Reject(()=>(batch with{Files=batch.Files.Reverse().ToArray()}).Validate("1","10",files,now),"Reordered upload targets cannot bind to the wrong selected files");
        Reject(()=>(batch with{Files=[batch.Files[0],batch.Files[0]]}).Validate("1","10",files,now),"Duplicate target identities are rejected");
        Reject(()=>(batch with{ExpiresAt=now.ToUnixTimeMilliseconds()}).Validate("1","10",files,now),"Expired upload receipt is rejected");
        Reject(()=>(batch with{ExpiresAt=now.AddMinutes(17).ToUnixTimeMilliseconds()}).Validate("1","10",files,now),"Unbounded upload receipt lifetime is rejected");
        Reject(()=>(batch with{BatchId="not-a-token"}).Validate("1","10",files,now),"Malformed upload batch identity is rejected");
        Reject(()=>(batch with{Files=[batch.Files[0] with{ByteLength=51},batch.Files[1]]}).Validate("1","10",files,now),"Receipt byte length must equal the admitted file length");
        foreach(var name in new[]{"../capture.mp4","pending/../capture.mp4","pending\\capture.mp4","C:/capture.mp4","pending//capture.mp4"})
            Reject(()=>(batch with{Files=[batch.Files[0] with{UploadedFileName=name},batch.Files[1]]}).Validate("1","10",files,now),
                "Uploaded filename cannot introduce an ambiguous provider path");

        void Reject(Action action,string name)
        {try{action();check(false,name);}catch(InvalidDataException){check(true,name);}}
        async Task RejectAsync(Func<Task> action,string name)
        {try{await action();check(false,name);}catch(InvalidDataException){check(true,name);}}
    }

    private sealed class FixtureHandler(Func<HttpRequestMessage,CancellationToken,Task<HttpResponseMessage>> send):HttpMessageHandler
    {
        internal int Requests;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken cancellationToken)
        {Requests++;return send(request,cancellationToken);}
    }
    private sealed class GeneratedStream(long length):Stream
    {
        private long position;
        internal int ReadCalls,MaximumRead;
        public override bool CanRead=>true;public override bool CanSeek=>true;public override bool CanWrite=>false;
        public override long Length=>length;
        public override long Position{get=>position;set{if(value<0||value>length)throw new ArgumentOutOfRangeException(nameof(value));position=value;}}
        public override int Read(byte[] buffer,int offset,int count)=>Read(buffer.AsSpan(offset,count));
        public override int Read(Span<byte> buffer)
        {ReadCalls++;MaximumRead=Math.Max(MaximumRead,buffer.Length);var count=(int)Math.Min(buffer.Length,length-position);buffer[..count].Fill(42);position+=count;return count;}
        public override ValueTask<int> ReadAsync(Memory<byte> buffer,CancellationToken cancellationToken=default)
        {cancellationToken.ThrowIfCancellationRequested();return ValueTask.FromResult(Read(buffer.Span));}
        public override long Seek(long offset,SeekOrigin origin)=>Position=origin switch{SeekOrigin.Begin=>offset,SeekOrigin.Current=>Position+offset,SeekOrigin.End=>length+offset,_=>throw new ArgumentOutOfRangeException(nameof(origin))};
        public override void Flush(){}public override void SetLength(long value)=>throw new NotSupportedException();
        public override void Write(byte[] buffer,int offset,int count)=>throw new NotSupportedException();
    }
    private sealed class CountingSink:Stream
    {
        internal long Count;
        public override bool CanRead=>false;public override bool CanSeek=>false;public override bool CanWrite=>true;
        public override long Length=>Count;public override long Position{get=>Count;set=>throw new NotSupportedException();}
        public override void Write(byte[] buffer,int offset,int count)=>Count+=count;
        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer,CancellationToken cancellationToken=default)
        {cancellationToken.ThrowIfCancellationRequested();Count+=buffer.Length;return ValueTask.CompletedTask;}
        public override void Flush(){}public override int Read(byte[] buffer,int offset,int count)=>throw new NotSupportedException();
        public override long Seek(long offset,SeekOrigin origin)=>throw new NotSupportedException();public override void SetLength(long value)=>throw new NotSupportedException();
    }
}
