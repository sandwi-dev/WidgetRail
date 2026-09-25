using Microsoft.VisualStudio.TestTools.UnitTesting;
using WidgetRail.Samples.PlayniteLibrary;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

namespace WidgetRail.Tests.PlayniteLibrary;

public sealed partial class PlayniteLibraryTests
{
    private const string DetailAction = "playnite-library.details.";

    [TestMethod]
    public void HtmlStructurePreservesParagraphsListsInlineWordsAndEntities()
    {
        var html = "<p>A free up<b>grade</b> &amp; more.</p><p>Another paragraph.<br>A line break.</p>"
            + "<ul><li>First item</li><li>Second item</li></ul><script>bad()</script><style>bad{}</style>";
        Assert.AreEqual("A free upgrade & more.\n\nAnother paragraph.\nA line break.\n\n• First item\n\n• Second item",
            PlayniteDescriptionText.Normalize(html));
        Assert.AreEqual("First paragraph.\n\nSecond paragraph.",
            PlayniteDescriptionText.Normalize("First paragraph.\r\n\r\nSecond paragraph."));
        Assert.AreEqual("First paragraph.\n\nSecond paragraph.",
            PlayniteDescriptionText.Normalize("First paragraph.<br><br>Second paragraph."));
        Assert.AreEqual("A formatted source line.",
            PlayniteDescriptionText.Normalize("<p>A formatted\nsource line.</p>"));
    }

    [TestMethod]
    public void CompletionPrefersFreshDetailsAndConfirmedMutationsUntilRefresh()
    {
        var game = new PlayniteBridgeGame("game", "Game", "Steam", true, false, false,
            "Completed", [], [], [], 0, null);
        var item = new FakeHost(1).ItemFactory(0);
        var extras = new PlayniteDetailsExtras { Full = new(item, game) };
        Assert.AreEqual("Completed", extras.ResolveCompletionStatus("Not Played"));
        Assert.IsNull((extras with { Full = new(item, game with { CompletionStatus = null }) })
            .ResolveCompletionStatus("Not Played"));
        extras = extras with { ConfirmedCompletionStatus = "Playing" };
        Assert.AreEqual("Playing", extras.ResolveCompletionStatus("Not Played"),
            "An older in-flight full response must not override a confirmed mutation.");
        Assert.AreEqual("Completed", (extras with { ConfirmedCompletionStatus = null }).ResolveCompletionStatus("Not Played"));
    }

    [TestMethod, Timeout(30_000)]
    [DataRow(false)]
    [DataRow(true)]
    public async Task CompletionControlUpdatesVisibleValueWithoutReloadingDetails(bool reject)
    {
        var host = new FakeHost(3);
        host.SetCompletionStatus("saved-00000", "Not Played");
        var widget = Create(host, out TestApplicationService application);
        application.RejectCompletionChanges = reject;
        var detailReads = 0;
        application.DetailsHandler = async (id, token) =>
        {
            detailReads++;
            return new((await application.ResolveSavedAsync([id], token)).Single());
        };
        await Interactive(widget);
        await Ready(widget, host);
        var game = Nodes(Snapshot(widget, 127_001).Root).First(node => node.ActionId == PlayniteLibraryActions.DetailsOpen);
        await widget.OnActionAsync(new(PlayniteLibraryActions.DetailsOpen, game.Id));
        await widget.OnActionAsync(new(DetailAction + "tab.activity", "tab"));
        await Bounded(widget.WhenDetailsIdleAsync(), "loaded details");
        var before = Snapshot(widget, 127_002);
        Assert.AreEqual("Not Played", Nodes(before.Root).Single(n => n.Id == DetailAction + "completion.value").Text);
        var full = widget.RenderState.Value.DetailsExtras.Full;
        var activity = widget.RenderState.Value.DetailsExtras.Activity;
        await widget.OnActionAsync(new(DetailAction + "completion", DetailAction + "completion.action"));
        await Bounded(widget.WhenDetailsIdleAsync(), "completion action");
        var after = Snapshot(widget, 127_003);
        Assert.AreEqual(reject ? "Not Played" : "Playing",
            Nodes(after.Root).Single(n => n.Id == DetailAction + "completion.value").Text);
        Assert.AreEqual(1, detailReads, "Changing completion must not refetch and clear the modal.");
        Assert.AreSame(full, widget.RenderState.Value.DetailsExtras.Full);
        Assert.AreSame(activity, widget.RenderState.Value.DetailsExtras.Activity);
        Assert.AreEqual(before.ActiveInputScopeId, after.ActiveInputScopeId);
        Assert.AreEqual(PlayniteDetailsTab.Activity, widget.RenderState.Value.DetailsExtras.Tab);
        if (reject) StringAssert.Contains(widget.RenderState.Value.DetailsExtras.OperationMessage!, "could not");
        else Assert.IsNull(widget.RenderState.Value.DetailsExtras.OperationMessage);
        Assert.AreEqual(0, ViewSnapshotValidator.Validate(after).Count);
        await Background(widget);
    }

    [TestMethod, Timeout(30_000)]
    public async Task HeaderGameOptionsResolveToTheOpenGame()
    {
        var host = new FakeHost(3);
        var widget = Create(host);
        await Interactive(widget);
        await Ready(widget, host);
        var game = Nodes(Snapshot(widget, 128_001).Root)
            .Where(node => node.ActionId == PlayniteLibraryActions.DetailsOpen).Skip(1).First();
        await widget.OnActionAsync(new(PlayniteLibraryActions.DetailsOpen, game.Id));
        await Bounded(widget.WhenDetailsIdleAsync(), "loaded details");
        var before = Snapshot(widget, 128_002);
        Assert.IsTrue(Nodes(before.Root).Single(n => n.Id == PlayniteLibraryDetailsPresentation.OptionsId)
            .ContextActions.Any(action => action.Label == "Add favorite"));
        await widget.OnActionAsync(new(PlayniteLibraryActions.Favorite, PlayniteLibraryDetailsPresentation.OptionsId));
        var after = Snapshot(widget, 128_003);
        Assert.IsTrue(Nodes(after.Root).Single(n => n.Id == PlayniteLibraryDetailsPresentation.OptionsId)
            .ContextActions.Any(action => action.Label == "Remove favorite"));
        Assert.AreEqual("saved-00001", widget.RenderState.Value.DetailsItem!.Value.SavedId);
        await Background(widget);
    }

    [TestMethod, Timeout(30_000)]
    public async Task DetailsResumeWithSameTabScopeAndCompletedDataUntilExplicitlyDismissed()
    {
        var host = new FakeHost(3);
        var widget = Create(host, out TestApplicationService application);
        var reads = 0;
        application.ActivityHandler = (_, _) =>
        {
            reads++;
            return ValueTask.FromResult(new PlayniteActivity(true, 60, [new(null, 60, "Play")]));
        };
        await Interactive(widget);
        await Ready(widget, host);
        var game = Nodes(Snapshot(widget, 124_001).Root).First(node => node.ActionId == PlayniteLibraryActions.DetailsOpen);
        await widget.OnActionAsync(new(PlayniteLibraryActions.DetailsOpen, game.Id));
        await widget.OnActionAsync(new(DetailAction + "tab.activity", "tab"));
        await Bounded(widget.WhenDetailsIdleAsync(), "loaded details");
        var before = Snapshot(widget, 124_002);
        var full = widget.RenderState.Value.DetailsExtras.Full;
        var activity = widget.RenderState.Value.DetailsExtras.Activity;
        await Background(widget);
        Assert.AreEqual(ViewNodeKind.ModalLayer, Snapshot(widget, 124_003).Root.Kind);
        await Interactive(widget);
        await Bounded(widget.WhenDetailsIdleAsync(), "resumed details");
        var after = Snapshot(widget, 124_004);
        Assert.AreEqual(before.ActiveInputScopeId, after.ActiveInputScopeId);
        Assert.AreEqual(Nodes(before.Root).Single(n => n.StyleClasses.Contains("wrail-modal__scroll")).Id,
            Nodes(after.Root).Single(n => n.StyleClasses.Contains("wrail-modal__scroll")).Id);
        Assert.AreEqual(PlayniteDetailsTab.Activity, widget.RenderState.Value.DetailsExtras.Tab);
        Assert.AreSame(full, widget.RenderState.Value.DetailsExtras.Full);
        Assert.AreSame(activity, widget.RenderState.Value.DetailsExtras.Activity);
        Assert.AreEqual(1, reads);
        Assert.IsTrue(await Route(widget, after, ControllerButton.B, after.InitialFocusId!));
        await WaitFor(() => widget.RenderState.Value.DetailsItem is null, "explicit details dismissal");
        await Background(widget);
    }

    [TestMethod, Timeout(30_000)]
    [DataRow("overview")]
    [DataRow("achievements")]
    [DataRow("activity")]
    public async Task InterruptedDetailsReadsRestartOnResume(string section)
    {
        var host = new FakeHost(3);
        var widget = Create(host, out TestApplicationService application);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var reads = 0;
        async Task WaitFirst(CancellationToken token)
        {
            if (Interlocked.Increment(ref reads) != 1) return;
            started.SetResult();
            await Task.Delay(Timeout.Infinite, token);
        }
        if (section == "overview") application.DetailsHandler = async (id, token) =>
        {
            await WaitFirst(token);
            var items = await application.ResolveSavedAsync([id], token);
            return new(items.Single());
        };
        else if (section == "achievements") application.AchievementsHandler = async (_, token) =>
        {
            await WaitFirst(token);
            return PlayniteAchievements.Unavailable;
        };
        else application.ActivityHandler = async (_, token) =>
        {
            await WaitFirst(token);
            return PlayniteActivity.Unavailable;
        };
        await Interactive(widget);
        await Ready(widget, host);
        var game = Nodes(Snapshot(widget, 125_001).Root).First(node => node.ActionId == PlayniteLibraryActions.DetailsOpen);
        await widget.OnActionAsync(new(PlayniteLibraryActions.DetailsOpen, game.Id));
        if (section != "overview") await widget.OnActionAsync(new(DetailAction + "tab." + section, "tab"));
        await Bounded(started.Task, "details request started");
        var scope = Snapshot(widget, 125_002).ActiveInputScopeId;
        await Background(widget);
        await Interactive(widget);
        await Bounded(widget.WhenDetailsIdleAsync(), "resumed interrupted request");
        Assert.AreEqual(2, reads);
        Assert.IsFalse(widget.RenderState.Value.DetailsLoading);
        Assert.IsFalse(widget.RenderState.Value.DetailsExtras.AchievementsLoading);
        Assert.IsFalse(widget.RenderState.Value.DetailsExtras.ActivityLoading);
        Assert.AreEqual(scope, Snapshot(widget, 125_003).ActiveInputScopeId);
        await Background(widget);
    }

    [TestMethod, Timeout(30_000)]
    public async Task ResumeDoesNotReplayInstallationOrRetainConfirmation()
    {
        var host = new FakeHost(3);
        var widget = Create(host, out TestApplicationService application);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        application.InstallationHandler = async (_, _, token) =>
        {
            started.SetResult();
            await Task.Delay(Timeout.Infinite, token);
            return true;
        };
        await Interactive(widget);
        await Ready(widget, host);
        var game = Nodes(Snapshot(widget, 126_001).Root).First(node => node.ActionId == PlayniteLibraryActions.DetailsOpen);
        await widget.OnActionAsync(new(PlayniteLibraryActions.DetailsOpen, game.Id));
        await Bounded(widget.WhenDetailsIdleAsync(), "loaded details");
        await widget.OnActionAsync(new(DetailAction + "uninstall", "options"));
        await Background(widget);
        await Interactive(widget);
        Assert.IsFalse(widget.RenderState.Value.DetailsExtras.ConfirmUninstall);
        await widget.OnActionAsync(new(DetailAction + "uninstall", "options"));
        await widget.OnActionAsync(new(DetailAction + "uninstall.confirm", "confirm"));
        await Bounded(started.Task, "uninstall started");
        await widget.OnActionAsync(new(DetailAction + "refresh", "refresh"));
        Assert.IsTrue(widget.RenderState.Value.DetailsExtras.OperationBusy, "Refresh must not cancel a submitted operation.");
        await Background(widget);
        await Interactive(widget);
        await Bounded(widget.WhenDetailsIdleAsync(), "resumed after operation");
        Assert.AreEqual(1, application.InstallationRequests.Count);
        Assert.IsFalse(widget.RenderState.Value.DetailsExtras.OperationBusy);
        StringAssert.Contains(widget.RenderState.Value.DetailsExtras.OperationMessage!, "Refresh");
        await Background(widget);
    }

    [TestMethod, Timeout(30_000)]
    public async Task EachOpeningGetsFreshScopeButLoadingRefreshAndTabsKeepIt()
    {
        var host = new FakeHost(3);
        var widget = Create(host);
        await Interactive(widget);
        await Ready(widget, host);
        var page = Snapshot(widget, 123_001);
        var games = Nodes(page.Root).Where(node => node.ActionId == PlayniteLibraryActions.DetailsOpen).Take(2).ToArray();
        await widget.OnActionAsync(new(PlayniteLibraryActions.DetailsOpen, games[0].Id));
        var first = Snapshot(widget, 123_002);
        var firstScroll = Nodes(first.Root).Single(node => node.StyleClasses.Contains("wrail-modal__scroll")).Id;
        Assert.AreEqual(PlayniteLibraryDetailsPresentation.PlayId, first.InitialFocusId);
        await Bounded(widget.WhenDetailsIdleAsync(), "detail loading");
        Assert.AreEqual(first.ActiveInputScopeId, Snapshot(widget, 123_003).ActiveInputScopeId);
        await widget.OnActionAsync(new(DetailAction + "refresh", "refresh"));
        await Bounded(widget.WhenDetailsIdleAsync(), "detail refresh");
        Assert.AreEqual(first.ActiveInputScopeId, Snapshot(widget, 123_004).ActiveInputScopeId);
        await widget.OnActionAsync(new(DetailAction + "tab.activity", "tab"));
        await Bounded(widget.WhenDetailsIdleAsync(), "detail tab");
        Assert.AreEqual(first.ActiveInputScopeId, Snapshot(widget, 123_005).ActiveInputScopeId);
        await widget.OnActionAsync(new(PlayniteLibraryActions.DetailsClose, "close"));
        Assert.AreEqual(page.ActiveInputScopeId, Snapshot(widget, 123_006).ActiveInputScopeId);
        await widget.OnActionAsync(new(PlayniteLibraryActions.DetailsOpen, games[1].Id));
        var second = Snapshot(widget, 123_007);
        Assert.AreNotEqual(first.ActiveInputScopeId, second.ActiveInputScopeId);
        Assert.AreNotEqual(firstScroll, Nodes(second.Root).Single(node => node.StyleClasses.Contains("wrail-modal__scroll")).Id);
        Assert.AreEqual(PlayniteLibraryDetailsPresentation.PlayId, second.InitialFocusId);
        Assert.AreEqual(0, ViewSnapshotValidator.Validate(second).Count);
        await Background(widget);
        Assert.IsNotNull(widget.RenderState.Value.DetailsItem);
        Assert.AreEqual(second.ActiveInputScopeId, Snapshot(widget, 123_008).ActiveInputScopeId);
        var restartedHost = new FakeHost(3);
        var restarted = Create(restartedHost);
        await Interactive(restarted);
        await Ready(restarted, restartedHost);
        var reloadedGame = Nodes(Snapshot(restarted, 123_009).Root).First(node => node.ActionId == PlayniteLibraryActions.DetailsOpen);
        await restarted.OnActionAsync(new(PlayniteLibraryActions.DetailsOpen, reloadedGame.Id));
        Assert.AreNotEqual(first.ActiveInputScopeId, Snapshot(restarted, 123_010).ActiveInputScopeId,
            "A new worker must not reuse focus from an old worker's first opening.");
        await Background(restarted);
    }

    [TestMethod, Timeout(30_000)]
    public async Task OptionalDetailsLoadLazilyAndCacheUntilRefresh()
    {
        var host = new FakeHost(3);
        var widget = Create(host, out TestApplicationService application);
        var achievementReads = 0;
        var activityReads = 0;
        application.AchievementsHandler = (_, _) =>
        {
            achievementReads++;
            return ValueTask.FromResult(new PlayniteAchievements(true, 0, 0, []));
        };
        application.ActivityHandler = (_, _) =>
        {
            activityReads++;
            return ValueTask.FromResult(PlayniteActivity.Unavailable);
        };
        await Interactive(widget);
        await Ready(widget, host);
        var game = Nodes(Snapshot(widget, 120_001).Root).First(node => node.ActionId == PlayniteLibraryActions.DetailsOpen);
        await widget.OnActionAsync(new(PlayniteLibraryActions.DetailsOpen, game.Id));
        await Bounded(widget.WhenDetailsIdleAsync(), "overview");
        Assert.AreEqual(0, achievementReads);
        Assert.AreEqual(0, activityReads);
        await widget.OnActionAsync(new(DetailAction + "tab.achievements", "tab"));
        await Bounded(widget.WhenDetailsIdleAsync(), "achievements");
        var empty = Snapshot(widget, 120_002);
        Assert.IsTrue(Nodes(empty.Root).Any(node => node.Id == DetailAction + "achievements.empty"));
        Assert.AreEqual(0, ViewSnapshotValidator.Validate(empty).Count);
        await widget.OnActionAsync(new(DetailAction + "tab.activity", "tab"));
        await Bounded(widget.WhenDetailsIdleAsync(), "activity");
        var unavailable = Snapshot(widget, 120_003);
        Assert.IsTrue(Nodes(unavailable.Root).Any(node => node.Id == DetailAction + "activity.unavailable"));
        Assert.AreEqual(0, ViewSnapshotValidator.Validate(unavailable).Count);
        await widget.OnActionAsync(new(DetailAction + "tab.achievements", "tab"));
        await Bounded(widget.WhenDetailsIdleAsync(), "cached achievements");
        Assert.AreEqual(1, achievementReads);
        Assert.AreEqual(1, activityReads);
        await widget.OnActionAsync(new(DetailAction + "refresh", "refresh"));
        await Bounded(widget.WhenDetailsIdleAsync(), "refreshed achievements");
        Assert.AreEqual(2, achievementReads);
        Assert.AreEqual(1, activityReads);
        await Background(widget);
    }

    [TestMethod, Timeout(30_000)]
    public async Task LateOptionalResultsCannotReplaceAnotherGameOrClosedModal()
    {
        var host = new FakeHost(3);
        var widget = Create(host, out TestApplicationService application);
        var completion = new TaskCompletionSource<PlayniteAchievements>(TaskCreationOptions.RunContinuationsAsynchronously);
        application.AchievementsHandler = (_, _) => new(completion.Task);
        await Interactive(widget);
        await Ready(widget, host);
        var games = Nodes(Snapshot(widget, 121_001).Root).Where(node => node.ActionId == PlayniteLibraryActions.DetailsOpen).Take(2).ToArray();
        await widget.OnActionAsync(new(PlayniteLibraryActions.DetailsOpen, games[0].Id));
        await widget.OnActionAsync(new(DetailAction + "tab.achievements", "tab"));
        await widget.OnActionAsync(new(PlayniteLibraryActions.DetailsClose, "close"));
        await widget.OnActionAsync(new(PlayniteLibraryActions.DetailsOpen, games[1].Id));
        var savedId = widget.RenderState.Value.DetailsItem!.Value.SavedId;
        completion.SetResult(new(true, 0, 0, []));
        await Bounded(widget.WhenDetailsIdleAsync(), "late optional result");
        Assert.AreEqual(savedId, widget.RenderState.Value.DetailsItem!.Value.SavedId);
        Assert.IsNull(widget.RenderState.Value.DetailsExtras.Achievements);
        Assert.AreEqual(PlayniteDetailsTab.Overview, widget.RenderState.Value.DetailsExtras.Tab);
        await widget.OnActionAsync(new(PlayniteLibraryActions.DetailsClose, "close"));
        Assert.IsNull(widget.RenderState.Value.DetailsItem);
        Assert.AreEqual(0, ViewSnapshotValidator.Validate(Snapshot(widget, 121_002)).Count);
        await Background(widget);
    }

    [TestMethod, Timeout(30_000)]
    public async Task SecretAchievementsRequireRevealAndUninstallRequiresConfirmation()
    {
        var host = new FakeHost(3);
        var widget = Create(host, out TestApplicationService application);
        application.AchievementsHandler = (_, _) => ValueTask.FromResult(new PlayniteAchievements(true, 1, 0,
            [new("Secret name", "Secret description", false, null, null, null, true)]));
        await Interactive(widget);
        await Ready(widget, host);
        var game = Nodes(Snapshot(widget, 122_001).Root).First(node => node.ActionId == PlayniteLibraryActions.DetailsOpen);
        await widget.OnActionAsync(new(PlayniteLibraryActions.DetailsOpen, game.Id));
        var savedId = widget.RenderState.Value.DetailsItem!.Value.SavedId;
        await widget.OnActionAsync(new(DetailAction + "tab.achievements", "tab"));
        await Bounded(widget.WhenDetailsIdleAsync(), "secret achievement");
        var secret = Snapshot(widget, 122_002);
        Assert.IsFalse(Nodes(secret.Root).Any(node => node.Text is "Secret name" or "Secret description"));
        Assert.IsFalse(Nodes(secret.Root).Any(node => node.AccessibilityLabel?.Contains("Secret name") == true));
        Assert.AreEqual(0, ViewSnapshotValidator.Validate(secret).Count);
        await widget.OnActionAsync(new(DetailAction + "achievement.0", "achievement"));
        Assert.IsTrue(Nodes(Snapshot(widget, 122_003).Root).Any(node => node.Text == "Secret name"));
        await widget.OnActionAsync(new(DetailAction + "uninstall.confirm", "confirm"));
        Assert.AreEqual(0, application.InstallationRequests.Count);
        await widget.OnActionAsync(new(DetailAction + "uninstall", "options"));
        var confirmation = Snapshot(widget, 122_004);
        Assert.AreEqual(0, ViewSnapshotValidator.Validate(confirmation).Count);
        Assert.AreEqual(DetailAction + "uninstall.cancel", confirmation.InitialFocusId);
        Assert.IsTrue(await Route(widget, confirmation, ControllerButton.B, confirmation.InitialFocusId!));
        await WaitFor(() => !widget.RenderState.Value.DetailsExtras.ConfirmUninstall, "cancel uninstall");
        Assert.IsNotNull(widget.RenderState.Value.DetailsItem);
        Assert.AreEqual(0, application.InstallationRequests.Count);
        await widget.OnActionAsync(new(DetailAction + "uninstall", "options"));
        await widget.OnActionAsync(new(DetailAction + "uninstall.confirm", "confirm"));
        await Bounded(widget.WhenDetailsIdleAsync(), "uninstall request");
        Assert.AreEqual((savedId, false), application.InstallationRequests.Single());
        await Background(widget);
        Assert.IsNotNull(widget.RenderState.Value.DetailsExtras.Achievements);
        Assert.IsFalse(widget.RenderState.Value.DetailsExtras.OperationBusy);
        Assert.IsFalse(widget.RenderState.Value.DetailsExtras.ConfirmUninstall);
    }
}

public sealed partial class PlayniteLibraryLayoutTests
{
    [TestMethod]
    public void DescriptionKeepsRealParagraphsInsteadOfFixedCharacterChunks()
    {
        var paragraph = string.Join(" ", Enumerable.Repeat("A sentence that wraps naturally without splitting its paragraph.", 20));
        var description = paragraph + "\n\nA second paragraph.\n\n• A feature\n• Another feature";
        var game = ArtworkItem("description-app", "description-id", "Description game", "Steam", "poster", "hero");
        game = game.WithProjectedValue(game.Value with
        {
            Presentation = game.Presentation with
            {
                Metadata = new WidgetAppLibraryMetadata("description", new("test", "test", "Steam", 1))
                { Description = description },
            },
        });
        var page = PlayniteLibraryPresentation.Render(State(Snapshot(WidgetPagedResourceStatus.Ready, [game]),
            PlayniteLibraryPrivateState.Empty, PlayniteLibraryRoute.Library, []));
        var snapshot = new PresentationWidget(page.WithModal(PlayniteLibraryDetailsPresentation.Create(
            game, true, null, "Ready", null))).RenderSnapshot("description.paragraphs", 1);
        var paragraphs = Nodes(snapshot.Root).Single(node => node.Id == "playnite-library.details.description.paragraphs");
        Assert.AreEqual(3, paragraphs.Children.Count);
        Assert.AreEqual(paragraph, paragraphs.Children[0].Text);
        Assert.AreEqual("A second paragraph.", paragraphs.Children[1].Text);
        Assert.AreEqual("• A feature\n• Another feature", paragraphs.Children[2].Text);
        Assert.AreEqual(0, ViewSnapshotValidator.Validate(snapshot).Count);
    }

    [TestMethod]
    [DataRow("Played")]
    [DataRow(null)]
    public void PopulatedGameDetailsRemainValidAcrossTabs(string? completion)
    {
        var item = PlayniteLibraryItem.From(Item("full-app", "full-game", "Test game", "Steam"));
        var metadata = new PlayniteBridgeGame(item.Value.SavedId, "Test game", "Steam",
            true, true, false, completion, ["Favorites"], ["Adventure"], ["Windows"], 600, 1)
        {
            Developers = ["Developer"], Publishers = ["Publisher"], ReleaseDate = "2026-01-01",
            Features = ["Controller"], Series = ["Series"], AgeRatings = ["Teen"], Tags = ["Story"],
            PlayCount = 3, InstallSize = 1024, CriticScore = 80, CommunityScore = 85, UserScore = 90,
            Notes = "Saved game notes", Links = [new("Website", "https://example.com/game")],
        };
        var organization = PlayniteLibraryPrivateState.Empty with
        {
            Categories = Enumerable.Range(0, 7).Select(index =>
                new PlayniteLibraryCategory("category." + index, "Category " + index, [])).ToArray(),
        };
        var page = PlayniteLibraryPresentation.Render(State(Snapshot(WidgetPagedResourceStatus.Ready, [item]),
            organization, PlayniteLibraryRoute.Library, []));
        foreach (var tab in Enum.GetValues<PlayniteDetailsTab>())
        {
            var modal = PlayniteLibraryDetailsPresentation.Create(item, true, null, "Ready", null,
                extras: new() { Full = new(item.Value, metadata), Tab = tab }, categories: organization.Categories);
            var snapshot = new PresentationWidget(page.WithModal(modal)).RenderSnapshot("populated.details", 1);
            Assert.AreEqual(0, ViewSnapshotValidator.Validate(snapshot).Count);
            var nodes = Nodes(snapshot.Root.Children[1]).ToArray();
            Assert.AreEqual(ProtocolConstants.MaximumContextActionCount,
                nodes.Single(node => node.Id == PlayniteLibraryDetailsPresentation.OptionsId).ContextActions.Count);
            var button = nodes.Single(node => node.ActionId == "playnite-library.details.completion");
            Assert.AreEqual(ViewNodeKind.Button, button.Kind);
            if (tab == PlayniteDetailsTab.Overview && completion is not null)
            {
                Assert.IsTrue(nodes.Any(node => node.Text == completion));
                Assert.AreNotEqual("playnite-library.details.completion", button.Id,
                    "The action control must not collide with the loaded metadata row.");
            }
        }
    }

    [TestMethod]
    public void DetailsUsesPosterAndSameGameOptionsWithoutCategorySection()
    {
        var game = ArtworkItem("game-app", "game-id", "Test game", "Steam", "poster-handle", "hero-handle");
        var organization = PlayniteLibraryPrivateState.Empty with
        { Categories = [new("favorites", "My games", [game.Value.SavedId])], FavoriteSavedIds = [game.Value.SavedId] };
        var page = PlayniteLibraryPresentation.Render(State(Snapshot(WidgetPagedResourceStatus.Ready, [game]),
            organization, PlayniteLibraryRoute.Library, []));
        var before = new PresentationWidget(page).RenderSnapshot("details.options", 1);
        var tile = Nodes(before.Root).Single(node => node.ActionId == PlayniteLibraryActions.DetailsOpen);
        var modal = PlayniteLibraryDetailsPresentation.Create(game, true, null, "Ready", null,
            categories: organization.Categories, openingId: "details.opening.7", favorite: true);
        var snapshot = new PresentationWidget(page.WithModal(modal)).RenderSnapshot("details.options", 2);
        var nodes = Nodes(snapshot.Root.Children[1]).ToArray();
        var options = nodes.Single(node => node.Id == PlayniteLibraryDetailsPresentation.ContentId);
        Assert.AreEqual(ViewNodeKind.Stack, nodes.Single(node => node.Id == "playnite-library.details.actions").Kind);
        Assert.AreEqual("playnite-library.details.overview-header", options.Children[0].Id);
        Assert.IsTrue(options.Children[1].StyleClasses.Contains("playnite-library-details-tabs"));
        Assert.IsFalse(nodes.Any(node => node.Kind == ViewNodeKind.Button && node.Text is "Close" or "Refresh"));
        var header = snapshot.Root.Children[1].Children[0];
        Assert.AreEqual("playnite-library.details.header-hints", header.Children[1].Id);
        Assert.IsTrue(Nodes(header).Any(node => node.Text == "Refresh"));
        Assert.IsTrue(Nodes(header).Any(node => node.Text == "Close"));

        var anchor = nodes.Single(node => node.Id == PlayniteLibraryDetailsPresentation.OptionsId);
        CollectionAssert.AreEqual(tile.ContextActions.ToArray(), anchor.ContextActions.ToArray());
        Assert.AreEqual(ControllerButton.X, anchor.ContextMenuButton);
        Assert.AreEqual(0, options.ContextActions.Count);
        Assert.AreEqual(anchor.Id, header.Children[1].Children[0].Id);
        Assert.IsTrue(Nodes(header.Children[1].Children[1]).Any(node => node.Text == "Refresh"));
        var sourceRow = nodes.Single(node => node.Id == "playnite-library.details.source-row");
        Assert.AreEqual(ViewNodeKind.Row, sourceRow.Kind);
        Assert.AreEqual("Steam", sourceRow.Children[0].Text);
        Assert.AreEqual("No completion status", sourceRow.Children[1].Text);
        Assert.IsFalse(nodes.Any(node => node.Text is "Installed" or "Not installed"));
        Assert.AreEqual("poster-handle", nodes.Single(node => node.Id == "playnite-library.details." + "poster").ArtworkHandle);
        Assert.IsFalse(nodes.Any(node => node.Id == "playnite-library.details." + "categories.title"));
        Assert.AreEqual(0, ViewSnapshotValidator.Validate(snapshot).Count);
    }

    [TestMethod]
    public void RichDetailsRemainBoundedAndValidAcrossOptionalStates()
    {
        var game = PlayniteLibraryItem.From(Item("details-app", "details-game", "Test game", "Steam"));
        var achievements = new PlayniteAchievements(true, 4000, 0,
            Enumerable.Range(0, 4000).Select(index => new PlayniteAchievement(
                "Achievement " + index, "A description", false, null, null, null, false)).ToArray());
        var activity = new PlayniteActivity(true, 600000,
            Enumerable.Range(0, 10000).Select(_ => new PlayniteSession(null, 60, "Play")).ToArray());
        var cases = new PlayniteDetailsExtras[]
        {
            new() { Tab = PlayniteDetailsTab.Achievements, AchievementsLoading = true },
            new() { Tab = PlayniteDetailsTab.Achievements, AchievementsError = "Refresh to retry" },
            new() { Tab = PlayniteDetailsTab.Achievements, Achievements = achievements },
            new() { Tab = PlayniteDetailsTab.Achievements, Achievements = achievements, Page = 199 },
            new() { Tab = PlayniteDetailsTab.Activity, ActivityLoading = true },
            new() { Tab = PlayniteDetailsTab.Activity, ActivityError = "Refresh to retry" },
            new() { Tab = PlayniteDetailsTab.Activity, Activity = activity },
            new() { Tab = PlayniteDetailsTab.Activity, Activity = activity, Page = 499 },
            new() { ConfirmUninstall = true },
        };
        var page = PlayniteLibraryPresentation.Render(State(Snapshot(WidgetPagedResourceStatus.Ready, [game]),
            PlayniteLibraryPrivateState.Empty, PlayniteLibraryRoute.Library, []));
        foreach (var state in cases)
        {
            var view = page.WithModal(PlayniteLibraryDetailsPresentation.Create(game, true, null, "Ready", null, extras: state));
            var snapshot = new PresentationWidget(view).RenderSnapshot("details.fixture", 1);
            Assert.AreEqual(0, ViewSnapshotValidator.Validate(snapshot).Count, state.ToString());
            Assert.IsTrue(Nodes(snapshot.Root).Count() < 300, "Optional lists must not expand the entire provider result into the view.");
            ExportFixture("Details-" + state.Tab + "-" + state.Page + "-" + state.AchievementsLoading + "-" + state.ActivityLoading + "-" + state.ConfirmUninstall, snapshot);
        }
    }

    [TestMethod]
    public void SharedNavigationSeparatesPageAndGameOptionsAndLibraryAdvertisesSearch()
    {
        var game = PlayniteLibraryItem.From(Item("app-layout", "saved-layout", "Test game", "Steam"));
        ViewNode? previousHeader = null;
        foreach (var route in new[] { PlayniteLibraryRoute.Library, PlayniteLibraryRoute.Browse })
        {
            var state = State(Snapshot(WidgetPagedResourceStatus.Ready, [game]), PlayniteLibraryPrivateState.Empty, route, []);
            var snapshot = new PresentationWidget(PlayniteLibraryPresentation.Render(state)).RenderSnapshot("navigation", 1);
            var nodes = Nodes(snapshot.Root).ToArray();
            var header = nodes.Single(node => node.Id == "playnite-library.home.actions");
            if (previousHeader is not null)
            {
                CollectionAssert.AreEqual(previousHeader.StyleClasses.ToArray(), header.StyleClasses.ToArray());
                CollectionAssert.AreEqual(previousHeader.Children.Select(node => node.Id).ToArray(), header.Children.Select(node => node.Id).ToArray());
            }
            previousHeader = header;
            Assert.IsTrue(nodes.Where(node => node.ActionId == PlayniteLibraryActions.DetailsOpen).All(node => node.ContextMenuButton == ControllerButton.X));
            var menu = nodes.Single(node => node.Id == "playnite-library.library.menu");
            Assert.AreEqual(ControllerButton.Menu, menu.ContextMenuButton);
            Assert.IsTrue(menu.ContextActions.Any(action => action.ActionId == PlayniteLibraryActions.CategoriesOpen));
            if (route == PlayniteLibraryRoute.Browse)
            {
                var frame = nodes.Single(node => node.Id == "playnite-library.browse.frame");
                CollectionAssert.AreEqual(new[] { header.Id, "playnite-library.browse.page" }, frame.Children.Select(node => node.Id).ToArray());
                var hint = nodes.Single(node => node.Id == "playnite-library.browse.hint.search.key");
                Assert.AreEqual(ControllerPrompt.RightStickPress, hint.ControllerPrompt);
                Assert.IsTrue(nodes.SelectMany(node => node.Shortcuts).Any(shortcut => shortcut.Button == ControllerButton.RightStick && shortcut.ActionId == PlayniteLibraryActions.SearchFocus));
            }
            Assert.AreEqual(0, ViewSnapshotValidator.Validate(snapshot).Count);
        }
    }
}
