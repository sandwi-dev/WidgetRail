using Microsoft.VisualStudio.TestTools.UnitTesting;
using WidgetRail.Samples.PlayniteLibrary;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

namespace WidgetRail.Tests.PlayniteLibrary;

public sealed partial class PlayniteLibraryTests
{
    [TestMethod]
    [DataRow("empty")]
    [DataRow("unavailable")]
    [DataRow("populated")]
    [DataRow("failure")]
    [DataRow("loading")]
    public async Task DetailsBumpersKeepStableFocusAncestryAcrossEveryOptionalState(string scenario)
    {
        var provider = new FakeHost(1);
        var widget = Create(provider, out TestApplicationService application);
        var pending = new TaskCompletionSource<PlayniteAchievements>(TaskCreationOptions.RunContinuationsAsynchronously);
        application.AchievementsHandler = (_, token) => scenario switch
        {
            "loading" => new(pending.Task.WaitAsync(token)),
            "failure" => throw new InvalidOperationException("Fixture unavailable"),
            "unavailable" => ValueTask.FromResult(PlayniteAchievements.Unavailable),
            "populated" => ValueTask.FromResult(new PlayniteAchievements(true, 1, 1,
                [new("Achievement", "Description", true, null, null, null, false)])),
            _ => ValueTask.FromResult(new PlayniteAchievements(true, 0, 0, [])),
        };
        await Interactive(widget);
        await Ready(widget, provider);
        var game = PageNodes(widget, Snapshot(widget, 160_000).Root).First(node => node.ActionId == PlayniteLibraryActions.DetailsOpen);
        await widget.OnActionAsync(new(PlayniteLibraryActions.DetailsOpen, game.Id));
        await Bounded(widget.WhenDetailsIdleAsync(), "initial details");
        var initial = Snapshot(widget, 160_001);
        var ancestry = Path(initial.Root, PlayniteLibraryDetailsPresentation.PlayId);
        long sequence = 160_002;
        foreach (var direction in new[] { ControllerButton.RightBumper, ControllerButton.LeftBumper })
        {
            foreach (var expected in direction == ControllerButton.RightBumper
                ? new[] { PlayniteDetailsTab.Information, PlayniteDetailsTab.Achievements, PlayniteDetailsTab.Activity, PlayniteDetailsTab.Links, PlayniteDetailsTab.Description }
                : new[] { PlayniteDetailsTab.Links, PlayniteDetailsTab.Activity, PlayniteDetailsTab.Achievements, PlayniteDetailsTab.Information, PlayniteDetailsTab.Description })
            {
                var before = Snapshot(widget, sequence++);
                var leavingAchievements = widget.RenderState.Value.DetailsExtras.Tab == PlayniteDetailsTab.Achievements;
                Assert.IsTrue(await Route(widget, before, direction, PlayniteLibraryDetailsPresentation.PlayId),
                    scenario + ": the bumper must route from retained Play focus");
                await WaitFor(() => widget.RenderState.Value.DetailsExtras.Tab == expected, "bumper navigates to " + expected);
                if (leavingAchievements) pending.TrySetResult(new(true, 0, 0, []));
                if (scenario != "loading" || expected != PlayniteDetailsTab.Achievements)
                    await Bounded(widget.WhenDetailsIdleAsync(), "details " + expected);
                var after = Snapshot(widget, sequence++);
                Assert.AreEqual(initial.ActiveInputScopeId, after.ActiveInputScopeId);
                CollectionAssert.AreEqual(ancestry, Path(after.Root, PlayniteLibraryDetailsPresentation.PlayId),
                    "Optional results must never reparent retained native focus.");
                Assert.AreEqual(0, ViewSnapshotValidator.Validate(after).Count);
            }
        }
        await Background(widget);

        static string[] Path(ViewNode node, string target)
        {
            if (node.Id == target) return [node.Kind + ":" + node.Id];
            foreach (var child in node.Children)
            {
                var path = Path(child, target);
                if (path.Length != 0) return [node.Kind + ":" + node.Id, .. path];
            }
            return [];
        }
    }

    [TestMethod]
    public async Task LocalTitleSuggestionsAreBoundedDeduplicatedAndDoNotQueryProvider()
    {
        var provider = new FakeHost(4)
        {
            ItemFactory = index => WithPresentation(Item(index), displayName: index switch
            {
                0 => "Alpha",
                1 => "ALPHA",
                2 => "The Alpha Story",
                _ => new string('a', 94) + "\U0001F3AE Saga",
            }),
        };
        var widget = Create(provider);
        await Interactive(widget);
        await Ready(widget, provider);
        await widget.OnActionAsync(new(PlayniteLibraryActions.BrowseOpen, "library"));
        await Bounded(widget.WhenLibraryIdleAsync(), "browse suggestions");
        var field = PageNodes(widget, Snapshot(widget, 150_001).Root).Single(node => node.Id == "playnite-library.search");
        var reads = provider.Queries.Count;
        var query = new TextSuggestionQuery(field.Id, field.TextEntrySuggestions!.Key,
            Guid.NewGuid().ToString("N"), 1, "Alp", 2, 120);
        var result = await widget.OnSuggestTextAsync(query, CancellationToken.None);
        Assert.AreEqual(2, result.Items.Count);
        Assert.IsTrue(result.Items[0].Value.Equals("Alpha", StringComparison.OrdinalIgnoreCase));
        Assert.AreEqual("The Alpha Story", result.Items[1].Value);
        Assert.AreEqual(reads, provider.Queries.Count);
        Assert.IsTrue(TextSuggestionContract.IsValid(result, 2, 120));
        var longTitle = await widget.OnSuggestTextAsync(query with { Text = "aaaa" }, CancellationToken.None);
        Assert.AreEqual(1, longTitle.Items.Count);
        Assert.IsTrue(TextSuggestionContract.IsValid(longTitle, 2, 120), "Truncation must preserve UTF-16 surrogate pairs.");
        Assert.IsTrue((await widget.OnSuggestTextAsync(query with { SourceKey = "retired" }, CancellationToken.None)).Unavailable);
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        await Assert.ThrowsAsync<OperationCanceledException>(async () =>
            await widget.OnSuggestTextAsync(query, cancelled.Token));
        await widget.OnActionAsync(new(PlayniteLibraryActions.HomeOpen, "home"));
        Assert.IsTrue((await widget.OnSuggestTextAsync(query, CancellationToken.None)).Unavailable);
        await Background(widget);
    }

    [TestMethod]
    public async Task FilterDraftCancelsWithoutQueryAndAppliesCombinedChangesOnce()
    {
        var provider = new FakeHost(8);
        var widget = Create(provider);
        await Interactive(widget);
        await Ready(widget, provider);
        await widget.OnActionAsync(new(PlayniteLibraryActions.BrowseOpen, "library"));
        await Bounded(widget.WhenLibraryIdleAsync(), "browse filters");
        var before = provider.Queries.Count;
        var owner = widget.RenderState.Value.IndexedBrowse.Publication!.QueryOwner;
        await widget.OnActionAsync(new(PlayniteLibraryActions.FiltersOpen, PlayniteLibraryActions.FiltersOpen));
        Assert.IsTrue(Snapshot(widget, 150_000).Root.AnchoredPresentation?.SizeToContentHeight,
            "The filter dialog should fit its controls instead of reserving its maximum height.");
        await widget.OnActionAsync(new("playnite-library.filters.installed", "installed"));
        await widget.OnActionAsync(new("playnite-library.filters.recent", "recent"));
        Assert.AreEqual(before, provider.Queries.Count);
        Assert.IsFalse(widget.RenderState.Value.BrowseCollection.Query.InstalledOnly);
        await widget.OnActionAsync(new(PlayniteLibraryActions.FiltersCancel, "cancel"));
        Assert.IsNull(widget.RenderState.Value.FilterDraft);
        Assert.AreSame(owner, widget.RenderState.Value.IndexedBrowse.Publication!.QueryOwner);
        await widget.OnActionAsync(new(PlayniteLibraryActions.FiltersOpen, PlayniteLibraryActions.FiltersOpen));
        await widget.OnActionAsync(new(PlayniteLibraryActions.FiltersApply, PlayniteLibraryActions.FiltersApply));
        Assert.AreEqual(before, provider.Queries.Count, "Applying an unchanged draft preserves the query.");
        await widget.OnActionAsync(new(PlayniteLibraryActions.FiltersOpen, PlayniteLibraryActions.FiltersOpen));
        await widget.OnActionAsync(new("playnite-library.filters.installed", "installed"));
        await widget.OnActionAsync(new("playnite-library.filters.recent", "recent"));
        await widget.OnActionAsync(new(PlayniteLibraryActions.FiltersApply, PlayniteLibraryActions.FiltersApply));
        await Bounded(widget.WhenLibraryIdleAsync(), "combined filters");
        Assert.AreEqual(before + 1, provider.Queries.Count);
        Assert.IsTrue(widget.RenderState.Value.BrowseCollection.Query.InstalledOnly);
        Assert.IsTrue(widget.RenderState.Value.BrowseCollection.RecentlyPlayed);
        Assert.AreEqual(PlayniteLibraryActions.FiltersOpen, Snapshot(widget, 150_002).InitialFocusId);
        await widget.OnActionAsync(new(PlayniteLibraryActions.FiltersOpen, PlayniteLibraryActions.FiltersOpen));
        await widget.OnActionAsync(new(PlayniteLibraryActions.HomeOpen, "home"));
        Assert.IsNull(widget.RenderState.Value.FilterDraft);
        await Background(widget);
    }

    [TestMethod]
    public async Task QuickPlayUsesExactIndexedGameWithoutAddingFocusJumpShortcuts()
    {
        var provider = new FakeHost(80);
        var widget = Create(provider);
        await Interactive(widget);
        await Ready(widget, provider);
        using var host = WidgetTestHost.CreateIndexedCollectionHost(widget, "quick-play");
        using var row = await host.AcquireAsync(PlayniteLibraryPresentation.HomeRailId, 63, 1);
        var item = row.Range.Items.Single();
        Assert.IsTrue(item.Root.Shortcuts.Any(shortcut =>
            shortcut.Button == ControllerButton.Y && shortcut.ActionId == PlayniteLibraryActions.QuickPlay));
        Assert.IsFalse(PageNodes(widget, host.CurrentSnapshot.Root).Any(node =>
            node.Shortcuts.Any(shortcut => shortcut.Button == ControllerButton.LeftStick)));
        Assert.AreEqual(WidgetOperationAdmission.Enqueued, row.RouteAction(item.Key, ControllerButton.Y));
        await WaitIndexed(() => provider.Launches.Count == 1);
        Assert.AreEqual("app-00063", provider.Launches.Single());
        await Background(widget);
    }

    [TestMethod]
    public async Task AchievementPreviewIsLazyReadOnlyAndDoesNotRevealSecrets()
    {
        var provider = new FakeHost(1);
        var widget = Create(provider, out TestApplicationService application);
        application.AchievementsHandler = (_, _) => ValueTask.FromResult(new PlayniteAchievements(true, 1, 0,
            [new("Secret name", "Secret description", false, null, null, null, true)
                { Icon = @"C:\cache\secret.png", Progress = new(3, 10) }]));
        await Interactive(widget);
        await Ready(widget, provider);
        var game = PageNodes(widget, Snapshot(widget, 150_003).Root).First(node => node.ActionId == PlayniteLibraryActions.DetailsOpen);
        await widget.OnActionAsync(new(PlayniteLibraryActions.DetailsOpen, game.Id));
        await widget.OnActionAsync(new(DetailAction + "tab.achievements", "tab"));
        await Bounded(widget.WhenDetailsIdleAsync(), "achievements");
        using var host = WidgetTestHost.CreateIndexedCollectionHost(widget, "achievement-preview");
        var surface = PageNodes(widget, host.CurrentSnapshot.Root).Single(node => node.Id == DetailAction + "preview-surface");
        Assert.IsNotNull(surface.FocusPreviewSource);
        Assert.IsFalse(PageNodes(widget, host.CurrentSnapshot.Root).Any(node => node.Text == "Secret description"));
        using var preview = await host.AcquirePreviewAsync(surface.Id, PlayniteAchievementEntry.KeyFor(widget.RenderState.Value.DetailsExtras.Achievements!.Items[0]));
        var nodes = Nodes(preview.Range.Items.Single().Root).ToArray();
        Assert.IsFalse(nodes.Any(node => node.Text is "Secret name" or "Secret description"));
        Assert.IsTrue(nodes.Any(node => node.Text == "Secret achievement"));
        Assert.IsFalse(nodes.Any(node => node.IsFocusable));
        Assert.IsFalse(nodes.Any(node => node.ArtworkHandle is not null || node.ImageSource is not null || node.Kind == ViewNodeKind.Progress));
        Assert.IsNull(await preview.ResolveArtworkSourceAsync(preview.Range.Items.Single().Key,
            PlayniteAchievementBrowse.IconHandle(PlayniteAchievementEntry.KeyFor(widget.RenderState.Value.DetailsExtras.Achievements!.Items[0]))));
        await Background(widget);
    }

    [TestMethod]
    public async Task AchievementIconBelongsOnlyToItsCapturedPreviewAndShowsProgress()
    {
        var provider = new FakeHost(1);
        var widget = Create(provider, out TestApplicationService application);
        application.AchievementsHandler = (_, _) => ValueTask.FromResult(new PlayniteAchievements(true, 1, 0,
            [new("Steps", "Complete ten steps", false, null, 12.5, 20, false)
                { Icon = @"C:\cache\steps.png", Progress = new(3, 10) }])
                { Provider = PlayniteAchievementProvider.PlayniteAchievements });
        await Interactive(widget);
        await Ready(widget, provider);
        var game = PageNodes(widget, Snapshot(widget, 150_010).Root).First(node => node.ActionId == PlayniteLibraryActions.DetailsOpen);
        await widget.OnActionAsync(new(PlayniteLibraryActions.DetailsOpen, game.Id));
        await widget.OnActionAsync(new(DetailAction + "tab.achievements", "tab"));
        await Bounded(widget.WhenDetailsIdleAsync(), "achievements");
        using var host = WidgetTestHost.CreateIndexedCollectionHost(widget, "achievement-icon");
        var surface = PageNodes(widget, host.CurrentSnapshot.Root).Single(node => node.Id == DetailAction + "preview-surface");
        Assert.IsTrue(PageNodes(widget, host.CurrentSnapshot.Root).Any(node => node.Text == "PlayniteAchievements"));
        using var preview = await host.AcquirePreviewAsync(surface.Id, PlayniteAchievementEntry.KeyFor(widget.RenderState.Value.DetailsExtras.Achievements!.Items[0]));
        var row = preview.Range.Items.Single();
        var nodes = Nodes(row.Root).ToArray();
        var handle = PlayniteAchievementBrowse.IconHandle(PlayniteAchievementEntry.KeyFor(widget.RenderState.Value.DetailsExtras.Achievements!.Items[0]));
        Assert.IsTrue(nodes.Any(node => node.ArtworkHandle == handle.Value));
        Assert.IsTrue(nodes.Any(node => node.Kind == ViewNodeKind.Progress));
        Assert.IsFalse(nodes.Any(node => node.IsFocusable || node.ImageSource?.Contains("C:") == true));
        var artwork = await preview.ResolveArtworkSourceAsync(row.Key, handle) as WidgetFileArtwork;
        Assert.IsNotNull(artwork);
        Assert.AreEqual(@"C:\cache\steps.png", artwork.Path);
        Assert.IsNull(await preview.ResolveArtworkSourceAsync(row.Key, new WidgetArtworkHandle("forged-icon")));
        await widget.OnActionAsync(new(PlayniteLibraryActions.DetailsClose, "close"));
        host.PublishSnapshot();
        Assert.IsNull(await preview.ResolveArtworkSourceAsync(row.Key, handle));
        await Background(widget);
    }
}
