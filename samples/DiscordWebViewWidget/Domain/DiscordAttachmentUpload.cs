using System.Net.Http.Headers;
using WidgetRail.WidgetProtocol;

namespace WidgetRail.Samples.DiscordWebView.Domain;

internal sealed record DiscordUploadTarget(string Id,string FileName,long ByteLength,string UploadUrl,string UploadedFileName);
internal sealed record DiscordUploadBatch(string Account,string ChannelId,string BatchId,long ExpiresAt,DiscordUploadTarget[] Files)
{
    internal void Validate(string account,string channel,IReadOnlyList<WidgetFileReference> files,DateTimeOffset now)
    {
        if(Account!=account || ChannelId!=channel || !WidgetFileContract.Token(BatchId) || ExpiresAt<=now.ToUnixTimeMilliseconds() ||
            ExpiresAt>now.AddMinutes(16).ToUnixTimeMilliseconds() || Files is null || Files.Length!=files.Count || Files.Length is <1 or >10)
            throw DiscordMessageValues.Invalid();
        for(var index=0;index<files.Count;index++)
        {
            var target=Files[index];var file=files[index];
            if(target is null || target.Id!=index.ToString(System.Globalization.CultureInfo.InvariantCulture) || target.FileName!=file.Name || target.ByteLength!=file.ByteLength ||
                !DiscordAttachmentUpload.ValidUrl(target.UploadUrl) || target.UploadedFileName is not {Length:>0 and <=1024} ||
                target.UploadedFileName.Any(c=>char.IsControl(c)||c is '\\' or ':') || target.UploadedFileName.Split('/').Any(part=>part is "" or "." or ".."))
                throw DiscordMessageValues.Invalid();
        }
    }
}

internal static class DiscordAttachmentUpload
{
    // A private first-party upload receipt is not permission to send arbitrary
    // requests. No redirects, cookies, bearer credentials, or full-file buffers.
    private static readonly HttpClient Client=new(new HttpClientHandler{AllowAutoRedirect=false,UseCookies=false,UseDefaultCredentials=false})
    {Timeout=TimeSpan.FromMinutes(10)};
    internal static bool ValidUrl(string? value)=>value is {Length:>0 and <=4096} &&
        Uri.TryCreate(value,UriKind.Absolute,out var uri) && uri.Scheme==Uri.UriSchemeHttps && uri.IsDefaultPort &&
        uri.Host=="discord-attachments-uploads-prd.storage.googleapis.com" && uri.UserInfo.Length==0 && uri.Fragment.Length==0 && uri.AbsolutePath!="/";

    internal static async Task PutAsync(DiscordUploadTarget target,Stream stream,CancellationToken token,HttpClient? client=null,Action<long>? progress=null)
    {
        if(!ValidUrl(target.UploadUrl) || !stream.CanRead || !stream.CanSeek || stream.Length!=target.ByteLength || stream.Position!=0)
            throw DiscordMessageValues.Invalid();
        using var request=new HttpRequestMessage(HttpMethod.Put,target.UploadUrl);
        request.Content=new UploadContent(stream,target.ByteLength,progress);
        request.Content.Headers.ContentType=new MediaTypeHeaderValue("application/octet-stream");
        request.Content.Headers.ContentLength=target.ByteLength;
        try
        {
            using var response=await (client??Client).SendAsync(request,HttpCompletionOption.ResponseHeadersRead,token);
            if(!response.IsSuccessStatusCode)throw new InvalidOperationException("Attachment upload was not accepted.");
        }
        catch(OperationCanceledException) when(token.IsCancellationRequested){throw;}
        catch(Exception error) when(error is HttpRequestException or IOException or OperationCanceledException)
        {throw new InvalidOperationException("Attachment upload could not be completed.");}
    }

    private sealed class UploadContent(Stream source,long length,Action<long>? progress):HttpContent
    {
        protected override bool TryComputeLength(out long value){value=length;return true;}
        protected override Task SerializeToStreamAsync(Stream destination,System.Net.TransportContext? context)=>
            SerializeToStreamAsync(destination,context,CancellationToken.None);
        protected override async Task SerializeToStreamAsync(Stream destination,System.Net.TransportContext? context,CancellationToken token)
        {
            var buffer=new byte[64*1024];long transferred=0;
            while(transferred<length)
            {
                var read=await source.ReadAsync(buffer.AsMemory(0,(int)Math.Min(buffer.Length,length-transferred)),token);
                if(read==0)throw new IOException("Attachment ended before its admitted length.");
                await destination.WriteAsync(buffer.AsMemory(0,read),token);
                transferred+=read;progress?.Invoke(transferred);
            }
        }
    }
}
