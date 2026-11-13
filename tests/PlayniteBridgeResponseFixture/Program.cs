using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using PlayniteBridge.Server;

internal static class Program
{
    private static int Main(string[] args)
    {
        try { Run(args.Length > 0 && args[0] == "--expect-escape").GetAwaiter().GetResult(); return 0; }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }
    private static async Task Run(bool expectEscape)
    {
        var reservation = new TcpListener(IPAddress.Loopback, 0); reservation.Start();
        var port = ((IPEndPoint)reservation.LocalEndpoint).Port; reservation.Stop();
        using (var listener = new HttpListener())
        {
            listener.Prefixes.Add("http://localhost:" + port + "/"); listener.Start();
            var router = new Router();
            var server = new HttpApiServer(port, new PlayniteBridge.Handlers.AuthHandler(), router);
            var handler = typeof(HttpApiServer).GetMethod("HandleRequest", BindingFlags.Instance | BindingFlags.NonPublic);
            for (var iteration = 0; iteration < 3; iteration++)
            {
                var entered = new ManualResetEventSlim(); var proceed = new ManualResetEventSlim();
                router.Handler = context => { entered.Set(); if (!proceed.Wait(5000)) throw new TimeoutException("Client did not release the route"); return new { ok = true }; };
                var accept = listener.GetContextAsync();
                var client = new TcpClient(); client.Client.LingerState = new LingerOption(true, 0);
                client.Connect("localhost", port);
                var request = Encoding.ASCII.GetBytes("GET /cancelled HTTP/1.1\r\nHost: localhost:" + port + "\r\nAuthorization: Bearer fixture\r\nConnection: close\r\n\r\n");
                client.GetStream().Write(request, 0, request.Length);
                var context = await accept;
                var handling = Task.Run(() => Invoke(handler, server, context));
                if (!entered.Wait(5000)) throw new TimeoutException("Server route did not start");
                client.Close(); await Task.Delay(100); proceed.Set();
                var failure = await handling;
                if (!expectEscape && failure != null) throw failure;
                Console.WriteLine("disconnect " + iteration + ": " + (failure == null ? "contained" : failure.GetType().Name));
                entered.Dispose(); proceed.Dispose();
            }
            router.Handler = context => new PlayniteBridge.Handlers.ImageResult
                { Data = new byte[8 * 1024 * 1024], ContentType = "application/octet-stream" };
            var imageAccept = listener.GetContextAsync();
            using (var imageClient = new TcpClient())
            {
                imageClient.ReceiveBufferSize = 1024;
                imageClient.ReceiveTimeout = 5000;
                imageClient.Connect("localhost", port);
                var bytes = Encoding.ASCII.GetBytes("GET /abort-image HTTP/1.1\r\nHost: localhost:" + port + "\r\nAuthorization: Bearer fixture\r\nConnection: close\r\n\r\n");
                imageClient.GetStream().Write(bytes, 0, bytes.Length);
                var imageContext = await imageAccept;
                var imageSend = Task.Run(() => Invoke(handler, server, imageContext));
                if (imageClient.GetStream().ReadByte() < 0) throw new IOException("Image response did not start");
                imageContext.Response.Abort();
                imageClient.Close();
                var abortFailure = await imageSend;
                if (expectEscape != (abortFailure != null)) throw new InvalidOperationException("Unexpected committed-response abort outcome", abortFailure);
                Console.WriteLine("committed response abort: " + (abortFailure == null ? "contained" : abortFailure.GetType().Name));
            }
            router.Handler = context => new { ok = true };
            var next = listener.GetContextAsync();
            var query = (HttpWebRequest)WebRequest.Create("http://localhost:" + port + "/ok");
            query.Headers[HttpRequestHeader.Authorization] = "Bearer fixture";
            var response = query.GetResponseAsync();
            var accepted = await next;
            var send = Task.Run(() => Invoke(handler, server, accepted));
            using (var received = await response)
            using (var reader = new StreamReader(received.GetResponseStream()))
                if (!reader.ReadToEnd().Contains("true")) throw new InvalidDataException("Subsequent JSON request failed");
            if (await send != null) throw new InvalidOperationException("Subsequent response escaped");
            Console.WriteLine("subsequent request: passed");
            await CheckResponse(listener, handler, server, router, port, "OPTIONS", false, () => new { ok = true }, 204, null);
            await CheckResponse(listener, handler, server, router, port, "GET", false, () => new { ok = true }, 401, "Unauthorized");
            await CheckResponse(listener, handler, server, router, port, "GET", true, () => { throw new InvalidOperationException("fixture route error"); }, 500, "fixture route error");
            var circular = new Circular(); circular.Self = circular;
            await CheckResponse(listener, handler, server, router, port, "GET", true, () => circular, 500, "error");
            await CheckResponse(listener, handler, server, router, port, "GET", true, () => null, 404, "Not found");
            await CheckResponse(listener, handler, server, router, port, "GET", true, () => new PlayniteBridge.Handlers.ImageResult { Data = null, ContentType = "image/png" }, 500, "error");
        }
    }
    private sealed class Circular { public Circular Self { get; set; } }
    private static async Task CheckResponse(HttpListener listener, MethodInfo handler, HttpApiServer server, Router router,
        int port, string method, bool authorize, Func<object> route, int status, string expectedText)
    {
        router.Handler = context => route();
        var accept = listener.GetContextAsync();
        var query = (HttpWebRequest)WebRequest.Create("http://localhost:" + port + "/check");
        query.Method = method; query.Timeout = 5000;
        if (authorize) query.Headers[HttpRequestHeader.Authorization] = "Bearer fixture";
        var receive = query.GetResponseAsync();
        var context = await accept;
        var send = Task.Run(() => Invoke(handler, server, context));
        WebResponse response;
        try { response = await receive; }
        catch (WebException error) when (error.Response != null) { response = error.Response; }
        using (response)
        using (var reader = new StreamReader(response.GetResponseStream()))
        {
            var body = reader.ReadToEnd();
            if ((int)((HttpWebResponse)response).StatusCode != status || expectedText != null && !body.Contains(expectedText))
                throw new InvalidDataException("Unexpected HTTP response for " + method + ": " + status);
        }
        var failure = await send; if (failure != null) throw failure;
        Console.WriteLine("HTTP " + status + " response: passed");
    }
    private static Exception Invoke(MethodInfo method, object target, HttpListenerContext context)
    {
        try { method.Invoke(target, new object[] { context }); return null; }
        catch (TargetInvocationException error) { return error.InnerException; }
    }
}
namespace Playnite.SDK
{
    internal interface ILogger { void Info(string text); void Warn(string text); void Error(string text); void Error(Exception error, string text); }
    internal static class LogManager { internal static ILogger GetLogger() { return new Logger(); } }
    internal sealed class Logger : ILogger
    {
        public void Info(string text) { } public void Warn(string text) { } public void Error(string text) { }
        public void Error(Exception error, string text) { Console.WriteLine("request error: " + error.GetType().Name); }
    }
}
namespace PlayniteBridge.Handlers
{
    internal sealed class AuthHandler { internal bool ValidateAuth(string header) { return header == "Bearer fixture"; } }
    internal sealed class ImageResult { public byte[] Data; public string ContentType; }
}
namespace PlayniteBridge.Server
{
    internal sealed class RequestContext { internal static RequestContext FromHttpRequest(HttpListenerRequest request) { return new RequestContext(); } }
    internal sealed class Router { internal Func<RequestContext, object> Handler; internal object Route(RequestContext context) { return Handler(context); } }
}
