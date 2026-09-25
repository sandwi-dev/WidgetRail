using Microsoft.VisualStudio.TestTools.UnitTesting;
using WidgetRail.Samples.PlayniteLibrary;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

namespace WidgetRail.Tests.PlayniteLibrary;

public sealed partial class PlayniteLibraryTests
{
    private const string DetailAction = "playnite-library.details.";

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
        Assert.IsNull(widget.RenderState.Value.DetailsExtras.Achievements);
        Assert.IsNull(widget.RenderState.Value.DetailsExtras.Full);
    }
}

public sealed partial class PlayniteLibraryLayoutTests
{
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
