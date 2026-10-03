using System.Text;
using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WidgetRail.Samples.GameHelp;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

namespace WidgetRail.GameHelpWidget.Tests;

[TestClass]
public sealed class GameHelpCitationTests
{
    [TestMethod]
    public void ByteOffsetsMapUtf8EscapesTrimAndRepeatedText()
    {
        const string json = """{"hint":"  Café 😀: open \"door\".\nThen go.  ","detailedSolution":"Café 😀: open \"door\".","observations":["First","First"],"uncertainty":"","replyOptions":[],"searchQueries":[]}""";
        var first = Range(json, "Café 😀");
        var door = Range(json, "open \\\"door\\\"");
        var second = Range(json, "Café 😀", json.IndexOf("detailedSolution", StringComparison.Ordinal));
        var answer = Parse(json, [Annotation(first, 1), Annotation(door, 2), Annotation(second, 1)]);
        Assert.IsTrue(GameHelpContract.IsValidAnswer(answer));
        Assert.AreEqual("Café 😀", Slice(answer, answer.Citations[0]));
        Assert.AreEqual("open \"door\"", Slice(answer, answer.Citations[1]));
        Assert.AreEqual("detailedSolution", answer.Citations[2].Field);
        Assert.AreEqual(1, answer.Citations[2].SourceNumber);
        Assert.AreEqual(2, answer.Observations.Count, "Removing duplicate array entries would shift citation indices.");
        Assert.AreEqual(0, answer.Citations[0].Start, "Account for leading whitespace trim.");
    }

    [TestMethod]
    public void EscapedUnicodeAndNewlinesDecodeAndInvalidRangesRemainAggregateOnly()
    {
        const string json = """{"hint":"\u00e9\uD83D\uDE00\nDoor","detailedSolution":"","observations":[],"uncertainty":"","replyOptions":[],"searchQueries":[]}""";
        var valid = Range(json, """\u00e9\uD83D\uDE00\n""");
        var answer = Parse(json, [Annotation(valid, 1), Annotation((valid.Start + 1, valid.End), 2),
            new { type = "url_citation", title = "No range", url = "https://example.com/3" }]);
        Assert.AreEqual(3, answer.Sources.Count);
        Assert.AreEqual(1, answer.Citations.Count);
        Assert.AreEqual("é😀\n", Slice(answer, answer.Citations[0]));
    }

    [TestMethod]
    public void RepeatedSourcesShareNumbersWithoutEightSourceOrTwentyAnnotationTruncation()
    {
        var json = AnswerJson("Follow the path.", "Follow the path.");
        var span = Range(json, "Follow the path.");
        var answer = Parse(json, Enumerable.Range(0, 32).Select(i => Annotation(span, i % 12 + 1)).ToArray());
        Assert.AreEqual(12, answer.Sources.Count);
        Assert.AreEqual(12, answer.Citations.Count);
        CollectionAssert.AreEqual(Enumerable.Range(1, 12).ToArray(), answer.Citations.Select(c => c.SourceNumber).ToArray());
    }

    [TestMethod]
    public void ReplacementOutputCannotBorrowEarlierOutputCitations()
    {
        var first = AnswerJson("Old answer", ""); var final = AnswerJson("New answer", "");
        var root = System.Text.Json.Nodes.JsonNode.Parse(Response(final, []))!;
        root["steps"]!.AsArray().Insert(1, JsonSerializer.SerializeToNode(new { type = "model_output", content = new[] {
            new { type = "text", text = first, annotations = new[] { Annotation(Range(first, "Old answer"), 1) } } } }));
        var answer = GeminiGameHelpClient.ParseResponse(Encoding.UTF8.GetBytes(root.ToJsonString()), true);
        Assert.AreEqual("New answer", answer.Answer);
        Assert.AreEqual(0, answer.Sources.Count);
        Assert.AreEqual(0, answer.Citations.Count);
    }

    [TestMethod]
    public void DuplicateJsonFieldsCannotMoveACitationToDifferentText()
    {
        var json = AnswerJson("New answer", "").Replace("{", "{\"hint\":\"Old answer\",", StringComparison.Ordinal);
        Assert.Throws<GameHelpException>(() => Parse(json, [Annotation(Range(json, "Old answer"), 1)]));
    }

    [TestMethod]
    public void ExcessiveCitationMetadataIsRejectedRatherThanSilentlyDropped()
    {
        var json = AnswerJson("Hint", "");
        Assert.Throws<GameHelpException>(() => Parse(json,
            Enumerable.Range(0, 129).Select(i => Annotation(Range(json, "Hint"), i + 1)).ToArray()));
    }

    [TestMethod]
    public async Task SourcesCollapseIndependentlyAndInlineLinksRetainNumbersAndIntents()
    {
        var json = AnswerJson("Try the doorway.", "Move the boxes. Enter the vent.");
        var answer = Parse(json, [Annotation(Range(json, "Move the boxes"), 1), Annotation(Range(json, "Move the boxes"), 2),
            Annotation(Range(json, "Enter the vent"), 1)]) with
        { Attribution = new ProviderDocumentReference(new string('a', 32), ProviderDocumentReference.RestrictedHtml) };
        var service = new ReplyService(answer);
        var widget = WidgetTestHost.Attach(new WidgetRail.Samples.GameHelp.GameHelpWidget(service), new WidgetTestHostServicesBuilder().Build());
        try
        {
            await WidgetTestHost.InitializeAsync(widget); await WidgetTestHost.SetLifecycleStateAsync(widget, WidgetLifecycleState.Interactive);
            await Until(() => Nodes(widget).Any(n => n.Id == "help.composer"));
            await widget.OnActionAsync(new("starter.0", "help.starter.0"));
            await widget.OnActionAsync(new("ask.plain", "help.answer-plain"));
            await Until(() => Nodes(widget).Any(n => n.Id == "help.message.2.sources-toggle"));
            Assert.IsFalse(Nodes(widget).Any(n => n.Id.StartsWith("help.message.2.source.")));
            Assert.IsTrue(Nodes(widget).Any(n => n.Id == "help.message.2.attribution"));
            Assert.IsFalse(Nodes(widget).Any(n => n.Id.Contains(".citation.")), "Do not attach solution citations to an uncited hint.");
            await widget.OnActionAsync(new("solution.2", "help.message.2.solution-toggle"));
            var links = Nodes(widget).Where(n => n.Id.Contains(".citation.") && n.Intent is not null).ToArray();
            CollectionAssert.AreEqual(new[] { "¹", "²", "¹" }, links.Select(n => n.Text!).ToArray());
            Assert.AreEqual(ViewNodeKind.RichText, Nodes(widget).Single(n => n.Id == "help.message.2.solution-text.paragraph").Kind);
            Assert.IsTrue(links.All(n => n.Kind == ViewNodeKind.Button && n.Intent!.Presentation == WidgetIntentPresentation.PreferExistingSurface));
            Assert.AreEqual("https://example.com/1", links[0].Intent!.Payload.GetProperty("url").GetString());
            StringAssert.Contains(links[0].AccessibilityLabel!, "Move the boxes");
            var displayed = string.Concat(Nodes(widget).Where(n => n.Kind == ViewNodeKind.Text && n.Id.StartsWith("help.message.2.solution-text.") && !n.Id.Contains(".separator.")).Select(n => n.Text));
            Assert.AreEqual(answer.DetailedSolution, displayed, "Markers must not replace or drop answer text.");
            await widget.OnActionAsync(new("sources.2", "help.message.2.sources-toggle"));
            Assert.AreEqual("[1] Source 1", Nodes(widget).Single(n => n.Id == "help.message.2.source.0").Text);
            Assert.AreEqual("[2] Source 2", Nodes(widget).Single(n => n.Id == "help.message.2.source.1").Text);
            await widget.OnActionAsync(new("sources.2", "help.message.2.sources-toggle"));
            Assert.AreEqual(3, Nodes(widget).Count(n => n.Id.Contains(".citation.") && n.Intent is not null));
            Assert.IsTrue(Nodes(widget).Any(n => n.Id == "help.message.2.attribution"));
            Assert.AreEqual(1, service.Requests, "Expansion and collapse must not call Gemini again.");
            var snapshot = widget.RenderSnapshot("fixture", 1);
            Assert.AreEqual(0, ViewSnapshotValidator.Validate(snapshot).Count, string.Join("; ", ViewSnapshotValidator.Validate(snapshot)));
            Assert.AreEqual(0, WinUiPresentationContract.Validate(snapshot).Count);
        }
        finally { await WidgetTestHost.DestroyAsync(widget); }
    }

    private static IEnumerable<ViewNode> Nodes(Widget widget) => Walk(widget.RenderSnapshot("fixture", 1).Root);
    private static IEnumerable<ViewNode> Walk(ViewNode node) => new[] { node }.Concat(node.Children.SelectMany(Walk));
    private static async Task Until(Func<bool> ready)
    { var end = Environment.TickCount64 + 5000; while (!ready()) { if (Environment.TickCount64 > end) throw new TimeoutException(); await Task.Delay(20); } }
    private sealed class ReplyService(GameHelpAnswer answer) : IGameHelpService
    {
        internal int Requests;
        public Task<bool> HasKeyAsync(CancellationToken token) => Task.FromResult(true);
        public Task SaveKeyAsync(string key, CancellationToken token) => Task.CompletedTask;
        public Task DeleteKeyAsync(CancellationToken token) => Task.CompletedTask;
        public Task<GameHelpAnswer> AskAsync(GameHelpRequest request, CancellationToken token) { ++Requests; return Task.FromResult(answer); }
    }

    private static string AnswerJson(string hint, string detail) => JsonSerializer.Serialize(new { hint, detailedSolution = detail,
        observations = Array.Empty<string>(), uncertainty = "", replyOptions = Array.Empty<object>(), searchQueries = Array.Empty<string>() });
    private static (int Start, int End) Range(string json, string part, int from = 0)
    {
        var offset = json.IndexOf(part, from, StringComparison.Ordinal);
        Assert.IsTrue(offset >= 0, "Fixture span is missing: " + part);
        var start = Encoding.UTF8.GetByteCount(json.AsSpan(0, offset));
        return (start, start + Encoding.UTF8.GetByteCount(part));
    }
    private static object Annotation((int Start, int End) span, int source) => new { type = "url_citation", url = "https://example.com/" + source,
        title = "Source " + source, start_index = span.Start, end_index = span.End };
    private static byte[] Response(string json, object[] annotations) => JsonSerializer.SerializeToUtf8Bytes(new { status = "completed", steps = new object[] {
        new { type = "google_search_result", result = new[] { new { search_suggestions = "<div>Fixture suggestions</div>" } } },
        new { type = "model_output", content = new[] { new { type = "text", text = json, annotations } } } } });
    private static GameHelpAnswer Parse(string json, object[] annotations) => GeminiGameHelpClient.ParseResponse(Response(json, annotations), true);
    private static string Slice(GameHelpAnswer answer, GameHelpCitation citation) => GameHelpCitations.FieldText(answer, citation.Field)![citation.Start..citation.End];
}
