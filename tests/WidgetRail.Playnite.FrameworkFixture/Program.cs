using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.IO;
using System.Linq;
using System.Runtime.Remoting.Messaging;
using System.Runtime.Remoting.Proxies;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Threading;
using Playnite.SDK;
using Playnite.SDK.Models;
using Playnite.SDK.Plugins;
using WidgetRail.Playnite;

internal static class Program
{
    private static int result, checks;
    [STAThread]
    private static int Main(string[] args)
    {
        if (args.SequenceEqual(new[] { "--capture-session-child" })) { Console.ReadLine(); return 0; }
        var ui = Dispatcher.CurrentDispatcher;
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(ui));
        ui.BeginInvoke(new Action(async () =>
        {
            try { await RunAsync(ui); Console.WriteLine("PASS " + checks + " Framework/API checks"); }
            catch (Exception error) { Console.Error.WriteLine(error); result = 1; }
            finally { ui.BeginInvokeShutdown(DispatcherPriority.Background); }
        }));
        Dispatcher.Run(); return result;
    }
    private static void Check(bool condition, string name)
    { if (!condition) throw new InvalidOperationException(name); checks++; Console.WriteLine("PASS " + name); }
    private static Dictionary<string, object> Json(LocalApiResponse response) =>
        new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(Encoding.UTF8.GetString(response.Body));
    private static LocalApiRequest Request(string method, string path, string body = "", string? command = null, NameValueCollection? query = null) =>
        new LocalApiRequest(method, path, query ?? new NameValueCollection(), Encoding.UTF8.GetBytes(body), command);
    private static async Task RunAsync(Dispatcher ui)
    {
        var root = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "widgetrail-playnite-framework", Guid.NewGuid().ToString("N")));
        Directory.CreateDirectory(root);
        try
        {
            CaptureSettingsChecks.Run(Check, root);
            await CaptureCompanionChecks.RunAsync(Check, root);
            var playerRoot = Path.Combine(root, "Game");
            Directory.CreateDirectory(Path.Combine(playerRoot,"Player_Data"));
            File.WriteAllBytes(Path.Combine(playerRoot,"UnityPlayer.dll"),Array.Empty<byte>());
            var player = Path.Combine(playerRoot,"Player.exe");
            var helper = Path.Combine(playerRoot,"UnityCrashHandler64.exe");
            Check(CaptureLaunchProcess.IsPlayer(playerRoot,helper,player), "A colocated Unity player has exact game installation evidence");
            Check(!CaptureLaunchProcess.IsPlayer(playerRoot + "2",helper,player) &&
                !CaptureLaunchProcess.IsPlayer(playerRoot,helper,Path.Combine(playerRoot,"Launcher.exe")) &&
                !CaptureLaunchProcess.IsPlayer(playerRoot,Path.Combine(root,"UnityCrashHandler64.exe"),player),
                "Sibling roots, unrelated launchers and foreign helper paths cannot claim the capture");
            var game = new Game("Fixture A") { IsInstalled = true, Description = "Details", Playtime = 42 };
            var second = new Game("Fixture B") { IsInstalled = false, Hidden = true };
            var games = new List<Game> { game, second };
            var updates = 0; var launches = 0; var ownerThread = Thread.CurrentThread.ManagedThreadId;
            var gameCollection = Proxy.For(typeof(IItemCollection<Game>), (name, args) =>
            {
                if (name == "Get") return games.FirstOrDefault(item => item.Id == (Guid)args[0]);
                if (name == "GetEnumerator") return games.GetEnumerator();
                if (name == "get_Count") return games.Count;
                if (name == "Update") { Check(Thread.CurrentThread.ManagedThreadId == ownerThread, "Mutation runs on Playnite owner thread"); updates++; }
                return null;
            });
            var categories = new List<Category>(); var statuses = new List<CompletionStatus> { new CompletionStatus("Completed") };
            var categoryCollection = Collection(categories); var statusCollection = Collection(statuses);
            var dbType = typeof(IPlayniteAPI).GetProperty("Database")!.PropertyType;
            var database = Proxy.For(dbType, (name, args) => name == "get_Games" ? gameCollection : name == "get_Categories" ? categoryCollection :
                name == "get_CompletionStatuses" ? statusCollection : name == "GetFullFilePath" ? args[0] : name == "BufferedUpdate" ? new Empty() : null);
            var pathsType = typeof(IPlayniteAPI).GetProperty("Paths")!.PropertyType;
            var paths = Proxy.For(pathsType, (name, _) => root);
            var mainType = typeof(IPlayniteAPI).GetProperty("MainView")!.PropertyType;
            var main = Proxy.For(mainType, (name, _) => name == "get_UIDispatcher" ? ui : null);
            var plugins = new List<Plugin>();
            var addonsType = typeof(IPlayniteAPI).GetProperty("Addons")!.PropertyType;
            var addons = Proxy.For(addonsType, (name, _) => name == "get_Plugins" ? plugins : null);
            var api = (IPlayniteAPI)Proxy.For(typeof(IPlayniteAPI), (name, args) =>
            {
                if (name == "get_Database") return database;
                if (name == "get_Paths") return paths;
                if (name == "get_MainView") return main;
                if (name == "get_Addons") return addons;
                if (name == "StartGame") { Check((Guid)args[0] == game.Id && Thread.CurrentThread.ManagedThreadId == ownerThread, "Launch admitted on owner thread"); launches++; }
                return null;
            });
            using (var dispatcher = new PlayniteApiDispatcher(api, ConnectionDescriptor.Create(Guid.NewGuid().ToString("D"), 19822)))
            {
                var connection = await dispatcher.DispatchAsync(Request("GET", "/v1/connection"), default);
                Check(connection.Status == 200 && (string)Json(connection)["pluginId"] == IntegrationIdentity.PluginId, "Typed handshake works without optional providers");
                dispatcher.CaptureGameStarting(game.Id);
                using (var child = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(
                    System.Reflection.Assembly.GetExecutingAssembly().Location, "--capture-session-child") {
                        UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true }))
                {
                    try
                    {
                        dispatcher.CaptureGameStarted(game.Id, child.Id);
                        var request = Request("GET","/v1/capture-integration",query: new NameValueCollection { ["gameId"] = game.Id.ToString("D") });
                        var history = Json(await dispatcher.DispatchAsync(request,default));
                        Check((string)history["sessionGameId"] == game.Id.ToString("D") &&
                            ((System.Collections.IEnumerable)history["sessions"]).Cast<object>().Count() == 1,
                            "The Playnite add-on identifies a real newly launched process without WidgetRail or an achievement provider");
                        dispatcher.CaptureGameStopped(game.Id);
                        var afterStop = Json(await dispatcher.DispatchAsync(request,default));
                        Check(((System.Collections.IEnumerable)afterStop["sessions"]).Cast<object>().Count() == 1,
                            "The stopped game's source interval is available to a later overlay connection");
                    }
                    finally { child.StandardInput.Close(); child.WaitForExit(5000); }
                }
                var captureSettings = await dispatcher.DispatchAsync(Request("GET", "/v1/capture-settings"), default);
                Check(captureSettings.Status == 200 && (string)Json(captureSettings)["availability"] == "missing", "Capture settings route reports unavailable addon without breaking library");
                Check((await dispatcher.DispatchAsync(Request("PATCH", "/v1/capture-settings", "{}"), default)).Status == 400,
                    "Capture settings mutation requires existing command identity");
                var page = await dispatcher.DispatchAsync(Request("GET", "/v1/games", query: new NameValueCollection { ["limit"] = "2" }), default);
                Check(page.Status == 200 && Convert.ToInt32(Json(page)["total"]) == 1, "Library excludes hidden games by default");
                var revision = (string)Json(page)["revision"];
                dispatcher.LibraryChanged();
                var oldPage = await dispatcher.DispatchAsync(Request("GET", "/v1/games", query: new NameValueCollection { ["revision"] = revision, ["limit"] = "2" }), default);
                Check((string)Json(oldPage)["revision"] == revision, "An admitted snapshot stays stable across invalidation");
                var detail = await dispatcher.DispatchAsync(Request("GET", "/v1/games/" + game.Id.ToString("D")), default);
                Check(detail.Status == 200 && (string)Json(detail)["description"] == "Details", "Typed details preserve metadata");
                var artwork = await dispatcher.DispatchAsync(Request("POST", "/v1/artwork/resolve", "{\"gameIds\":[\"" + game.Id + "\"],\"kinds\":[\"cover\"]}"), default);
                Check(artwork.Status == 200, "Typed batch artwork accepts native Framework JSON arrays without eval");
                var id = Guid.NewGuid().ToString("D");
                var mutation = Request("PATCH", "/v1/games/" + game.Id, "{\"favorite\":true}", id);
                Check((await dispatcher.DispatchAsync(mutation, default)).Status == 200 && game.Favorite, "Favorite mutation is applied");
                await dispatcher.DispatchAsync(mutation, default);
                Check(updates == 1, "Mutation replay does not apply twice");
                Check((await dispatcher.DispatchAsync(Request("PATCH", mutation.Path, "{\"favorite\":false}", id), default)).Status == 409,
                    "Command identity cannot authorize different mutation content");
                Check((await dispatcher.DispatchAsync(Request("PATCH", mutation.Path, "{\"name\":\"changed\"}", Guid.NewGuid().ToString("D")), default)).Status == 400 && game.Name == "Fixture A",
                    "Unrelated game fields cannot be written");
                Check((await dispatcher.DispatchAsync(Request("POST", "/api/eval", "{\"code\":\"1\"}"), default)).Status == 404, "No compatibility eval endpoint exists");
                var launch = Request("POST", "/v1/games/" + game.Id + "/launch", command: Guid.NewGuid().ToString("D"));
                await dispatcher.DispatchAsync(launch, default); await dispatcher.DispatchAsync(launch, default);
                Check(launches == 1, "Launch replay preserves one external action");
                var endedReturn = Request("POST", "/v1/games/" + game.Id + "/return", command: Guid.NewGuid().ToString("D"));
                Check((await dispatcher.DispatchAsync(endedReturn, default)).Status == 404 && launches == 1,
                    "Returning to a game that closed never starts a replacement process");
                dispatcher.CaptureGameStarting(second.Id);
                dispatcher.CaptureGameStarted(second.Id, 0);
                var running = Request("GET", "/v1/events", query: new NameValueCollection { ["after"] = "now", ["wait"] = "0" });
                Check(((System.Collections.IEnumerable)Json(await dispatcher.DispatchAsync(running, default))["runningGames"])
                    .Cast<string>().Single() == second.Id.ToString("D"), "Opening after a launch recovers the running game");
                Check(((Dictionary<string, object>)Json(await dispatcher.DispatchAsync(running, default))["runningProcesses"]).Count == 0,
                    "A running game with no process evidence cannot reveal the foreground");
                var guarded = Request("POST", launch.Path, command: Guid.NewGuid().ToString("D"),
                    query: new NameValueCollection { ["running"] = "check" });
                var conflict = Json(await dispatcher.DispatchAsync(guarded, default));
                Check((string)conflict["name"] == second.Name && launches == 1, "A second game requires confirmation before StartGame");
                var approved = Request("POST", launch.Path, command: Guid.NewGuid().ToString("D"),
                    query: new NameValueCollection { ["running"] = (string)conflict["conflict"] });
                await dispatcher.DispatchAsync(approved, default);
                await dispatcher.DispatchAsync(approved, default);
                Check(launches == 2, "Launch anyway starts the game exactly once");
                dispatcher.CaptureGameStopped(second.Id);
                dispatcher.CaptureGameStarting(second.Id);
                using (var process = System.Diagnostics.Process.GetCurrentProcess())
                    dispatcher.CaptureGameStarted(second.Id, process.Id);
                var evidence = (Dictionary<string, object>)Json(await dispatcher.DispatchAsync(running, default))["runningProcesses"];
                Check(evidence.TryGetValue(second.Id.ToString("D"), out var key) && key is string instance && instance.Length == 64,
                    "The running snapshot carries a process-instance key scoped to the launched game");
                dispatcher.CaptureGameStopped(second.Id);
                Check(!((System.Collections.IEnumerable)Json(await dispatcher.DispatchAsync(running, default))["runningGames"])
                    .Cast<object>().Any(), "Reopening after exit recovers an empty running set");
                var activity = await dispatcher.DispatchAsync(Request("GET", "/v1/games/" + game.Id + "/activity"), default);
                Check(activity.Status == 200 && !(bool)Json(activity)["installed"], "Absent GameActivity is unavailable without breaking library reads");
                var withoutActivity = await dispatcher.DispatchAsync(Request("GET", "/v1/activity/recent",
                    query: new NameValueCollection { ["ids"] = game.Id.ToString("D") }), default);
                Check(withoutActivity.Status == 200 && !((System.Collections.IEnumerable)Json(withoutActivity)["sessions"]).Cast<object>().Any(),
                    "Home can read an empty activity feed without the GameActivity plugin or its files");
                var captureEvidence = await dispatcher.DispatchAsync(Request("GET", "/v1/capture-integration",
                    query: new NameValueCollection { ["gameId"] = game.Id.ToString("D") }), default);
                Check(captureEvidence.Status == 200 && (string)Json(captureEvidence)["sessionGameId"] == game.Id.ToString("D"),
                    "Home can request independent capture session evidence without GameActivity");
                var overview = await dispatcher.DispatchAsync(Request("GET", "/v1/achievements/overview"), default);
                Check(overview.Status == 200 && (string)Json(overview)["availability"] == "missing" && Convert.ToInt32(Json(overview)["totalGames"]) == 0,
                    "Achievement destination can distinguish a missing addon without failing the library");
                var provider = new FakeAchievements(api); plugins.Add(provider);
                overview = await dispatcher.DispatchAsync(Request("GET", "/v1/achievements/overview", query: new NameValueCollection { ["limit"] = "1" }), default);
                Check(overview.Status == 200 && (string)Json(overview)["availability"] == "ready" && Convert.ToInt32(Json(overview)["totalGames"]) == 1 &&
                    Convert.ToInt32(Json(overview)["unlockedAchievements"]) == 1,
                    "Overview deduplicates cached identities and excludes hidden Playnite games");
                var overviewRevision = (string)Json(overview)["revision"];
                overview = await dispatcher.DispatchAsync(Request("GET", "/v1/achievements/overview"), default);
                Check((string)Json(overview)["revision"] == overviewRevision && provider.AchievementDataService.OverviewReads == 1 && provider.Refreshes == 0,
                    "Repeated overview reads reuse summaries without full detail hydration or provider refresh");
                Check((await dispatcher.DispatchAsync(Request("GET", "/v1/achievements/overview", query: new NameValueCollection { ["offset"] = "1" }), default)).Status == 400,
                    "Continuation pages require an exact overview snapshot");
                provider.Invalidate();
                Check((await dispatcher.DispatchAsync(Request("GET", "/v1/achievements/overview", query: new NameValueCollection { ["revision"] = overviewRevision }), default)).Status == 409,
                    "Provider invalidation rejects stale overview pages");
                second.Hidden = false; dispatcher.LibraryChanged();
                overview = await dispatcher.DispatchAsync(Request("GET", "/v1/achievements/overview", query: new NameValueCollection { ["limit"] = "1" }), default);
                overviewRevision = (string)Json(overview)["revision"];
                Check(Convert.ToInt32(Json(overview)["totalGames"]) == 2 && Convert.ToInt32(Json(overview)["nextOffset"]) == 1,
                    "Overview emits bounded pages with aggregate counts over the same snapshot");
                overview = await dispatcher.DispatchAsync(Request("GET", "/v1/achievements/overview", query: new NameValueCollection { ["offset"] = "1", ["limit"] = "1", ["revision"] = overviewRevision }), default);
                Check(overview.Status == 200 && Json(overview)["nextOffset"] == null &&
                    ((System.Collections.IEnumerable)Json(overview)["recent"]).Cast<object>().Count() == 0,
                    "Overview continuation neither duplicates recent unlocks nor changes its snapshot");
                second.Hidden = true; dispatcher.LibraryChanged();
                var activityFolder = Directory.CreateDirectory(Path.Combine(root, "afbb1a0d-04a1-4d0c-9afa-c6e42ca855b4", "GameActivity")).FullName;
                var historyText = new System.Web.Script.Serialization.JavaScriptSerializer().Serialize(new {
                    Items = Enumerable.Range(1, 12).Select(day => new { DateSession = new DateTime(2026, 1, day, 12, 0, 0, DateTimeKind.Utc)
                        .ToString("o"), ElapsedSeconds = 60 * day }).ToArray() });
                File.WriteAllText(Path.Combine(activityFolder, game.Id.ToString("D") + ".json"), historyText);
                File.WriteAllText(Path.Combine(activityFolder, second.Id.ToString("D") + ".json"), historyText);
                var batchRequest = Request("GET", "/v1/activity/recent", query: new NameValueCollection {
                    ["ids"] = game.Id + "," + second.Id + "," + game.Id });
                var recentResponse = Json(await dispatcher.DispatchAsync(batchRequest, default));
                var recentSessions = ((System.Collections.IEnumerable)recentResponse["sessions"]).Cast<Dictionary<string, object>>().ToArray();
                var recentUnlock = ((System.Collections.IEnumerable)recentResponse["unlocks"]).Cast<Dictionary<string, object>>().Single();
                Check(recentSessions.Length == 8 && recentSessions.All(row => (string)row["gameId"] == game.Id.ToString("D")) &&
                    Convert.ToInt64(recentSessions[0]["seconds"]) == 720 && (string)recentUnlock["description"] == "Description" &&
                    (string)recentUnlock["icon"] == @"C:\fixture\badge.png",
                    "Home batches recent sessions and unlocks, selects newest sessions, and excludes duplicate and hidden games");
                var excessive = Request("GET", "/v1/activity/recent", query: new NameValueCollection {
                    ["ids"] = string.Join(",", Enumerable.Repeat(game.Id.ToString("D"), 13)) });
                Check((await dispatcher.DispatchAsync(excessive, default)).Status == 400, "Recent activity enforces its batch limit");
                var overviewReads = provider.AchievementDataService.OverviewReads;
                var extra = Enumerable.Range(0, 500).Select(index => new Game("Overview " + index)).ToArray();
                games.AddRange(extra); provider.AchievementDataService.Games = games.Select(value => value.Id.ToString("D")).ToArray();
                provider.Invalidate(); dispatcher.LibraryChanged();
                var inputRan = false;
                _ = ui.BeginInvoke(DispatcherPriority.Input, new Action(() => inputRan = true));
                overview = await dispatcher.DispatchAsync(Request("GET", "/v1/achievements/overview", query: new NameValueCollection { ["limit"] = "200" }), default);
                Check(overview.Status == 200 && inputRan && Convert.ToInt32(Json(overview)["totalGames"]) == 501 &&
                    ((System.Collections.IEnumerable)Json(overview)["games"]).Cast<object>().Count() == 200 &&
                    provider.AchievementDataService.OverviewReads - overviewReads == 501,
                    "Large overview yields to input and projects cached games once while returning only one bounded page");
                games.RemoveAll(value => extra.Contains(value)); provider.AchievementDataService.Games = games.Select(value => value.Id.ToString("D")).ToArray();
                provider.Invalidate(); dispatcher.LibraryChanged();
                var data = await dispatcher.DispatchAsync(Request("GET", "/v1/games/" + game.Id + "/achievements"), default);
                Check(data.Status == 200 && (string)Json(data)["provider"] == "playnite-achievements" && Convert.ToInt32(Json(data)["total"]) == 1,
                    "Optional public achievement service is projected without runtime compilation");
                await dispatcher.DispatchAsync(Request("GET", "/v1/games/" + game.Id + "/achievements"), default);
                Check(provider.AchievementDataService.Reads == 1, "Repeated selected-game reads reuse a bounded current projection");
                provider.Invalidate();
                await dispatcher.DispatchAsync(Request("GET", "/v1/games/" + game.Id + "/achievements"), default);
                Check(provider.AchievementDataService.Reads == 2, "Provider events invalidate the cached projection");
                var refresh = await dispatcher.DispatchAsync(Request("POST", "/v1/games/" + game.Id + "/achievements/refresh", command: Guid.NewGuid().ToString("D")), default);
                Check(refresh.Status == 202, "Provider refresh has a separate operation identity");
                for (var attempt = 0; provider.Refreshes == 0 && attempt < 20; attempt++) await Task.Delay(10);
                Check(provider.Refreshes == 1, "Provider refresh uses the public method once");
                await Task.Delay(30);
                provider.PendingRefresh = new TaskCompletionSource<bool>();
                try
                {
                    await dispatcher.DispatchAsync(Request("POST", "/v1/games/" + game.Id + "/achievements/refresh", command: Guid.NewGuid().ToString("D")), default);
                    await dispatcher.DispatchAsync(Request("POST", "/v1/games/" + game.Id + "/achievements/refresh", command: Guid.NewGuid().ToString("D")), default);
                    for (var attempt = 0; provider.Refreshes < 2 && attempt < 50; attempt++) await Task.Delay(10);
                    await Task.Delay(30);
                    Check(provider.Refreshes == 2, "Concurrent commands for one game share its in-flight provider refresh");
                }
                finally { provider.PendingRefresh.SetResult(true); provider.PendingRefresh = null; }
                var baseline = await dispatcher.DispatchAsync(Request("GET", "/v1/events", query: new NameValueCollection { ["after"] = "now", ["wait"] = "0" }), default);
                var cursor = Convert.ToInt64(Json(baseline)["nextSequence"]);
                var unlock = new FakeUnlock { PlayniteGameId = game.Id };
                provider.Unlock(unlock); provider.Unlock(unlock);
                foreach (var flag in new[] { "IsPreview", "IsTestFire", "IsFriendUnlock", "IsProgressUpdate", "IsCompletionAchievement", "IsGameCompleted" })
                {
                    var synthetic = new FakeUnlock { PlayniteGameId = game.Id, ApiName = flag };
                    typeof(FakeUnlock).GetProperty(flag)!.SetValue(synthetic, true); provider.Unlock(synthetic);
                }
                var events = await dispatcher.DispatchAsync(Request("GET", "/v1/events", query: new NameValueCollection { ["after"] = cursor.ToString(), ["wait"] = "0" }), default);
                Check(((System.Collections.IEnumerable)Json(events)["events"]).OfType<Dictionary<string, object>>().Count(item => (string)item["kind"] == "achievement-unlocked") == 1,
                    "Unlock feed deduplicates real events and rejects previews, friends, progress and completion notices");
                var fallback = Path.Combine(root, "cebe6d32-8c46-4459-b993-5a5189d60788", "SuccessStory");
                Directory.CreateDirectory(fallback);
                File.WriteAllText(Path.Combine(fallback, game.Id.ToString("D") + ".json"),
                    "{\"Items\":[{\"Name\":\"Fallback\",\"DateUnlocked\":\"2026-01-01T00:00:00Z\",\"Percent\":12.5}]}");
                provider.AchievementDataService.Empty = true; provider.Invalidate();
                data = await dispatcher.DispatchAsync(Request("GET", "/v1/games/" + game.Id + "/achievements"), default);
                Check(Convert.ToInt32(Json(data)["total"]) == 0 && (string)Json(data)["provider"] == "playnite-achievements",
                    "Valid empty provider data does not resurrect older fallback achievements");
                provider.AchievementDataService.Fail = true; provider.Invalidate();
                data = await dispatcher.DispatchAsync(Request("GET", "/v1/games/" + game.Id + "/achievements"), default);
                Check(Convert.ToInt32(Json(data)["total"]) == 1 && (string)Json(data)["provider"] == "success-story",
                    "Unavailable provider uses bounded SuccessStory fallback in the integration");
                categories.Add(new Category("Duplicate")); categories.Add(new Category("duplicate"));
                var ambiguous = await dispatcher.DispatchAsync(Request("PUT", "/v1/games/" + game.Id + "/categories",
                    "{\"categories\":[\"New category\",\"Duplicate\"]}", Guid.NewGuid().ToString("D")), default);
                Check(ambiguous.Status == 409 && categories.Count == 2,
                    "Ambiguous category identity cannot create a partial edit or choose the first match");
                categories.AddRange(Enumerable.Range(0, 257).Select(index => new Category("Category " + index)));
                Check((await dispatcher.DispatchAsync(Request("GET", "/v1/categories"), default)).Status == 413,
                    "Oversized named catalogs are rejected rather than truncated");
                provider.AchievementDataService.Fail=false;provider.AchievementDataService.Empty=false;
                var captureRoot=Path.Combine(root,"provider-captures");var captureFolder=Path.Combine(captureRoot,ProviderCaptureCompanions.Segment(game.Name,true));
                Directory.CreateDirectory(captureFolder);
                provider.Settings.Persisted.UnlockScreenshotDirectory=captureRoot;
                provider.GameCustomDataStore.Records=new[]{new FakeCustomCapture{PlayniteGameId=second.Id,CaptureOverride=new FakeCaptureOverride{CaptureDirectory=Path.Combine(root,"custom-captures")}}};
                provider.Invalidate();
                var integration=await dispatcher.DispatchAsync(Request("GET","/v1/capture-integration"),default);
                Check(integration.Status==200&&Guid.TryParse((string)Json(integration)["profileId"],out _),"Capture integration exposes a stable profile namespace");
                Check(provider.GameCustomDataStore.Queries>0,"Capture roots use the public read-locked projection API");
                var captureFile=Path.Combine(captureFolder,"001_Fixture achievement_clean.png");File.WriteAllBytes(captureFile,new byte[]{1,2,3});
                var captureBody=new JavaScriptSerializer().Serialize(new{path=captureRoot,enabled=true});
                Check((await dispatcher.DispatchAsync(Request("PUT","/v1/capture-integration",captureBody,Guid.NewGuid().ToString("D")),default)).Status==200,"Explicit provider capture root can enable companions");
                for(var attempt=0;attempt<150&&!File.Exists(captureFile+ProviderCaptureCompanions.Suffix);attempt++)await Task.Delay(20);
                var associated=IntegrationFiles.Read<ProviderCaptureContext>(captureFile+ProviderCaptureCompanions.Suffix);
                Check(associated?.References.Length==2&&associated.References.All(reference=>reference.HasRelatedAction),"Public provider projection produces actionable game and achievement companions");
                Check(associated!.EventAtUnixMilliseconds==null,"Discovered capture does not inherit an unproven historical event timestamp");
                Check(File.GetAttributes(captureFile+ProviderCaptureCompanions.Suffix).HasFlag(FileAttributes.Hidden),"Provider integration publishes hidden companions");
                captureBody=new JavaScriptSerializer().Serialize(new{path=root,enabled=true});
                Check((await dispatcher.DispatchAsync(Request("PUT","/v1/capture-integration",captureBody,Guid.NewGuid().ToString("D")),default)).Status==409,"Unconfigured directories cannot become provider companion roots");
                captureBody=new JavaScriptSerializer().Serialize(new{path=captureRoot,enabled=false});
                Check((await dispatcher.DispatchAsync(Request("PUT","/v1/capture-integration",captureBody,Guid.NewGuid().ToString("D")),default)).Status==200,"Capture root disable is an explicit retained mutation");
                plugins.Remove(provider);
                plugins.Add(new VisibleAchievements(api, game.Id, second.Id));
                var modern = Json(await dispatcher.DispatchAsync(Request("GET", "/v1/achievements/overview"), default));
                Check((string)modern["availability"] == "ready" && Convert.ToInt32(modern["totalGames"]) == 1 &&
                    Convert.ToInt32(modern["totalAchievements"]) == 1 && Convert.ToInt32(modern["unlockedAchievements"]) == 1 &&
                    ((System.Collections.IEnumerable)modern["recent"]).Cast<object>().Count() == 1,
                    "Visible-only provider honors both game and achievement summary exclusions");
            }
        }
        finally
        {
            var parent = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "widgetrail-playnite-framework")) + Path.DirectorySeparatorChar;
            if (!root.StartsWith(parent, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Unexpected fixture root");
            Directory.Delete(root, true);
        }
    }
    private static object Collection<T>(List<T> items) where T : DatabaseObject => Proxy.For(typeof(IItemCollection<T>), (name, args) =>
    {
        if (name == "GetEnumerator") return items.GetEnumerator();
        if (name == "Get") return items.FirstOrDefault(item => item.Id == (Guid)args[0]);
        if (name == "get_Count") return items.Count;
        if (name == "Add") items.Add((T)args[0]);
        return null;
    });
    private sealed class Empty : IDisposable { public void Dispose() { } }
    private sealed class Proxy : RealProxy
    {
        private readonly Func<string, object[], object?> call;
        private Proxy(Type type, Func<string, object[], object?> call) : base(type) { this.call = call; }
        internal static object For(Type type, Func<string, object[], object?> call) => new Proxy(type, call).GetTransparentProxy();
        public override IMessage Invoke(IMessage message)
        {
            var method = (IMethodCallMessage)message;
            try { return new ReturnMessage(call(method.MethodName, method.Args), Array.Empty<object>(), 0, method.LogicalCallContext, method); }
            catch (Exception error) { return new ReturnMessage(error, method); }
        }
    }
    public sealed class FakeAchievements : GenericPlugin
    {
        public override Guid Id => new Guid("e6aad2c9-6e06-4d8d-ac55-ac3b252b5f7b");
        public FakeData AchievementDataService { get; } = new FakeData();
        public FakeCaptureOptions Settings { get; } = new FakeCaptureOptions();
        public FakeCustomCaptureStore GameCustomDataStore { get; } = new FakeCustomCaptureStore();
        public int Refreshes;
        public TaskCompletionSource<bool>? PendingRefresh;
        public static event EventHandler<EventArgs>? SettingsSaved;
        public static event EventHandler<FakeUnlock>? AchievementUnlocked;
        public FakeAchievements(IPlayniteAPI api) : base(api) { AchievementDataService.Games = api.Database.Games.Select(game => game.Id.ToString("D")).ToArray(); }
        public void Invalidate() => SettingsSaved?.Invoke(null, EventArgs.Empty);
        public Task RequestSingleGameRefreshAsync(Guid id) { Refreshes++; return PendingRefresh?.Task ?? Task.CompletedTask; }
        public void Unlock(FakeUnlock value) => AchievementUnlocked?.Invoke(null, value);
    }
    public sealed class FakeUnlock : EventArgs
    {
        public Guid PlayniteGameId { get; set; }
        public string ApiName { get; set; } = "fixture-achievement";
        public string ProviderKey => "steam";
        public DateTime UnlockTimeUtc => new DateTime(2026, 11, 14, 0, 0, 0, DateTimeKind.Utc);
        public bool IsPreview { get; set; }
        public bool IsTestFire { get; set; }
        public bool IsFriendUnlock { get; set; }
        public bool IsProgressUpdate { get; set; }
        public bool IsCompletionAchievement { get; set; }
        public bool IsGameCompleted { get; set; }
    }
    public sealed class FakeData
    {
        public int Reads, OverviewReads;
        public string[] Games = Array.Empty<string>();
        public string[] GetCachedGameIds() => Games.Concat(Games.Take(1)).ToArray();
        public object GetGameAchievementDataForOverview(Guid gameId)
        { OverviewReads++; return new FakeAchievementData { Achievements = Empty ? Array.Empty<FakeAchievement>() : new[] { new FakeAchievement() } }; }
        public bool Empty, Fail;
        public object GetVisibleGameAchievementData(string gameId)
        {
            Reads++;
            if (Fail) throw new InvalidOperationException("Synthetic provider failure");
            return new FakeAchievementData { Achievements = Empty ? Array.Empty<FakeAchievement>() : new[] { new FakeAchievement() } };
        }
    }
    public sealed class VisibleAchievements : GenericPlugin
    {
        public override Guid Id => new Guid("e6aad2c9-6e06-4d8d-ac55-ac3b252b5f7b");
        public VisibleData AchievementDataService { get; }
        public VisibleAchievements(IPlayniteAPI api, Guid kept, Guid excluded) : base(api)
        { AchievementDataService = new VisibleData(kept, excluded); }
    }
    public sealed class VisibleData
    {
        private readonly Guid kept, excluded;
        public VisibleData(Guid kept, Guid excluded) { this.kept = kept; this.excluded = excluded; }
        public string[] GetCachedGameIds() => new[] { kept.ToString("D"), excluded.ToString("D") };
        public FakeAchievementData GetVisibleGameAchievementData(Guid id) => new FakeAchievementData
        {
            ExcludedFromSummaries = id == excluded,
            Achievements = new[] { new FakeAchievement(), new FakeAchievement { IsFilteredFromSummaries = true } }
        };
    }
    public sealed class FakeAchievementData { public bool ExcludedFromSummaries { get; set; } public FakeAchievement[] Achievements { get; set; } = Array.Empty<FakeAchievement>();public string EffectiveProviderKey=>"steam"; }
    public sealed class FakeCaptureOptions {public FakeCapturePersisted Persisted {get;}=new FakeCapturePersisted();}
    public sealed class FakeCapturePersisted
    {
        public string UnlockScreenshotDirectory {get;set;}="";
        public string UnlockRecordingDirectory {get;set;}="";
        public string UnlockScreenshotSuffixClean=>"clean";
        public string UnlockScreenshotSuffixWithToast=>"notification";
        public string UnlockScreenshotSuffixFramed=>"framed";
    }
    public sealed class FakeCustomCaptureStore
    {
        public FakeCustomCapture[] Records=Array.Empty<FakeCustomCapture>();public int Queries;
        public T QueryAll<T>(Func<IEnumerable<FakeCustomCapture>,T> query){Queries++;return query(Records);}
    }
    public sealed class FakeCustomCapture {public Guid PlayniteGameId {get;set;}public FakeCaptureOverride CaptureOverride {get;set;}=new FakeCaptureOverride();}
    public sealed class FakeCaptureOverride {public string CaptureDirectory {get;set;}="";}
    public sealed class FakeAchievement
    {
        public bool IsFilteredFromSummaries { get; set; }
        public string ApiName => "fixture-achievement";
        public string DisplayName => "Fixture achievement";
        public string Description => "Description";
        public string UnlockedIconPath => @"C:\fixture\badge.png";
        public bool Hidden => false;
        public bool Unlocked => true;
        public DateTime UnlockTimeUtc => new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    }
}
