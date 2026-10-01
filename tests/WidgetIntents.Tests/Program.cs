using System.Text;
using System.Text.Json;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetBridge;
using WidgetRail.WidgetSdk;

var tests = new (string Name, Action Run)[]
{
    ("Custom contracts require no host intent enumeration", CustomContract),
    ("Schema object and set ordering do not change identity", SchemaIdentity),
    ("Malformed unsupported and unbounded schemas fail closed", InvalidSchemas),
    ("Payload types bounds duplicate keys and extra properties are enforced", Payloads),
    ("Nested arrays objects booleans and numeric bounds are enforced", NestedPayloads),
    ("Numeric precision cannot round invalid fractions into accepted values", ExactNumbers),
    ("Contract and payload resources have explicit limits", Limits),
    ("Compiled schema outlives the source document", OwnedSchema),
    ("Absent intent declarations preserve legacy manifest serialization", LegacyManifest),
    ("Manifest declarations round trip without granting capabilities", ManifestRoundTrip),
    ("Duplicate conflicting reserved and malformed declarations fail closed", BadDeclarations),
    ("Handler choice is independent of enumeration order", HandlerSelection),
    ("Disabled and different-version handlers cannot claim delivery", HandlerEligibility),
    ("Conflicting handler schemas fail without browser fallback", HandlerConflict),
    ("Only the standard web contract gets external browser fallback", Fallback),
    ("Web routing rejects executable schemes credentials and malformed URLs", WebUrls),
    ("Handler catalogs are bounded and generation bearing", CatalogBounds),
    ("Displayed intent authority survives unrelated snapshots but rejects changed payloads", DisplayedIntentAuthority),
    ("Disabled busy and out-of-scope intent actions cannot be admitted", RejectedIntentAuthority),
};
var failures = 0;
foreach (var (name, run) in tests)
{
    try { run(); Console.WriteLine($"PASS {name}"); }
    catch (Exception error) { failures++; Console.Error.WriteLine($"FAIL {name}: {error}"); }
}
Console.WriteLine($"{tests.Length - failures}/{tests.Length} tests passed.");
return failures == 0 ? 0 : 1;

static void CustomContract()
{
    var contract = Custom();
    Check(contract.Id == "example.guide.explain" && contract.Version == 1);
    Check(contract.Accepts(Json("""{"topic":"boss"}""")));
    foreach (var id in new[] { "plain", ".bad", "bad.", "UPPER.id", "bad..id", "bad/id", "bad.1id", "bad.hello world" })
        Throws(() => CompiledWidgetIntentContract.Create(new(id, 1, WidgetIntentContracts.Web.PayloadSchema)));
    Throws(() => CompiledWidgetIntentContract.Create(WidgetIntentContracts.Web with { Version = 0 }));
}

static void SchemaIdentity()
{
    var a = Json("""{"type":"object","properties":{"a":{"type":"string","maxLength":10,"enum":["a","b"]},"b":{"type":"boolean"}},"required":["a","b"],"additionalProperties":false}""");
    var b = Json("""{"additionalProperties":false,"required":["b","a"],"properties":{"b":{"type":"boolean"},"a":{"enum":["b","a"],"maxLength":10,"type":"string"}},"type":"object"}""");
    Check(Compile(a).SchemaDigest == Compile(b).SchemaDigest);
    Check(Compile(a).SchemaDigest != Compile(Json(a.GetRawText().Replace("10", "11"))).SchemaDigest);
}

static void InvalidSchemas()
{
    foreach (var schema in new[]
    {
        "{}", "null", "[]", """{"type":"string","maxLength":4}""",
        """{"type":"object","type":"object","properties":{},"additionalProperties":false}""",
        """{"type":"object","properties":{},"additionalProperties":true}""",
        """{"type":"object","properties":{},"additionalProperties":false,"$ref":"https://example.com/schema"}""",
        """{"type":"object","properties":{},"additionalProperties":false,"required":["missing"]}""",
        """{"type":"object","properties":{"x":{"type":"string"}},"additionalProperties":false}""",
        """{"type":"object","properties":{"x":{"type":"string","maxLength":8,"pattern":".*"}},"additionalProperties":false}""",
        """{"type":"object","properties":{"x":{"type":"array","maxItems":8}},"additionalProperties":false}""",
        """{"type":"object","properties":{"x":{"type":"number","minimum":0,"maximum":1e100}},"additionalProperties":false}""",
        """{"type":"object","properties":{"x":{"type":"boolean"},"x":{"type":"boolean"}},"additionalProperties":false}""",
        """{"type":"object","properties":{"x":{"type":"string","maxLength":4,"enum":["a","a"]}},"additionalProperties":false}""",
        """{"type":"object","properties":{"x":{"type":"integer","minimum":0.5,"maximum":2}},"additionalProperties":false}""",
    }) Throws(() => Compile(Json(schema)));
}

static void Payloads()
{
    var contract = Custom();
    foreach (var payload in new[] { "null", "[]", "{}", """{"topic":null}""", """{"topic":7}""",
        """{"topic":"boss","extra":1}""", """{"topic":"boss","topic":"map"}""", """{"topic":""}""",
        """{"topic":"more than twenty characters"}""" }) Check(!contract.Accepts(Json(payload)));
    Check(!contract.Accepts(default));
    Check(contract.Accepts(Json("""{"topic":"😀"}""")));
}

static void NestedPayloads()
{
    var contract = Compile(Json("""
        {"type":"object","additionalProperties":false,"properties":{
          "choices":{"type":"array","minItems":1,"maxItems":2,"items":{
            "type":"object","additionalProperties":false,"properties":{
              "label":{"type":"string","minLength":1,"maxLength":3},
              "rank":{"type":"integer","minimum":0,"maximum":5},
              "value":{"type":"number","minimum":-1,"maximum":1},
              "enabled":{"type":"boolean"}},"required":["label","rank","enabled"]}}
        },"required":["choices"]}
        """));
    Check(contract.Accepts(Json("""{"choices":[{"label":"😀abc" ,"rank":1,"enabled":true}]}""")) == false);
    Check(contract.Accepts(Json("""{"choices":[{"label":"😀ab" ,"rank":1,"enabled":true,"value":0.5}]}""")));
    foreach (var payload in new[] { """{"choices":[]}""", """{"choices":[{"label":"x","rank":1.5,"enabled":true}]}""",
        """{"choices":[{"label":"x","rank":6,"enabled":true}]}""", """{"choices":[{"label":"x","rank":1,"enabled":"true"}]}""",
        """{"choices":[{"label":"x","rank":1,"enabled":false,"value":2}]}""" }) Check(!contract.Accepts(Json(payload)));
}

static void Limits()
{
    var nested = """{"type":"boolean"}""";
    for (var i = 0; i < 8; i++) nested = "{\"type\":\"object\",\"additionalProperties\":false,\"properties\":{\"x\":" + nested + "}}";
    Throws(() => Compile(Json(nested)));
    Throws(() => Compile(Json("{\"type\":\"object\",\"additionalProperties\":false,\"properties\":{}," + new string(' ', CompiledWidgetIntentContract.MaximumSchemaBytes) + "\"required\":[]}")));
    var contract = Custom();
    Check(!contract.Accepts(Json("{\"topic\":\"boss\"" + new string(' ', CompiledWidgetIntentContract.MaximumPayloadBytes) + "}")));
    var properties = string.Join(',', Enumerable.Range(0, 33).Select(i => $"\"p{i}\":{{\"type\":\"boolean\"}}"));
    Throws(() => Compile(Json("{\"type\":\"object\",\"additionalProperties\":false,\"properties\":{" + properties + "}}")));
}

static void ExactNumbers()
{
    var contract = Compile(Json("""{"type":"object","additionalProperties":false,"properties":{"n":{"type":"integer","minimum":0,"maximum":1}},"required":["n"]}"""));
    foreach (var number in new[] { "1e-100", "-1e-100", "1.00000000000000000000000000001", "0.99999999999999999999999999999", "1e100" })
        Check(!contract.Accepts(Json("{\"n\":" + number + "}")));
    foreach (var number in new[] { "1.0", "1e0", "10e-1", "0", "-0", "0.000" })
        Check(contract.Accepts(Json("{\"n\":" + number + "}")));
}

static void OwnedSchema()
{
    CompiledWidgetIntentContract compiled;
    using (var document = JsonDocument.Parse(WidgetIntentContracts.Web.PayloadSchema.GetRawText()))
        compiled = Compile(document.RootElement);
    Check(compiled.Accepts(Json("""{"url":"https://example.com"}""")));
}

static void LegacyManifest()
{
    var bytes = ManifestJson.Serialize(Manifest());
    Check(!Encoding.UTF8.GetString(bytes).Contains("\"intents\"", StringComparison.Ordinal));
    var legacy = ManifestJson.Deserialize(bytes);
    Check(legacy.Intents is null && WidgetManifestValidator.Validate(legacy).Count == 0);
}

static void ManifestRoundTrip()
{
    var original = Manifest() with { Intents = new() { Requests = [WidgetIntentContracts.Web], Handles = [WidgetIntentContracts.Video] } };
    var restored = ManifestJson.Deserialize(ManifestJson.Serialize(original));
    Check(restored.Intents!.Requests.Single().Id == WidgetIntentContracts.OpenWebPage);
    Check(restored.Intents.Handles.Single().Id == WidgetIntentContracts.OpenVideo);
    Check(restored.Permissions.Count == 0 && restored.OptionalPermissions.Count == 0);
    Check(WidgetManifestValidator.Validate(restored).Count == 0);
    var fullTrust = restored with { Entrypoint = new(WidgetEntrypointRuntimes.FullTrustApplicationV1, Executable: "payload/app.exe") };
    Check(WidgetManifestValidator.Validate(fullTrust).Count == 0);
}

static void BadDeclarations()
{
    CheckErrors(new() { Requests = [WidgetIntentContracts.Web, WidgetIntentContracts.Web] }, "duplicate_intent");
    CheckErrors(new() { Requests = [WidgetIntentContracts.Web], Handles = [WidgetIntentContracts.Web with { PayloadSchema = CustomSchema() }] }, "conflicting_intent");
    CheckErrors(new() { Requests = [WidgetIntentContracts.Web with { Id = "widgetrail.unknown" }] }, "reserved_intent");
    CheckErrors(new() { Requests = [WidgetIntentContracts.Web with { Version = 2 }] }, "reserved_intent");
    CheckErrors(new() { Requests = null! }, "required");
    CheckErrors(new() { Handles = [null!] }, "invalid_intent");
    CheckErrors(new() { Requests = Enumerable.Repeat(WidgetIntentContracts.Web, 17).ToArray() }, "too_many_intents");
    var json = Encoding.UTF8.GetString(ManifestJson.Serialize(Manifest() with { Intents = new() }));
    ThrowsJson(() => ManifestJson.Deserialize(Encoding.UTF8.GetBytes(json.Replace("\"handles\": []", "\"handles\": [], \"launchProcess\": true"))));
}

static void HandlerSelection()
{
    var a = Candidate("example.alpha"); var b = Candidate("example.beta");
    var one = Resolve([b, a]); var two = Resolve([a, b]);
    Check(one.Kind == IntentResolutionKind.ChooseHandler && two.Kind == one.Kind);
    Check(one.Candidates.Select(c => c.WidgetId).SequenceEqual(two.Candidates.Select(c => c.WidgetId)));
    Check(Resolve([a, b], "example.beta").Candidates.Single() == b);
    Check(Resolve([a]).Kind == IntentResolutionKind.Widget);
    Check(Resolve([a], "example.missing").Candidates.Single() == a);
}

static void HandlerEligibility()
{
    Check(Resolve([Candidate("example.disabled") with { Enabled = false }]).Kind == IntentResolutionKind.ExternalBrowser);
    var future = CompiledWidgetIntentContract.Create(WidgetIntentContracts.Web with { Version = 2 });
    Check(Resolve([Candidate("example.future") with { Contract = future }]).Kind == IntentResolutionKind.ExternalBrowser);
}

static void HandlerConflict()
{
    var conflict = Candidate("example.conflict") with { Contract = CompiledWidgetIntentContract.Create(WidgetIntentContracts.Web with { PayloadSchema = CustomSchema() }) };
    Check(Resolve([Candidate("example.good"), conflict], "example.good").Kind == IntentResolutionKind.SchemaConflict);
    Check(Resolve([conflict]).Kind == IntentResolutionKind.SchemaConflict);
    Check(Resolve([conflict with { Enabled = false }]).Kind == IntentResolutionKind.ExternalBrowser);
}

static void Fallback()
{
    Check(Resolve([]).Kind == IntentResolutionKind.ExternalBrowser);
    Check(IntentResolutionPolicy.Resolve(Custom(), Json("""{"topic":"boss"}"""), []).Kind == IntentResolutionKind.Unavailable);
    Check(IntentResolutionPolicy.Resolve(CompiledWidgetIntentContract.Create(WidgetIntentContracts.Video),
        Json("""{"provider":"youtube","videoId":"abcdefghijk"}"""), []).Kind == IntentResolutionKind.Unavailable);
    Check(IntentResolutionPolicy.Resolve(CompiledWidgetIntentContract.Create(WidgetIntentContracts.Web with { Version = 2 }),
        Json("""{"url":"https://example.com"}"""), []).Kind == IntentResolutionKind.Unavailable);
}

static void WebUrls()
{
    foreach (var url in new[] { "file:///C:/test.exe", "javascript:alert(1)", "ms-settings:", "https://user:pass@example.com", "not a url", " https://example.com", "https://example.com\n" })
        Check(IntentResolutionPolicy.Resolve(CompiledWidgetIntentContract.Create(WidgetIntentContracts.Web),
            JsonSerializer.SerializeToElement(new { url }), []).Kind == IntentResolutionKind.InvalidPayload);
    Check(IntentResolutionPolicy.Resolve(CompiledWidgetIntentContract.Create(WidgetIntentContracts.Web),
        Json("""{"url":"http://example.com/guide"}"""), []).Kind == IntentResolutionKind.ExternalBrowser);
}

static void CatalogBounds()
{
    var candidate = Candidate("example.browser");
    Check(Resolve([candidate, candidate]).Kind == IntentResolutionKind.InvalidCatalog);
    Check(Resolve([candidate with { Generation = 0 }]).Kind == IntentResolutionKind.InvalidCatalog);
    Check(Resolve(Enumerable.Repeat(candidate, IntentResolutionPolicy.MaximumCandidates + 1).ToArray()).Kind == IntentResolutionKind.InvalidCatalog);
    Check(Resolve([null!]).Kind == IntentResolutionKind.InvalidCatalog);
    Check(Resolve([candidate]).Candidates.Single().Generation == 42);
    Check(Resolve([Candidate("browser")]).Kind == IntentResolutionKind.Widget);
}

static void DisplayedIntentAuthority()
{
    var snapshot = IntentSnapshot();
    var action = IntentAction();
    var latest = snapshot with { Sequence = 5, Root = snapshot.Root with { Children =
        [snapshot.Root.Children[0], new ViewNode { Id = "status", Kind = ViewNodeKind.Text, Text = "Updated" }] } };
    Check(IntentActionAuthority.Revalidate(snapshot, latest, action) is not null);
    var node = snapshot.Root.Children[0];
    latest = snapshot with { Sequence = 2, Root = snapshot.Root with { Children = [node with { Intent = node.Intent! with
        { Payload = Json("""{"url":"https://changed.example"}""") } }] } };
    Check(IntentActionAuthority.Revalidate(snapshot, latest, action) is null);
    Check(IntentActionAuthority.Revalidate(snapshot, snapshot with { WidgetInstanceId = "restarted" }, action) is null);
    Check(IntentActionAuthority.Revalidate(snapshot, snapshot, action with { Phase = ControllerEventPhase.Released }) is null);
    Check(IntentActionAuthority.Revalidate(snapshot, snapshot, action with { Phase = ControllerEventPhase.Repeated }) is null);
    Check(IntentActionAuthority.Revalidate(snapshot, snapshot, action with { ControllerButton = ControllerButton.B }) is null);
    var row = node with { CollectionItemKey = "old-row" };
    var old = snapshot with { Root = snapshot.Root with { Children = [row] } };
    var changed = snapshot with { Root = snapshot.Root with { Children = [row with { CollectionItemKey = "new-row" }] } };
    Check(IntentActionAuthority.Revalidate(old, changed, action) is null);
}

static void RejectedIntentAuthority()
{
    var snapshot = IntentSnapshot(); var action = IntentAction(); var node = snapshot.Root.Children[0];
    foreach (var blocked in new[] { node with { IsDisabled = true }, node with { IsBusy = true }, node with { ActionId = "other" },
        node with { Intent = null }, node with { Kind = ViewNodeKind.Text } })
        Check(IntentActionAuthority.Revalidate(snapshot, snapshot with { Root = snapshot.Root with { Children = [blocked] } }, action) is null);
    Check(IntentActionAuthority.Revalidate(snapshot, snapshot, action with { InputScopeId = "other" }) is null);
    Check(IntentActionAuthority.Revalidate(snapshot, snapshot, action with { RequestedValue = 1 }) is null);
    Check(IntentActionAuthority.Revalidate(snapshot, snapshot, action with { CommittedText = "command" }) is null);
    var nested = snapshot with { Root = snapshot.Root with { Children = [new ViewNode
        { Id = "modal", Kind = ViewNodeKind.Stack, InputScopeId = "modal", Children = [node] }] } };
    Check(IntentActionAuthority.Revalidate(nested, nested, action) is null);
}

static ViewSnapshot IntentSnapshot() => new WidgetView(UI.Stack("root", UI.Button("Guide", "open", "guide")
    .OpenIntent(WidgetIntentContracts.Web, Json("""{"url":"https://example.com"}""")))).CreateSnapshot("intent.widget", 1);
static WidgetActionEvent IntentAction() => new("open", "guide", ControllerButton.A, InputScopeId: "root");

static IntentHandlerCandidate Candidate(string id) => new(id, 42, CompiledWidgetIntentContract.Create(WidgetIntentContracts.Web), true);
static IntentResolution Resolve(IReadOnlyList<IntentHandlerCandidate> candidates, string? preferred = null) =>
    IntentResolutionPolicy.Resolve(CompiledWidgetIntentContract.Create(WidgetIntentContracts.Web), Json("""{"url":"https://example.com/guide"}"""), candidates, preferred);
static void CheckErrors(WidgetIntentDeclarations declarations, string code) =>
    Check(WidgetManifestValidator.Validate(Manifest() with { Intents = declarations }).Any(e => e.Code == code));
static WidgetManifest Manifest() => new()
{
    Id = "example.game-help", Publisher = "example.publisher", Name = "Game Help", Version = "0.1.0", HostApi = new("1.0.0", 1),
    Entrypoint = new("dotnet-worker", "payload/Example.dll", "Example.Widget"),
};
static JsonElement CustomSchema() => Json("""{"type":"object","additionalProperties":false,"properties":{"topic":{"type":"string","minLength":1,"maxLength":20}},"required":["topic"]}""");
static CompiledWidgetIntentContract Custom() => Compile(CustomSchema());
static CompiledWidgetIntentContract Compile(JsonElement schema) => CompiledWidgetIntentContract.Create(new("example.guide.explain", 1, schema));
static JsonElement Json(string json) { using var doc = JsonDocument.Parse(json); return doc.RootElement.Clone(); }
static void Check(bool condition, [System.Runtime.CompilerServices.CallerArgumentExpression(nameof(condition))] string? expression = null)
{ if (!condition) throw new Exception("Assertion failed: " + expression); }
static void Throws(Action action) { try { action(); } catch (ArgumentException) { return; } throw new Exception("Expected ArgumentException."); }
static void ThrowsJson(Action action) { try { action(); } catch (JsonException) { return; } throw new Exception("Expected JsonException."); }
