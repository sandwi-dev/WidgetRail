using System.Net;
using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WidgetRail.Samples.GameHelp;

namespace WidgetRail.GameHelpWidget.Tests;

[TestClass]
public sealed class GameHelpDiagnosticsTests
{
    [TestMethod]
    public void FailureRecordsStageWithoutPrivateExceptionDetailsAndSuccessSupersedesOldFailure()
    {
        var directory = Path.Combine(Path.GetTempPath(), "widgetrail-diagnostics-" + Guid.NewGuid().ToString("N"));
        try
        {
            var trace = new GameHelpRequestDiagnostics(true, directory);
            trace.Started();
            trace.SetStage("search-attribution");
            var error = trace.Failed(new InvalidOperationException("private response, question or key", new Exception("private inner detail")));
            StringAssert.Contains(error.Message, "Gemini responded");
            StringAssert.Contains(error.Message, trace.Id);
            Assert.IsFalse(error.ToString().Contains("private"));
            var saved = File.ReadAllText(Path.Combine(directory, "last-provider-error.json"));
            Assert.IsFalse(saved.Contains("private"));
            using var record = JsonDocument.Parse(saved);
            Assert.AreEqual("search-attribution", record.RootElement.GetProperty("stage").GetString());
            Assert.AreEqual("System.InvalidOperationException", record.RootElement.GetProperty("exceptionType").GetString());
            Assert.IsTrue(record.RootElement.GetProperty("grounding").GetBoolean());
            var next = new GameHelpRequestDiagnostics(false, directory);
            next.Completed();
            using var latest = JsonDocument.Parse(File.ReadAllText(Path.Combine(directory, "last-request.json")));
            Assert.AreEqual("completed", latest.RootElement.GetProperty("outcome").GetString());
            Assert.AreEqual(next.Id, latest.RootElement.GetProperty("requestId").GetString());
            Assert.AreEqual(saved, File.ReadAllText(Path.Combine(directory, "last-provider-error.json")));
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    [TestMethod]
    public void DiagnosticWriteFailureCannotHideOriginalProviderError()
    {
        var file = Path.GetTempFileName();
        try
        {
            var trace = new GameHelpRequestDiagnostics(true, file);
            trace.Started();
            var error = trace.Failed(new GameHelpException("rate_limited", "Request limit reached.", httpStatus: 429));
            Assert.AreEqual("rate_limited", error.Code);
            Assert.AreEqual(429, error.HttpStatus);
            StringAssert.Contains(error.Message, "Request limit reached.");
            trace.Cancelled();
        }
        finally { File.Delete(file); }
    }

    [TestMethod]
    public async Task ClientDistinguishesTimeoutFromResponseParsingWithoutSendingNetworkRequests()
    {
        var request = new GameHelpRequest("Fixture", "", "Hints first", [new("user", "Hint please")], true);
        var stage = "";
        using var timeoutClient = new GeminiGameHelpClient(new(new Handler((_, _) => throw new TaskCanceledException("private error"))));
        var timeout = await Assert.ThrowsAsync<GameHelpException>(() => timeoutClient.AskAsync(request, "fixture-key-not-real-12345", default, value => stage = value));
        Assert.AreEqual("timed_out", timeout.Code);
        Assert.AreEqual("provider-request", stage);
        using var malformedClient = new GeminiGameHelpClient(new(new Handler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        { Content = new StringContent("{\"status\":\"completed\",\"steps\":[]}") }))));
        var malformed = await Assert.ThrowsAsync<GameHelpException>(() => malformedClient.AskAsync(request, "fixture-key-not-real-12345", default, value => stage = value));
        Assert.AreEqual("invalid_response", malformed.Code);
        Assert.AreEqual("response-parsing", stage);
    }

    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => respond(request, cancellationToken);
    }
}
