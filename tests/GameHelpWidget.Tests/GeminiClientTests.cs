using System.Net;
using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WidgetRail.Samples.GameHelp;
using WidgetRail.WidgetProtocol;

namespace WidgetRail.GameHelpWidget.Tests;

[TestClass]
public sealed class GeminiClientTests
{
    private static GameHelpRequest Request => new("Fixture game", "Fixture area", "Hints first", [new("user", "Where next?")], false);
    private const string FakeKey = "fixture-key-never-a-real-key";

    [TestMethod]
    public async Task UploadUsesFixedEndpointHeaderAndBoundedStructuredRequest()
    {
        using var handler = new Handler(async (message, token) =>
        {
            Assert.AreEqual("https://generativelanguage.googleapis.com/v1beta/interactions", message.RequestUri!.AbsoluteUri);
            Assert.AreEqual(FakeKey, message.Headers.GetValues("x-goog-api-key").Single());
            using var json = JsonDocument.Parse(await message.Content!.ReadAsByteArrayAsync(token));
            var root = json.RootElement;
            Assert.AreEqual("gemini-3.5-flash-lite", root.GetProperty("model").GetString());
            Assert.IsFalse(root.GetProperty("store").GetBoolean());
            Assert.AreEqual("application/json", root.GetProperty("response_format").GetProperty("mime_type").GetString());
            Assert.IsFalse(root.TryGetProperty("tools", out _));
            Assert.AreEqual("image", root.GetProperty("input")[0].GetProperty("type").GetString());
            Assert.AreEqual("AQID", root.GetProperty("input")[0].GetProperty("data").GetString());
            using var context = JsonDocument.Parse(root.GetProperty("input")[1].GetProperty("text").GetString()!);
            Assert.AreEqual("Fixture game", context.RootElement.GetProperty("detectedApplication").GetString());
            Assert.AreEqual("Fixture area", context.RootElement.GetProperty("windowTitle").GetString());
            return GoodResponse();
        });
        using var client = new GeminiGameHelpClient(new(handler));
        var attachment = new CaptureAttachment(new string('a', 32), "image/png", 10, 10, 3, 0, DateTimeOffset.UtcNow.AddMinutes(1).ToUnixTimeMilliseconds());
        var request = Request with { Attachment = attachment with { SourceApplication = new(Request.ApplicationName, Request.WindowTitle) }, Content = [1, 2, 3] };
        Assert.IsFalse(JsonSerializer.Serialize(request).Contains("AQID"), "Pixels never cross worker request JSON.");
        var answer = await client.AskAsync(request, FakeKey, default);
        Assert.AreEqual("Try the path on the left.", answer.Answer);
        Assert.AreEqual(1, handler.Calls);
    }

    [TestMethod]
    public void StructuredRepliesKeepButtonLabelsSeparateFromSentMessagesAndSolution()
    {
        var response = JsonSerializer.SerializeToUtf8Bytes(new { status = "completed", steps = new[] { new { type = "model_output", content = new[] { new { type = "text",
            text = JsonSerializer.Serialize(new { hint = "Try a side route.", detailedSolution = "Go through the office.", observations = Array.Empty<string>(), uncertainty = "",
                replyOptions = new[] { new { label = "Another hint", message = "Give me another hint." } }, searchQueries = Array.Empty<string>() }) } } } } });
        var answer = GeminiGameHelpClient.ParseResponse(response, false);
        Assert.AreEqual("Try a side route.", answer.Answer);
        Assert.AreEqual("Go through the office.", answer.DetailedSolution);
        Assert.AreEqual("Another hint", answer.ReplyOptions.Single().Label);
        Assert.AreEqual("Give me another hint.", answer.ReplyOptions.Single().Message);
        Assert.IsTrue(GameHelpContract.IsValidAnswer(answer));
        Assert.IsFalse(GameHelpContract.IsValidAnswer(answer with { ReplyOptions = [new("Label", "")] }));
    }

    [TestMethod]
    public async Task GroundingIsExplicitAndPreservesOriginalSearchSuggestions()
    {
        const string html = "<div><a href=\"https://www.google.com/search?q=fixture\">Original search suggestion</a></div>";
        using var handler = new Handler(async (message, token) =>
        {
            using var request = JsonDocument.Parse(await message.Content!.ReadAsByteArrayAsync(token));
            Assert.AreEqual("google_search", request.RootElement.GetProperty("tools")[0].GetProperty("type").GetString());
            var answer = JsonSerializer.Serialize(new { hint = "Grounded fixture", detailedSolution = "", observations = Array.Empty<string>(), uncertainty = "", replyOptions = Array.Empty<GameHelpReplyOption>(), searchQueries = Array.Empty<string>() });
            object[] steps = [new { type = "google_search_result", result = new[] { new { search_suggestions = html } } },
                new { type = "model_output", content = new[] { new { type = "text", text = answer,
                    annotations = new[] { new { type = "url_citation", url = "https://example.com/guide", title = "Fixture guide" } } } } }];
            return new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(new { status = "completed", steps })) };
        });
        using var client = new GeminiGameHelpClient(new(handler));
        var response = await client.AskAsync(Request with { WebGrounding = true }, FakeKey, default);
        Assert.AreEqual(html, response.SearchSuggestionsHtml!.Single());
        Assert.AreEqual("https://example.com/guide", response.Sources.Single().Url);
        Assert.IsNull(response.Attribution, "The broker, not the provider client, issues presentation authority.");
        Assert.AreEqual(1, handler.Calls);
    }

    [TestMethod]
    public async Task ProviderFailureAndCancellationDoNotRetryOrExposeResponseBody()
    {
        using var handler = new Handler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.TooManyRequests)
        { Content = new StringContent("private-provider-detail") }));
        using var client = new GeminiGameHelpClient(new(handler));
        var error = await Assert.ThrowsAsync<GameHelpException>(() => client.AskAsync(Request, FakeKey, default));
        Assert.AreEqual("rate_limited", error.Code);
        Assert.IsFalse(error.Message.Contains("private-provider-detail"));
        Assert.AreEqual(1, handler.Calls);
        using var waiting = new Handler(async (_, token) => { await Task.Delay(Timeout.Infinite, token); return GoodResponse(); });
        using var canceledClient = new GeminiGameHelpClient(new(waiting));
        using var cancel = new CancellationTokenSource();
        var pending = canceledClient.AskAsync(Request, FakeKey, cancel.Token);
        cancel.Cancel();
        await Assert.ThrowsAsync<OperationCanceledException>(async () => await pending);
        Assert.AreEqual(1, waiting.Calls);
    }

    [TestMethod]
    [DataRow(400, "request_rejected")]
    [DataRow(401, "key_rejected")]
    [DataRow(403, "key_rejected")]
    [DataRow(404, "model_or_endpoint_not_found")]
    [DataRow(408, "timed_out")]
    [DataRow(413, "context_too_large")]
    [DataRow(422, "request_rejected")]
    [DataRow(429, "rate_limited")]
    [DataRow(500, "provider_unavailable")]
    [DataRow(503, "provider_unavailable")]
    [DataRow(504, "timed_out")]
    [DataRow(302, "provider_http_error")]
    public async Task HttpErrorsKeepSafeStatusAndNeverExposeProviderDetailsOrRetry(int status, string expectedCode)
    {
        using var handler = new Handler((_, _) => Task.FromResult(new HttpResponseMessage((HttpStatusCode)status)
        {
            Content = new StringContent("private-provider-detail"), ReasonPhrase = "private-reason",
        }));
        using var client = new GeminiGameHelpClient(new(handler));
        var error = await Assert.ThrowsAsync<GameHelpException>(() => client.AskAsync(Request, FakeKey, default));
        Assert.AreEqual(expectedCode, error.Code);
        Assert.AreEqual(status, error.HttpStatus);
        StringAssert.Contains(error.Message, $"HTTP {status}");
        Assert.IsFalse(error.ToString().Contains("private-"));
        Assert.IsFalse(error.ToString().Contains(FakeKey));
        Assert.AreEqual(1, handler.Calls);
    }

    [TestMethod]
    public async Task MalformedContextNeverStartsHttpRequest()
    {
        using var handler = new Handler((_, _) => Task.FromResult(GoodResponse()));
        using var client = new GeminiGameHelpClient(new(handler));
        await Assert.ThrowsAsync<GameHelpException>(() => client.AskAsync(Request with { Conversation = null! }, FakeKey, default));
        await Assert.ThrowsAsync<GameHelpException>(() => client.AskAsync(Request with { Conversation = [null!] }, FakeKey, default));
        Assert.AreEqual(0, handler.Calls);
        Assert.IsFalse(GameHelpContract.IsValidAnswer(new("Answer", [], "", [], [], [new("Unsafe", "file:///C:/secret")])));
        var incomplete = await GoodResponse().Content.ReadAsByteArrayAsync();
        var parsed = System.Text.Json.Nodes.JsonNode.Parse(incomplete)!;
        parsed["status"] = "incomplete";
        var error = Assert.Throws<GameHelpException>(() => GeminiGameHelpClient.ParseResponse(System.Text.Encoding.UTF8.GetBytes(parsed.ToJsonString()), false));
        Assert.AreEqual("provider_incomplete", error.Code);
    }

    private static HttpResponseMessage GoodResponse()
    {
        var answer = JsonSerializer.Serialize(new { hint = "Try the path on the left.", detailedSolution = "", observations = Array.Empty<string>(), uncertainty = "", replyOptions = Array.Empty<GameHelpReplyOption>(), searchQueries = Array.Empty<string>() });
        return new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(new { status = "completed", steps = new[] { new { type = "model_output", content = new[] { new { type = "text", text = answer } } } } })) };
    }
    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond) : HttpMessageHandler
    {
        internal int Calls;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        { Calls++; return respond(request, cancellationToken); }
    }
}
