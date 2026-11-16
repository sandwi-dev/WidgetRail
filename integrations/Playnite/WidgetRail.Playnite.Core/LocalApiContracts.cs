using System;
using System.Collections.Specialized;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace WidgetRail.Playnite;

public sealed class LocalApiRequest
{
    public string Method { get; }
    public string Path { get; }
    public NameValueCollection Query { get; }
    public byte[] Body { get; }
    public string? CommandId { get; }
    public LocalApiRequest(string method, string path, NameValueCollection query, byte[] body, string? commandId)
    { Method = method; Path = path; Query = query; Body = body; CommandId = commandId; }
}

public sealed class LocalApiResponse
{
    public int Status { get; }
    public string ContentType { get; }
    public byte[] Body { get; }
    public string? ETag { get; }
    public LocalApiResponse(int status, byte[] body, string contentType = "application/json; charset=utf-8", string? etag = null)
    {
        if (body.Length > IntegrationIdentity.MaximumResponseBytes) throw new ArgumentOutOfRangeException(nameof(body));
        Status = status; Body = body; ContentType = contentType; ETag = etag;
    }
    public static LocalApiResponse Error(int status, string code) => new LocalApiResponse(status,
        Encoding.UTF8.GetBytes("{\"error\":{\"code\":\"" + code + "\"}}"));
}

public interface ILocalApiDispatcher
{
    Task<LocalApiResponse> DispatchAsync(LocalApiRequest request, CancellationToken token);
}
