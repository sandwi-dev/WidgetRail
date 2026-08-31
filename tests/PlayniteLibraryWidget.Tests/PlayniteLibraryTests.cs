using System.Text.Json;
using System.Text;
using System.Globalization;
using WidgetRail.Samples.PlayniteLibrary;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using LauncherWidget = WidgetRail.Samples.PlayniteLibrary.PlayniteLibraryWidget;

namespace WidgetRail.Tests.PlayniteLibrary;

[TestClass]
public sealed class PlayniteLibraryTests
{
    [TestMethod, Timeout(30_000)]
    public async Task InteractiveLifecycleRepublishesActionablePlayniteControls()
    {
        var host = new FakeHost(0);
        using var playnite = new FakeConnectionClient();
        var services = host.Services();
        var widget = WidgetTestHost.Attach(
            new LauncherWidget(new TestApplicationService(services, host), playnite), services);

        await Visible(widget);
        await Ready(widget, host);
        var visible = Snapshot(widget, 1);
        var visibleConnect = Nodes(visible.Root).Single(node =>
            node.ActionId == LauncherWidget.PlayniteOpenActionId);
        Assert.IsTrue(visibleConnect.IsDisabled,
            "Visible preview must keep Playnite connection inert.");
        Assert.IsFalse(await Route(widget, visible, ControllerButton.A, visibleConnect.Id),
            "The disabled preview control must not admit A.");

        var invalidations = 0;
        widget.Invalidated += (_, _) => invalidations++;
        await Interactive(widget);
        Assert.AreEqual(1, invalidations,
            "Entering Interactive must publish the newly actionable controls once.");

        var interactive = Snapshot(widget, 2);
        var interactiveConnect = Nodes(interactive.Root).Single(node =>
            node.ActionId == LauncherWidget.PlayniteOpenActionId);
        Assert.IsTrue(interactiveConnect.IsDisabled != true);
        Assert.IsTrue(await Route(widget, interactive, ControllerButton.A,
            interactiveConnect.Id), "The fresh Interactive snapshot must admit A.");
        await WaitUntil(() => playnite.ProbeCalls == 1);
        Assert.AreEqual(1, playnite.ProbeCalls);
        var connection = Snapshot(widget, 3);
        Assert.IsTrue(Nodes(connection.Root).Any(node =>
            node.Id == "playnite-library.playnite.root"));

        invalidations = 0;
        await Visible(widget);
        Assert.AreEqual(1, invalidations,
            "Leaving Interactive for Visible must republish inert controls once.");
        var returnedVisible = Snapshot(widget, 4);
        Assert.IsTrue(Nodes(returnedVisible.Root)
            .Where(node => node.ActionId is not null)
            .All(node => node.IsDisabled == true),
            "Every action on the visible connection preview must remain inert.");

        invalidations = 0;
        await Visible(widget);
        Assert.AreEqual(0, invalidations,
            "A repeated stable lifecycle state must not churn publications.");
        await Background(widget);
    }

    [TestMethod, Timeout(30_000)]
    public async Task ExactTitleOverrideChangesPresentationAndSearchButNeverLaunchIdentity()
    {
        var privateState = new WidgetTestPrivateState();
        var host = new FakeHost(3, privateState);
        var widget = Create(host);
        await Interactive(widget);
        await Ready(widget, host);
        var library = Snapshot(widget, 1);
        var game = Nodes(library.Root).Single(node =>
            node.ActionId == "playnite-library.launch" &&
            (node.AccessibilityLabel ?? string.Empty).Contains(
                "Game 00001", StringComparison.Ordinal));

        await widget.OnActionAsync(new(PlayniteLibraryActionSheet.OpenAction, game.Id));
        var sheet = Snapshot(widget, 2);
        var edit = Nodes(sheet.Root).Single(node =>
            node.ActionId == PlayniteLibraryActionSheet.EditTitleAction);
        await widget.OnActionAsync(new(edit.ActionId!, edit.Id));
        var editor = Snapshot(widget, 3);
        Assert.AreEqual(PlayniteLibraryTitleEditor.ScopeId, editor.ActiveInputScopeId);
        Assert.AreEqual(ViewNodeKind.TextEntry, Nodes(editor.Root).Single(node =>
            node.Id == PlayniteLibraryTitleEditor.EntryId).Kind);
        await widget.OnActionAsync(new WidgetActionEvent(
            PlayniteLibraryTitleEditor.CommitAction, PlayniteLibraryTitleEditor.EntryId)
            { CommittedText = "  Couch   Champion  " });
        await Bounded(widget.WhenLibraryIdleAsync(), "title projection refresh");
        Assert.AreEqual("Couch Champion", widget.Organization.TitleOverrides.Single().Title);
        Assert.AreEqual("Game 00001", widget.Organization.Items.Single(item =>
            item.SavedId == "saved-00001").DisplayName);

        await widget.OnActionAsync(new(PlayniteLibraryTitleEditor.CloseAction,
            PlayniteLibraryTitleEditor.EntryId));
        await widget.OnActionAsync(new(PlayniteLibraryActionSheet.CloseAction,
            PlayniteLibraryActionSheet.InitialFocusId));
        library = Snapshot(widget, 4);
        var renamed = Nodes(library.Root).Single(node =>
            node.ActionId == "playnite-library.launch" &&
            (node.AccessibilityLabel ?? string.Empty).Contains(
                "Couch Champion", StringComparison.Ordinal));
        Assert.IsFalse((renamed.AccessibilityLabel ?? string.Empty).Contains(
            "Game 00000", StringComparison.Ordinal));

        await widget.OnActionAsync(new WidgetActionEvent(
            "playnite-library.search.commit", "playnite-library.search")
            { CommittedText = "Couch Champion" });
        await Bounded(widget.WhenLibraryIdleAsync(), "override-only search");
        var searched = Snapshot(widget, 5);
        renamed = Nodes(searched.Root).Single(node =>
            node.ActionId == "playnite-library.launch");
        await widget.OnActionAsync(new("playnite-library.launch", renamed.Id));
        CollectionAssert.AreEqual(new[] { "saved-00001" },
            host.ResolveRequests[^1].ToArray());
        CollectionAssert.AreEqual(new[] { "app-00001" }, host.Launches.ToArray());
        await Background(widget);

        var restartedHost = new FakeHost(3, privateState)
        {
            ItemFactory = index => WithPresentation(Item(index),
                displayName: $"Provider {index:D5}"),
        };
        var restarted = Create(restartedHost);
        await Interactive(restarted);
        await Ready(restarted, restartedHost);
        var restartedView = Snapshot(restarted, 6);
        var retained = Nodes(restartedView.Root).Single(node =>
            node.ActionId == "playnite-library.launch" &&
            (node.AccessibilityLabel ?? string.Empty).Contains(
                "Couch Champion", StringComparison.Ordinal));
        await restarted.OnActionAsync(new(PlayniteLibraryActionSheet.OpenAction, retained.Id));
        sheet = Snapshot(restarted, 7);
        edit = Nodes(sheet.Root).Single(node =>
            node.ActionId == PlayniteLibraryActionSheet.EditTitleAction);
        await restarted.OnActionAsync(new(edit.ActionId!, edit.Id));
        await restarted.OnActionAsync(new(PlayniteLibraryTitleEditor.ResetAction,
            PlayniteLibraryTitleEditor.ResetAction));
        await Bounded(restarted.WhenLibraryIdleAsync(), "title reset refresh");
        Assert.AreEqual(0, restarted.Organization.TitleOverrides.Count);
        Assert.AreEqual("Provider 00001", restarted.Organization.Items.Single(item =>
            item.SavedId == "saved-00001").DisplayName);
        await restarted.OnActionAsync(new(PlayniteLibraryTitleEditor.CloseAction,
            PlayniteLibraryTitleEditor.EntryId));
        await restarted.OnActionAsync(new(PlayniteLibraryActionSheet.CloseAction,
            PlayniteLibraryActionSheet.InitialFocusId));
        Assert.IsTrue(Nodes(Snapshot(restarted, 8).Root).Any(node =>
            node.ActionId == "playnite-library.launch" &&
            (node.AccessibilityLabel ?? string.Empty).Contains(
                "Provider 00001", StringComparison.Ordinal)));
        await Background(restarted);
    }

    [TestMethod]
    public void TitlePolicyIsBoundedExactAndCasReplaysOnlyRequestedSavedId()
    {
        var a = new PlayniteLibraryDisplayItem("saved-a", "Provider A", "Steam");
        var b = new PlayniteLibraryDisplayItem("saved-b", "Provider B", "Windows");
        var baseline = PlayniteLibraryPrivateState.Empty with { Items = [a, b] };
        var set = PlayniteLibraryTitlePolicy.Set(baseline, a, "  Custom   A ");
        Assert.IsTrue(set.Accepted);
        Assert.AreEqual("Custom A", set.State.TitleOverrides.Single().Title);
        Assert.AreEqual("Provider A", set.State.Items.Single(item =>
            item.SavedId == a.SavedId).DisplayName);
        Assert.AreEqual("Provider B", PlayniteLibraryTitlePolicy.DisplayName(
            set.State, b.SavedId, b.DisplayName));

        var boundedItems = Enumerable.Range(0, 257)
            .Select(index => new PlayniteLibraryDisplayItem(
                $"saved-title-{index:D3}", $"Provider {index:D3}", "Local"))
            .ToArray();
        var bounded = new PlayniteLibraryPrivateState(
            PlayniteLibraryPrivateState.CurrentVersion, boundedItems)
        {
            TitleOverrides = boundedItems.Select((item, index) =>
                new PlayniteLibraryTitleOverride(item.SavedId,
                    $"Custom {index:D3}")).ToArray(),
        };
        var normalizedBounded = PlayniteLibraryOrganizationPolicy.Normalize(bounded);
        Assert.AreEqual(257, normalizedBounded.TitleOverrides.Count,
            "The 257th ordinary title was rejected before the byte boundary.");
        Assert.IsLessThanOrEqualTo(
            WidgetCommunityPlatformLimits.MaximumPrivateStateUtf8Bytes,
            JsonSerializer.SerializeToUtf8Bytes(normalizedBounded).Length);

        var related = baseline with
        {
            FavoriteSavedIds = [a.SavedId],
            VariantGroups = [new("variant." + new string('a', 20),
                [a.SavedId, b.SavedId], a.SavedId)],
            RecentSavedIds = [a.SavedId],
            ManualSavedIds = [b.SavedId],
            ExcludedSavedIds = [b.SavedId],
            Categories = [new("category." + new string('b', 32), "Favorites",
                [a.SavedId, b.SavedId])],
        };
        AssertOnlyTitlesReset(related with
        {
            TitleOverrides = [new(a.SavedId, new string('X', 97))],
        });
        AssertOnlyTitlesReset(related with
        {
            TitleOverrides = [new(a.SavedId, "One"), new(a.SavedId, "Two")],
        });
        AssertOnlyTitlesReset(related with
        {
            TitleOverrides = [new("saved-missing", "Missing")],
        });

        var byteBounded = PlayniteLibraryPrivateState.Empty;
        PlayniteLibraryStateMutation? rejected = null;
        for (var index = 0;
             index < PlayniteLibraryPrivateState.MaximumTitleValidationItems;
             index++)
        {
            var display = new PlayniteLibraryDisplayItem(
                $"saved-byte-{index:D4}", $"Provider {index:D4}", "Local");
            var mutation = PlayniteLibraryTitlePolicy.Set(
                byteBounded, display, new string((char)('A' + index % 26), 96));
            if (!mutation.Accepted)
            {
                rejected = mutation;
                break;
            }
            byteBounded = mutation.State;
        }
        Assert.IsGreaterThanOrEqualTo(257, byteBounded.TitleOverrides.Count);
        Assert.IsNotNull(rejected, "The encoded byte boundary was not enforced.");
        CollectionAssert.AreEqual(
            JsonSerializer.SerializeToUtf8Bytes(byteBounded),
            JsonSerializer.SerializeToUtf8Bytes(rejected!.State),
            "Byte-budget rejection changed the committed state.");

        PlayniteLibraryPrivateState? written = null;
        var writes = 0;
        var result = PlayniteLibraryStateStore.SaveAsync(
            state => PlayniteLibraryTitlePolicy.Set(state, a, "CAS title"),
            (candidate, _, _) =>
            {
                if (++writes == 1)
                    return ValueTask.FromException<WidgetPrivateStateMutation>(
                        new WidgetCapabilityException("state_conflict", "conflict"));
                written = candidate;
                return ValueTask.FromResult(new WidgetPrivateStateMutation(3));
            },
            _ => ValueTask.FromResult(new WidgetPrivateStateValue<PlayniteLibraryPrivateState>(
                true, baseline with { FavoriteSavedIds = [b.SavedId] }, 2)),
            baseline, 1, default).GetAwaiter().GetResult();
        Assert.IsTrue(result.Saved);
        Assert.AreEqual("CAS title", written!.TitleOverrides.Single().Title);
        CollectionAssert.AreEqual(new[] { b.SavedId }, written.FavoriteSavedIds.ToArray());

        static void AssertOnlyTitlesReset(PlayniteLibraryPrivateState state)
        {
            var normalized = PlayniteLibraryOrganizationPolicy.Normalize(state);
            Assert.AreEqual(0, normalized.TitleOverrides.Count);
            CollectionAssert.AreEqual(state.FavoriteSavedIds.ToArray(),
                normalized.FavoriteSavedIds.ToArray());
            CollectionAssert.AreEqual(state.RecentSavedIds.ToArray(),
                normalized.RecentSavedIds.ToArray());
            CollectionAssert.AreEqual(state.ManualSavedIds.ToArray(),
                normalized.ManualSavedIds.ToArray());
            CollectionAssert.AreEqual(state.ExcludedSavedIds.ToArray(),
                normalized.ExcludedSavedIds.ToArray());
            Assert.AreEqual(state.VariantGroups.Count, normalized.VariantGroups.Count);
            Assert.AreEqual(state.Categories.Count, normalized.Categories.Count);
        }
    }

    [TestMethod]
    public void CategoryCapacityIsGovernedByBytesBeforePracticalSafetyCeilings()
    {
        var items = Enumerable.Range(0, 32)
            .Select(index => new PlayniteLibraryDisplayItem(
                $"saved-{index:D3}", $"Game {index:D2}", "Local"))
            .ToArray();
        var categories = Enumerable.Range(0, 32)
            .Select(index => new PlayniteLibraryCategory(
                "category." + index.ToString("x32"),
                $"Collection {index:D2}",
                Enumerable.Range(0, 8)
                    .Select(offset => items[(index + offset) % items.Length].SavedId)
                    .ToArray()))
            .ToArray();
        var state = new PlayniteLibraryPrivateState(
            PlayniteLibraryPrivateState.CurrentVersion, items)
        {
            Categories = categories,
        };

        var normalized = PlayniteLibraryOrganizationPolicy.Normalize(state);
        Assert.AreEqual(32, normalized.Categories.Count);
        Assert.AreEqual(256, normalized.Categories.Sum(category =>
            category.SavedIds.Count));
        Assert.IsLessThanOrEqualTo(
            WidgetCommunityPlatformLimits.MaximumPrivateStateUtf8Bytes,
            JsonSerializer.SerializeToUtf8Bytes(normalized).Length);

        var expanded = PlayniteLibraryCategoryPolicy.SetMembership(
            normalized, normalized.Categories[0].Id, items[31], included: true);
        Assert.IsTrue(expanded.Accepted,
            "An ordinary ninth membership was rejected by a prototype count cap.");
        Assert.AreEqual(9, expanded.State.Categories[0].SavedIds.Count);

        var sheet = PlayniteLibraryActionSheet.Render(new PlayniteLibraryDetailsState(
            new("saved-000", PlayniteLibraryIdentity.Key("saved-000"),
                PlayniteLibraryIdentity.FocusId("grid", PlayniteLibraryIdentity.Key("saved-000")),
                "Game 00", "Local", false),
            "Game 00", "Local", "Available", "Ready", false, false, 0, "Ready",
            "Choose another variant", true, true, true, true, false), categories)
            .CreateSnapshot("capacity", 1);
        Assert.AreEqual(UI.MaximumActionSheetItems,
            Nodes(sheet.Root).Count(node => node.ActionId is not null));

        var oversizedItems = Enumerable.Range(0, 128)
            .Select(index => new PlayniteLibraryDisplayItem(
                $"saved-{index:D3}-" + new string('s', 114),
                new string('N', 96), new string('S', 64)))
            .ToArray();
        var organized = oversizedItems.Take(
            PlayniteLibraryPrivateState.MaximumOrganizedItems).ToArray();
        var oversizedBase = new PlayniteLibraryPrivateState(
            PlayniteLibraryPrivateState.CurrentVersion, oversizedItems)
        {
            FavoriteSavedIds = organized.Select(item => item.SavedId).ToArray(),
            VariantGroups = organized.Chunk(PlayniteLibraryPrivateState.MaximumVariantsPerGroup)
                .Select((members, index) => new PlayniteLibraryVariantGroup(
                    "variant." + index.ToString("x20"),
                    members.Select(item => item.SavedId).ToArray(),
                    members[^1].SavedId))
                .ToArray(),
            RecentSavedIds = oversizedItems.Skip(
                    PlayniteLibraryPrivateState.MaximumOrganizedItems)
                .Take(PlayniteLibraryPrivateState.MaximumRecentItems)
                .Select(item => item.SavedId).ToArray(),
            ManualSavedIds = oversizedItems.Skip(
                    PlayniteLibraryPrivateState.MaximumOrganizedItems +
                    PlayniteLibraryPrivateState.MaximumRecentItems)
                .Take(PlayniteLibraryPrivateState.MaximumManualItems)
                .Select(item => item.SavedId).ToArray(),
            ExcludedSavedIds = oversizedItems.Skip(
                    PlayniteLibraryPrivateState.MaximumOrganizedItems +
                    PlayniteLibraryPrivateState.MaximumRecentItems +
                    PlayniteLibraryPrivateState.MaximumManualItems)
                .Take(PlayniteLibraryPrivateState.MaximumExcludedItems)
                .Select(item => item.SavedId).ToArray(),
        };
        var rejected = PlayniteLibraryCategoryPolicy.Create(
            oversizedBase, PlayniteLibraryCategoryPolicy.NewId(), "Byte boundary");
        Assert.IsFalse(rejected.Accepted);
        Assert.AreEqual(0, rejected.State.Categories.Count);
        Assert.AreEqual(oversizedBase.Items.Count, rejected.State.Items.Count);
    }

    [TestMethod, Timeout(30_000)]
    public async Task CollectionTriggersWrapPreserveExactFocusAndRespectModalOwnership()
    {
        var displays = Enumerable.Range(0, 3)
            .Select(index => new PlayniteLibraryDisplayItem(
                $"saved-{index:D5}", $"Game {index:D5}", "Conformance"))
            .ToArray();
        var categories = new[]
        {
            new PlayniteLibraryCategory(
                "category." + 1.ToString("x32"), "Alpha",
                [displays[0].SavedId, displays[1].SavedId]),
            new PlayniteLibraryCategory(
                "category." + 2.ToString("x32"), "Beta", [displays[1].SavedId]),
            new PlayniteLibraryCategory(
                "category." + 3.ToString("x32"), "Empty", []),
        };
        var persisted = new PlayniteLibraryPrivateState(
            PlayniteLibraryPrivateState.CurrentVersion, displays)
        {
            Categories = categories,
        };
        var host = new FakeHost(3, new WidgetTestPrivateState(
            JsonSerializer.Serialize(persisted), 1));
        var widget = Create(host);
        await Interactive(widget);
        await Ready(widget, host);

        var library = Snapshot(widget, 30);
        var selected = Nodes(library.Root).Single(node =>
            node.ActionId == "playnite-library.launch" &&
            (node.AccessibilityLabel ?? string.Empty).Contains(
                "Game 00001", StringComparison.Ordinal));
        Assert.IsTrue(await Route(widget, library, ControllerButton.RightTrigger,
            selected.Id));
        await WaitUntil(() => Nodes(Snapshot(widget, 300).Root).Any(node =>
            node.Id == "playnite-library.title" && node.Text == "Alpha"));
        await Bounded(widget.WhenLibraryIdleAsync(), "first category switch");
        var alpha = Snapshot(widget, 31);
        Assert.AreEqual("Alpha", Nodes(alpha.Root).Single(node =>
            node.Id == "playnite-library.title").Text);
        Assert.AreEqual(selected.Id, alpha.InitialFocusId);

        Assert.IsTrue(await Route(widget, alpha, ControllerButton.RightTrigger,
            selected.Id));
        await WaitUntil(() => Nodes(Snapshot(widget, 301).Root).Any(node =>
            node.Id == "playnite-library.title" && node.Text == "Beta"));
        await Bounded(widget.WhenLibraryIdleAsync(), "second category switch");
        var beta = Snapshot(widget, 32);
        Assert.AreEqual("Beta", Nodes(beta.Root).Single(node =>
            node.Id == "playnite-library.title").Text);
        Assert.AreEqual(selected.Id, beta.InitialFocusId);

        var currentBetaTile = Nodes(beta.Root).Single(node =>
            node.ActionId == "playnite-library.launch" &&
            (node.AccessibilityLabel ?? string.Empty).Contains(
                "Game 00001", StringComparison.Ordinal));
        await widget.OnActionAsync(new(
            PlayniteLibraryActionSheet.OpenAction, currentBetaTile.Id));
        var sheet = Snapshot(widget, 33);
        Assert.IsFalse(await Route(widget, sheet, ControllerButton.RightTrigger,
            PlayniteLibraryActionSheet.InitialFocusId));
        Assert.AreEqual(PlayniteLibraryActionSheet.ScopeId, Snapshot(widget, 34).ActiveInputScopeId);
        await widget.OnActionAsync(new(PlayniteLibraryActionSheet.CloseAction,
            PlayniteLibraryActionSheet.InitialFocusId));

        beta = Snapshot(widget, 35);
        currentBetaTile = Nodes(beta.Root).Single(node =>
            node.ActionId == "playnite-library.launch" &&
            (node.AccessibilityLabel ?? string.Empty).Contains(
                "Game 00001", StringComparison.Ordinal));
        Assert.IsTrue(await Route(
            widget, beta, ControllerButton.RightTrigger, currentBetaTile.Id));
        await WaitUntil(() => Nodes(Snapshot(widget, 302).Root).Any(node =>
            node.Id == "playnite-library.title" && node.Text == "Empty"));
        await Bounded(widget.WhenLibraryIdleAsync(), "empty category switch");
        var empty = Snapshot(widget, 36);
        Assert.AreEqual("Empty", Nodes(empty.Root).Single(node =>
            node.Id == "playnite-library.title").Text);
        Assert.AreEqual("playnite-library.category.empty.action", empty.InitialFocusId);

        Assert.IsTrue(await Route(widget, empty, ControllerButton.RightTrigger,
            "playnite-library.category.empty.action"));
        await WaitUntil(() => Nodes(Snapshot(widget, 303).Root).Any(node =>
            node.Id == "playnite-library.title" && node.Text == "Playnite Library"));
        await Bounded(widget.WhenLibraryIdleAsync(), "wrap to all games");
        library = Snapshot(widget, 37);
        Assert.AreEqual("Playnite Library", Nodes(library.Root).Single(node =>
            node.Id == "playnite-library.title").Text);
        Assert.IsFalse(await Route(widget, library, ControllerButton.RightTrigger,
            "playnite-library.search"), "Search TextEntry leaked LT/RT collection input.");

        Assert.IsTrue(await Route(widget, library, ControllerButton.LeftTrigger,
            selected.Id));
        await WaitUntil(() => Nodes(Snapshot(widget, 304).Root).Any(node =>
            node.Id == "playnite-library.title" && node.Text == "Empty"));
        await Bounded(widget.WhenLibraryIdleAsync(), "reverse wrap to empty");
        empty = Snapshot(widget, 38);
        Assert.AreEqual("Empty", Nodes(empty.Root).Single(node =>
            node.Id == "playnite-library.title").Text);
        await Background(widget);
    }

    [TestMethod, Timeout(30_000)]
    public async Task CategoriesCreateAssignBrowseRestartAndRevalidatePlayniteOwnership()
    {
        var state = new WidgetTestPrivateState();
        var host = new FakeHost(3, state);
        var widget = Create(host);
        await Interactive(widget);
        await Ready(widget, host);
        var tiles = Nodes(Snapshot(widget, 10).Root).Where(node =>
            node.ActionId == "playnite-library.launch").ToArray();

        await widget.OnActionAsync(new(PlayniteLibraryActionSheet.OpenAction, tiles[1].Id));
        await widget.OnActionAsync(new(PlayniteLibraryActionSheet.ManageCategoriesAction,
            "playnite-library.actions.categories"));
        var categories = Snapshot(widget, 11);
        Assert.AreEqual(ViewNodeKind.TextEntry, Nodes(categories.Root).Single(node =>
            node.Id == "playnite-library.category.create").Kind);
        await widget.OnActionAsync(new WidgetActionEvent(
            "playnite-library.category.create", "playnite-library.category.create")
            { CommittedText = "  Co-op   Night  " });
        var created = host.Authority.Categories.Single();
        Assert.AreEqual("Co-op Night", created.Name);

        await widget.OnActionAsync(new("playnite-library.categories.back",
            "playnite-library.categories.all-games"));
        await Bounded(widget.WhenLibraryIdleAsync(), "category return reload");
        var returnedLibrary = Snapshot(widget, 12);
        var current = Nodes(returnedLibrary.Root).Single(node =>
            node.ActionId == "playnite-library.launch" &&
            (node.AccessibilityLabel ?? string.Empty).Contains(
                "Game 00001", StringComparison.Ordinal));
        await widget.OnActionAsync(new(PlayniteLibraryActionSheet.OpenAction, current.Id));
        var membershipAction = PlayniteLibraryActionSheet.CategoryActionPrefix + created.Id;
        Assert.AreEqual("Add to Co-op Night", Nodes(Snapshot(widget, 13).Root).Single(node =>
            node.Id == membershipAction).Text);
        await widget.OnActionAsync(new(membershipAction, membershipAction));
        CollectionAssert.AreEqual(new[] { "saved-00001" },
            host.Authority.Categories.Single().SavedIds.ToArray());

        await widget.OnActionAsync(new(PlayniteLibraryActionSheet.CloseAction,
            PlayniteLibraryActionSheet.InitialFocusId));
        await widget.OnActionAsync(new("playnite-library.categories.open",
            "playnite-library.categories.open"));
        await widget.OnActionAsync(new("playnite-library.category.open." + created.Id,
            "playnite-library.category.open-button." + created.Id));
        await Bounded(widget.WhenLibraryIdleAsync(), "category browse load");
        var category = Snapshot(widget, 14);
        var categoryNodes = Nodes(category.Root).ToArray();
        Assert.IsTrue(categoryNodes.Any(node => node.ActionId == "playnite-library.launch"),
            "Category route omitted its exact member: " + string.Join(" | ",
                categoryNodes.Select(node => node.Text).Where(text => text is not null)));
        var categoryTile = categoryNodes.Single(node =>
            node.ActionId == "playnite-library.launch");
        StringAssert.Contains(categoryTile.AccessibilityLabel!, "Game 00001");
        await widget.OnActionAsync(new("playnite-library.launch", categoryTile.Id));
        CollectionAssert.AreEqual(new[] { "saved-00001" }, host.ResolveRequests[^1].ToArray());
        CollectionAssert.AreEqual(new[] { "app-00001" }, host.Launches.ToArray());

        await Background(widget);
        var restartedHost = new FakeHost(3, state);
        var restarted = Create(restartedHost);
        await Interactive(restarted);
        await Ready(restarted, restartedHost);
        Assert.AreEqual("Co-op Night", restartedHost.Authority.Categories.Single().Name);
        CollectionAssert.AreEqual(new[] { "saved-00001" },
            restartedHost.Authority.Categories.Single().SavedIds.ToArray());
        await restarted.OnActionAsync(new("playnite-library.categories.open",
            "playnite-library.categories.open"));
        var managed = Snapshot(restarted, 15);
        Assert.IsFalse(Nodes(managed.Root).Any(node =>
            node.ActionId?.StartsWith("playnite-library.category.rename.",
                StringComparison.Ordinal) == true));
        Assert.IsFalse(Nodes(managed.Root).Any(node =>
            node.ActionId?.StartsWith("playnite-library.category.delete.",
                StringComparison.Ordinal) == true));
        await restarted.OnActionAsync(new WidgetActionEvent(
            "playnite-library.category.create", "playnite-library.category.create")
            { CommittedText = " co-op night " });
        Assert.AreEqual(1, restartedHost.Authority.Categories.Count);
        StringAssert.Contains(Nodes(Snapshot(restarted, 16).Root).Single(node =>
            node.Id == "playnite-library.status").Text!, "Category was not created");
        CollectionAssert.AreEqual(new[] { "saved-00001" },
            restartedHost.Authority.Categories.Single().SavedIds.ToArray());
        await Background(restarted);
    }

    [TestMethod]
    public void CategoryPolicyBoundsResetOnlyCategoriesAndCasReplayExactDelta()
    {
        var a = new PlayniteLibraryDisplayItem("saved-a", "A", "Steam");
        var b = new PlayniteLibraryDisplayItem("saved-b", "B", "Windows");
        var favorite = new[] { a.SavedId };
        var state = new PlayniteLibraryPrivateState(
            PlayniteLibraryPrivateState.CurrentVersion, [a, b])
        {
            FavoriteSavedIds = favorite,
            Categories = Enumerable.Range(0, PlayniteLibraryPrivateState.MaximumCategories + 1)
                .Select(_ => new PlayniteLibraryCategory(
                    PlayniteLibraryCategoryPolicy.NewId(), "Category " + Guid.NewGuid().ToString("N")[..8],
                    Array.Empty<string>()))
                .ToArray(),
        };
        var normalized = PlayniteLibraryOrganizationPolicy.Normalize(state);
        Assert.AreEqual(0, normalized.Categories.Count);
        CollectionAssert.AreEqual(favorite, normalized.FavoriteSavedIds.ToArray());
        Assert.AreEqual(2, normalized.Items.Count);

        var valid = PlayniteLibraryOrganizationPolicy.Normalize(state with
        {
            Categories = [new(PlayniteLibraryCategoryPolicy.NewId(), "Arcade", [a.SavedId])],
        });
        var json = JsonSerializer.Serialize(valid);
        Assert.IsTrue(json.Length < 64 * 1024,
            "Bounded category state exceeded the private-state limit.");

        var baseline = PlayniteLibraryPrivateState.Empty with
        {
            Items = [a, b],
            FavoriteSavedIds = [a.SavedId],
            Categories = [new(PlayniteLibraryCategoryPolicy.NewId(), "Arcade", [a.SavedId])],
        };
        var concurrent = baseline with
        {
            FavoriteSavedIds = [a.SavedId, b.SavedId],
        };
        PlayniteLibraryPrivateState? written = null;
        var writes = 0;
        var result = PlayniteLibraryStateStore.SaveAsync(
            candidate => PlayniteLibraryCategoryPolicy.SetMembership(
                candidate, candidate.Categories[0].Id, b, included: true),
            (candidate, _, _) =>
            {
                writes++;
                if (writes == 1)
                    return ValueTask.FromException<WidgetPrivateStateMutation>(
                        new WidgetCapabilityException("state_conflict", "conflict"));
                written = candidate;
                return ValueTask.FromResult(new WidgetPrivateStateMutation(3));
            },
            _ => ValueTask.FromResult(new WidgetPrivateStateValue<PlayniteLibraryPrivateState>(
                true, concurrent, 2)),
            baseline, 1, default).GetAwaiter().GetResult();
        Assert.IsTrue(result.Saved);
        CollectionAssert.AreEqual(new[] { a.SavedId, b.SavedId },
            written!.FavoriteSavedIds.ToArray());
        CollectionAssert.AreEqual(new[] { a.SavedId, b.SavedId },
            written.Categories[0].SavedIds.ToArray());
    }

    [TestMethod, Timeout(30_000)]
    public async Task MissingCategoryMemberRetainsDisplayWithoutLaunchAuthority()
    {
        var display = new PlayniteLibraryDisplayItem(
            "saved-00001", "Temporarily missing", "Steam");
        var category = new PlayniteLibraryCategory(
            PlayniteLibraryCategoryPolicy.NewId(), "Offline", [display.SavedId]);
        var stored = new PlayniteLibraryPrivateState(
            PlayniteLibraryPrivateState.CurrentVersion, [display])
        {
            Categories = [category],
        };
        var host = new FakeHost(0, new WidgetTestPrivateState(
            JsonSerializer.Serialize(stored), 1));
        var widget = Create(host);
        await Interactive(widget);
        await Ready(widget, host);
        await widget.OnActionAsync(new("playnite-library.categories.open",
            "playnite-library.categories.open"));
        await widget.OnActionAsync(new("playnite-library.category.open." + category.Id,
            "playnite-library.category.open-button." + category.Id));
        await Bounded(widget.WhenLibraryIdleAsync(), "missing category member load");
        var snapshot = Snapshot(widget, 20);
        var tile = Nodes(snapshot.Root).Single(node =>
            node.ActionId == "playnite-library.launch");
        StringAssert.Contains(tile.AccessibilityLabel!, "Temporarily missing");
        StringAssert.Contains(tile.AccessibilityLabel!, "Play unavailable");
        Assert.IsTrue(tile.IsDisabled);
        await widget.OnActionAsync(new("playnite-library.launch", tile.Id));
        Assert.AreEqual(0, host.ResolveRequests.Count);
        Assert.AreEqual(0, host.Launches.Count);
        await Background(widget);
    }

    [TestMethod, Timeout(30_000)]
    public async Task UnavailableAndStaleResolvedRowsNeverAuthorizeLaunch()
    {
        foreach (var state in new[]
                 {
                     WidgetAppLibraryAvailabilityState.Unavailable,
                     WidgetAppLibraryAvailabilityState.StaleSource,
                 })
        {
            var host = new FakeHost(1);
            host.ResolveHandler = request => request.SavedIds.Select(_ =>
            {
                var current = host.ItemFactory(0);
                return current with
                {
                    Presentation = current.Presentation with
                    {
                        Availability = new(state, false,
                            state == WidgetAppLibraryAvailabilityState.Unavailable
                                ? "unavailable" : "stale"),
                        Capabilities = new([]),
                    },
                };
            }).ToArray();
            var widget = Create(host);
            await Interactive(widget);
            await Ready(widget, host);
            var tile = Nodes(Snapshot(widget, 1).Root).Single(node =>
                node.ActionId == "playnite-library.launch");

            await widget.OnActionAsync(new("playnite-library.launch", tile.Id));

            Assert.AreEqual(0, host.Launches.Count);
            await Background(widget);
        }
    }

    [TestMethod, Timeout(30_000)]
    public async Task EpicGameProjectsExactSourceAndLaunchIdentity()
    {
        var host = new FakeHost(1)
        {
            ItemFactory = _ => InstalledItem(
                "app-epic", "saved-epic", "Epic Game",
                WidgetAppLibraryKind.Game,
                "source-epic-installed", "Epic"),
        };
        host.ResolveHandler = _ => [host.ItemFactory(0)];
        var widget = Create(host);
        await Interactive(widget);
        await Ready(widget, host);

        var item = widget.Collection.Items.Single();
        Assert.AreEqual("Epic", item.Presentation.Source.DisplayName);
        var tile = Nodes(Snapshot(widget, 2).Root).Single(node =>
            node.ActionId == "playnite-library.launch");
        StringAssert.Contains(tile.AccessibilityLabel!, "Epic");
        await widget.OnActionAsync(new("playnite-library.launch", tile.Id));
        Assert.AreEqual("app-epic", host.Launches.Single());
        await Background(widget);
    }

    [TestMethod, Timeout(30_000)]
    public async Task GogGameProjectsExactSourceWithoutLaunchAuthority()
    {
        var host = new FakeHost(1)
        {
            ItemFactory = _ => InstalledItem(
                "app-gog", "saved-gog", "GOG Game",
                WidgetAppLibraryKind.Game,
                "source-gog-installed", "GOG") with
            {
                Presentation = InstalledItem(
                    "app-gog", "saved-gog", "GOG Game",
                    WidgetAppLibraryKind.Game,
                    "source-gog-installed", "GOG").Presentation with
                {
                    Availability = new(
                        WidgetAppLibraryAvailabilityState.Installed,
                        false, "play_unavailable"),
                    Capabilities = new([]),
                },
            },
        };
        host.ResolveHandler = _ => [host.ItemFactory(0)];
        var widget = Create(host);
        await Interactive(widget);
        await Ready(widget, host);

        var item = widget.Collection.Items.Single();
        Assert.AreEqual("GOG", item.Presentation.Source.DisplayName);
        var tile = Nodes(Snapshot(widget, 3).Root).Single(node =>
            node.ActionId == "playnite-library.launch");
        StringAssert.Contains(tile.AccessibilityLabel!, "GOG");
        StringAssert.Contains(tile.AccessibilityLabel!, "Play unavailable");
        Assert.IsTrue(tile.IsDisabled);
        await widget.OnActionAsync(new("playnite-library.launch", tile.Id));
        Assert.AreEqual(0, host.Launches.Count);
        await Background(widget);
    }

    [TestMethod, Timeout(30_000)]
    public async Task WindowsPackageGameProjectsExactSourceAndLaunchIdentity()
    {
        var host = new FakeHost(1)
        {
            ItemFactory = _ => InstalledItem(
                "app-xbox", "saved-xbox", "Package Game",
                WidgetAppLibraryKind.Game,
                "source-microsoft-games-installed", "Xbox / Microsoft Store"),
        };
        host.ResolveHandler = _ => [host.ItemFactory(0)];
        var widget = Create(host);
        await Interactive(widget);
        await Ready(widget, host);

        var item = widget.Collection.Items.Single();
        Assert.AreEqual("Xbox / Microsoft Store",
            item.Presentation.Source.DisplayName);
        var tile = Nodes(Snapshot(widget, 3).Root).Single(node =>
            node.ActionId == "playnite-library.launch");
        StringAssert.Contains(tile.AccessibilityLabel!, "Xbox / Microsoft Store");
        await widget.OnActionAsync(new("playnite-library.launch", tile.Id));
        Assert.AreEqual("app-xbox", host.Launches.Single());
        await Background(widget);
    }

    [TestMethod, Timeout(30_000)]
    public async Task QueryControlsMapExactBoundedCriteriaAndClear()
    {
        var persisted = new PlayniteLibraryPrivateState(
            PlayniteLibraryPrivateState.CurrentVersion,
            [new("saved-00000", "Game 00000", "Steam")])
        {
            FavoriteSavedIds = ["saved-00000"],
        };
        var host = new FakeHost(2, new WidgetTestPrivateState(
            JsonSerializer.Serialize(persisted), 1));
        var widget = Create(host);
        await Interactive(widget);
        await Ready(widget, host);
        await Bounded(widget.WhenWarmStateIdleAsync(), "query organization load");

        await widget.OnActionAsync(new(
            "playnite-library.filter.favorites", "playnite-library.filter.favorites"));
        await Bounded(widget.WhenLibraryIdleAsync(), "favorite query");
        CollectionAssert.AreEqual(new[] { "saved-00000" },
            host.Queries[^1].Query.FavoriteSavedIds.ToArray());

        await widget.OnActionAsync(new(
            "playnite-library.filter.source", "playnite-library.filter.source"));
        await Bounded(widget.WhenLibraryIdleAsync(), "source query");
        Assert.AreEqual("Steam", host.Queries[^1].Query.SourceAttribution);

        await widget.OnActionAsync(new(
            "playnite-library.filter.sort", "playnite-library.filter.sort"));
        await Bounded(widget.WhenLibraryIdleAsync(), "sort query");
        Assert.AreEqual(WidgetAppLibrarySortOrder.DisplayNameDescending,
            host.Queries[^1].Query.Sort);

        await widget.OnActionAsync(new(
            "playnite-library.query.clear", "playnite-library.query.clear"));
        await Bounded(widget.WhenLibraryIdleAsync(), "cleared query");
        var cleared = host.Queries[^1].Query;
        Assert.IsNull(cleared.SearchText);
        Assert.IsNull(cleared.SourceAttribution);
        CollectionAssert.AreEqual(new[] { "saved-00000" },
            cleared.FavoriteSavedIds.ToArray());
        Assert.AreEqual(WidgetAppLibrarySortOrder.DisplayName, cleared.Sort);
        await Background(widget);
    }

    [TestMethod, Timeout(30_000)]
    public async Task EveryTopControlKeepsPendingAndCommittedSnapshotsValid()
    {
        var displays = Enumerable.Range(0, 32)
            .Select(index => new PlayniteLibraryDisplayItem(
                $"saved-{index:D5}", $"Game {index:D5}",
                index % 2 == 0 ? "Steam" : "Windows"))
            .ToArray();
        var persisted = new PlayniteLibraryPrivateState(
            PlayniteLibraryPrivateState.CurrentVersion, displays)
        {
            FavoriteSavedIds = [displays[1].SavedId],
            RecentSavedIds = [displays[2].SavedId],
            ExcludedSavedIds = [displays[0].SavedId],
        };
        var host = new FakeHost(32, new WidgetTestPrivateState(
            JsonSerializer.Serialize(persisted), 1));
        var widget = Create(host);
        await Interactive(widget);
        await Ready(widget, host);
        await Bounded(widget.WhenWarmStateIdleAsync(), "top-control warm state");
        long sequence = 700;

        foreach (var actionId in new[]
                 {
                     "playnite-library.filter.favorites",
                     "playnite-library.filter.recent",
                     "playnite-library.filter.source",
                     "playnite-library.filter.sort",
                     "playnite-library.query.clear",
                 })
        {
            var started = new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource<WidgetAppLibraryPage>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var before = host.Queries.Count;
            host.QueryHandler = (request, token) =>
            {
                started.TrySetResult();
                return new(release.Task.WaitAsync(token));
            };

            await widget.OnActionAsync(new(actionId, actionId));
            await Bounded(started.Task, actionId + " admission");
            Assert.AreEqual(before + 1, host.Queries.Count,
                actionId + " must admit one replacement query.");
            AssertValidCollectionAnchor(Snapshot(widget, sequence++));

            release.TrySetResult(new(
                [Item(0), Item(1), Item(2)], null, null, actionId));
            await Bounded(widget.WhenLibraryIdleAsync(), actionId + " drain");
            AssertValidCollectionAnchor(Snapshot(widget, sequence++));
        }

        host.QueryHandler = null;
        await AssertRoute("playnite-library.add.open", PlayniteLibraryRoute.AddGames);
        await AssertRoute("playnite-library.running.open", PlayniteLibraryRoute.Running);
        await AssertRoute("playnite-library.categories.open", PlayniteLibraryRoute.Categories);
        await AssertRoute("playnite-library.hidden.open", PlayniteLibraryRoute.Hidden);
        await Background(widget);

        async Task AssertRoute(string actionId, PlayniteLibraryRoute expected)
        {
            var beforeQueries = host.Queries.Count;
            var beforeRunning = host.RunningObservationCount;
            await widget.OnActionAsync(new(actionId, actionId));
            await Bounded(widget.WhenLibraryIdleAsync(), actionId + " route load");
            var route = Snapshot(widget, sequence++);
            var expectedTitle = expected switch
            {
                PlayniteLibraryRoute.AddGames => "Add games",
                PlayniteLibraryRoute.Running => "Add running app",
                PlayniteLibraryRoute.Categories => "Categories",
                _ => "Hidden games",
            };
            Assert.AreEqual(expectedTitle, Nodes(route.Root).Single(node =>
                node.Id == "playnite-library.compact.title").Text);
            if (expected == PlayniteLibraryRoute.Running)
                Assert.AreEqual(beforeRunning + 1, host.RunningObservationCount);
            else if (expected != PlayniteLibraryRoute.Categories)
                Assert.AreEqual(beforeQueries + 1, host.Queries.Count);
            else
            {
                Assert.AreEqual(beforeQueries, host.Queries.Count);
                Assert.AreEqual(
                    "Categories are read from Playnite. Creating or changing " +
                    "membership uses the exact current Playnite game identity.",
                    Nodes(route.Root).Single(node =>
                        node.Id == "playnite-library.categories.help").Text);
                Assert.AreEqual(ViewNodeKind.TextEntry, Nodes(route.Root).Single(node =>
                    node.Id == "playnite-library.category.create").Kind);
            }

            var backId = expected switch
            {
                PlayniteLibraryRoute.AddGames => "playnite-library.add.back",
                PlayniteLibraryRoute.Running => "playnite-library.running.back",
                PlayniteLibraryRoute.Categories => "playnite-library.categories.back",
                _ => "playnite-library.hidden.back",
            };
            await widget.OnActionAsync(new(backId, backId));
            await Bounded(widget.WhenLibraryIdleAsync(), backId + " route load");
            AssertValidCollectionAnchor(Snapshot(widget, sequence++));
        }
    }

    [TestMethod, Timeout(30_000)]
    [DataRow(1)]
    [DataRow(65)]
    [DataRow(130)]
    public async Task LastGameRowContinuesExactlyAcrossAvailableCursorPages(int total)
    {
        var host = new FakeHost(total);
        var widget = Create(host);
        await Interactive(widget);
        await Ready(widget, host);
        var expectedQueries = 1;
        var expectedPages = (total + LauncherWidget.PageSize - 1) /
            LauncherWidget.PageSize;

        for (var page = 1; page < expectedPages; page++)
        {
            var before = Snapshot(widget, 800 + page);
            var scroll = Nodes(before.Root).Single(node =>
                node.Id == PlayniteLibraryPresentation.ScrollId);
            Assert.IsNotNull(scroll.ScrollNearEndActionId,
                "A non-terminal last row must expose one managed continuation action.");
            var lastGame = Nodes(scroll).Last(node => node.CollectionItemKey is not null);
            Assert.IsFalse(lastGame.Id.StartsWith("playnite-library.previous",
                StringComparison.Ordinal));

            var action = new WidgetActionEvent(
                scroll.ScrollNearEndActionId!, scroll.Id,
                ControllerButton.DPadDown, ControllerEventPhase.Pressed,
                InputScopeId: before.ActiveInputScopeId);
            await widget.OnActionAsync(action);
            await Bounded(widget.WhenLibraryIdleAsync(), "last-row cursor continuation");
            expectedQueries++;
            Assert.AreEqual(expectedQueries, host.Queries.Count,
                "One edge input must load the next cursor page exactly once.");
            Assert.IsNotNull(widget.Collection.RequestedFocusId);
            Assert.IsTrue(widget.Collection.RequestedFocusId!.StartsWith(
                "playnite-library.item.grid.", StringComparison.Ordinal));
            Assert.IsTrue(Nodes(Snapshot(widget, 850 + page).Root).Any(node =>
                node.Id == widget.Collection.RequestedFocusId &&
                node.ActionId == "playnite-library.launch"),
                "Continuation focus must land on a game rather than page/footer controls.");
        }

        var final = Snapshot(widget, 900 + total);
        var finalScroll = Nodes(final.Root).Single(node =>
            node.Id == PlayniteLibraryPresentation.ScrollId);
        Assert.IsNull(finalScroll.ScrollNearEndActionId,
            "A final partial row must not expose a looping continuation.");
        await Background(widget);
    }

    [TestMethod, Timeout(30_000)]
    public async Task FocusedShortcutLegendMatchesExactActionability()
    {
        var launchStarted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseLaunch = new TaskCompletionSource<WidgetAppLaunchObservation>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var host = new FakeHost(2)
        {
            LaunchHandler = (_, _) =>
            {
                launchStarted.TrySetResult();
                return new(releaseLaunch.Task);
            },
        };
        var widget = Create(host);
        await Interactive(widget);
        await Ready(widget, host);

        var ready = Snapshot(widget, 950);
        var tile = Nodes(ready.Root).First(node =>
            node.ActionId == "playnite-library.launch");
        var shortcuts = tile.Shortcuts.ToDictionary(shortcut => shortcut.Button);
        Assert.IsFalse(shortcuts.ContainsKey(ControllerButton.View),
            "View is host-reserved and must not be authored by the widget.");
        Assert.AreEqual("playnite-library.favorite",
            shortcuts[ControllerButton.X].ActionId);
        Assert.AreEqual(PlayniteLibraryActionSheet.OpenAction,
            shortcuts[ControllerButton.Y].ActionId);
        Assert.AreEqual("playnite-library.variant",
            shortcuts[ControllerButton.LeftBumper].ActionId);
        Assert.AreEqual("playnite-library.prefer",
            shortcuts[ControllerButton.RightBumper].ActionId);
        foreach (var (hintId, label) in new[]
                 {
                     ("playnite-library.hint.favorite", "Favorite"),
                     ("playnite-library.hint.actions", "Game actions"),
                     ("playnite-library.hint.variant", "Group variants"),
                     ("playnite-library.hint.prefer", "Prefer variant"),
                 })
        {
            Assert.IsTrue(Nodes(ready.Root).Any(node => node.Id == hintId));
            Assert.AreEqual(label, Nodes(ready.Root).Single(node =>
                node.Id == hintId + ".label").Text);
        }

        var launch = widget.OnActionAsync(new(
            "playnite-library.launch", tile.Id)).AsTask();
        await Bounded(launchStarted.Task, "shortcut busy admission");
        var busy = Snapshot(widget, 951);
        var busyTile = Nodes(busy.Root).Single(node => node.Id == tile.Id);
        Assert.IsTrue(busyTile.IsBusy);
        Assert.AreEqual("Pending", Nodes(busy.Root).Single(node =>
            node.Id == "playnite-library.hero.state").Text);
        Assert.AreEqual(0, busyTile.Shortcuts.Count,
            "A busy game must not advertise shortcuts it cannot dispatch.");
        Assert.IsFalse(Nodes(busy.Root).Any(node =>
            node.Id == "playnite-library.organization.hints"),
            "The static legend must not outlive current focused-game actionability.");

        releaseLaunch.TrySetResult(new(
            WidgetAppLaunchObservationState.LauncherStarted, false, false));
        await Bounded(launch, "shortcut busy completion");
        await Bounded(widget.WhenLibraryIdleAsync(), "shortcut recent refresh");
        var completed = Snapshot(widget, 952);
        Assert.AreEqual(4, Nodes(completed.Root).Single(node =>
            node.Id == tile.Id).Shortcuts.Count);
        await Background(widget);
    }

    [TestMethod, Timeout(30_000)]
    public async Task YActionSheetIsScopedCurrentAndRoutesExistingOwners()
    {
        var host = new FakeHost(2);
        var widget = Create(host);
        await Interactive(widget);
        await Ready(widget, host);
        var library = Snapshot(widget, 960);
        var tiles = Nodes(library.Root).Where(node =>
            node.ActionId == "playnite-library.launch").ToArray();

        await widget.OnActionAsync(new(PlayniteLibraryActionSheet.OpenAction, tiles[0].Id));
        var sheet = Snapshot(widget, 961);
        Assert.AreEqual(PlayniteLibraryActionSheet.ScopeId, sheet.ActiveInputScopeId);
        Assert.AreEqual(PlayniteLibraryActionSheet.InitialFocusId, sheet.InitialFocusId);
        var sheetRoot = Nodes(sheet.Root).Single(node =>
            node.Id == "playnite-library.actions.sheet");
        Assert.AreEqual(PlayniteLibraryActionSheet.ScopeId, sheetRoot.InputScopeId);
        Assert.AreEqual(PlayniteLibraryActionSheet.CloseAction,
            sheetRoot.Shortcuts.Single(shortcut =>
                shortcut.Button == ControllerButton.B).ActionId);
        Assert.AreEqual("Add favorite", Nodes(sheet.Root).Single(node =>
            node.Id == PlayniteLibraryActionSheet.InitialFocusId).Text);
        Assert.AreEqual("Hide game", Nodes(sheet.Root).Single(node =>
            node.Id == "playnite-library.actions.hide").Text);
        Assert.AreEqual("Choose another variant", Nodes(sheet.Root).Single(node =>
            node.Id == "playnite-library.actions.variant").Text);
        Assert.AreEqual("Refresh Steam source", Nodes(sheet.Root).Single(node =>
            node.Id == "playnite-library.actions.refresh-source").Text);
        var detailsAction = Nodes(sheet.Root).Single(node =>
            node.Id == PlayniteLibraryActionSheet.DetailsItemId);
        Assert.AreEqual("View details", detailsAction.Text);
        Assert.AreEqual(PlayniteLibraryActionSheet.DetailsAction,
            detailsAction.ActionId);
        Assert.IsFalse(Nodes(sheet.Root).Any(node =>
            node.ActionId == "playnite-library.launch"));
        Assert.IsFalse(Nodes(sheet.Root).SelectMany(node => node.Shortcuts).Any(
            shortcut => shortcut.Button == ControllerButton.View));

        await widget.OnActionAsync(new("playnite-library.favorite",
            PlayniteLibraryActionSheet.InitialFocusId));
        Assert.IsTrue(host.Authority.FavoriteGameIds.Contains("saved-00000"));
        Assert.AreEqual("Remove favorite", Nodes(Snapshot(widget, 962).Root).Single(node =>
            node.Id == PlayniteLibraryActionSheet.InitialFocusId).Text);

        await widget.OnActionAsync(new(PlayniteLibraryActionSheet.CloseAction,
            PlayniteLibraryActionSheet.InitialFocusId, ControllerButton.B,
            ControllerEventPhase.Pressed, InputScopeId: PlayniteLibraryActionSheet.ScopeId));
        var returned = Snapshot(widget, 963);
        Assert.AreEqual(tiles[0].Id, returned.InitialFocusId);

        await widget.OnActionAsync(new(PlayniteLibraryActionSheet.OpenAction, tiles[0].Id));
        await widget.OnActionAsync(new("playnite-library.variant",
            "playnite-library.actions.variant"));
        Assert.IsFalse(Nodes(Snapshot(widget, 964).Root).Any(node =>
            node.Id == "playnite-library.actions.sheet"));

        await widget.OnActionAsync(new(PlayniteLibraryActionSheet.OpenAction, tiles[1].Id));
        Assert.AreEqual("Group with selected game", Nodes(Snapshot(widget, 965).Root)
            .Single(node => node.Id == "playnite-library.actions.variant").Text);
        await widget.OnActionAsync(new("playnite-library.variant",
            "playnite-library.actions.variant"));
        Assert.AreEqual(1, widget.Organization.VariantGroups.Count);
        var grouped = Snapshot(widget, 966);
        Assert.AreEqual("Choose another variant", Nodes(grouped.Root)
            .Single(node => node.Id == "playnite-library.actions.variant").Text);
        var prefer = Nodes(grouped.Root).Single(node =>
            node.Id == "playnite-library.actions.prefer");
        Assert.IsTrue(prefer.Text is "Prefer variant" or "Preferred variant");
        Assert.AreEqual(prefer.Text == "Preferred variant", prefer.IsDisabled is true);

        var queriesBefore = host.Queries.Count;
        await widget.OnActionAsync(new(PlayniteLibraryActionSheet.RefreshSourceAction,
            "playnite-library.actions.refresh-source"));
        await Bounded(widget.WhenLibraryIdleAsync(), "action-sheet source refresh");
        Assert.IsTrue(host.Queries.Count > queriesBefore);
        Assert.IsFalse(Nodes(Snapshot(widget, 967).Root).Any(node =>
            node.Id == "playnite-library.actions.sheet"));

        var refreshedLibrary = Snapshot(widget, 968);
        var currentSecond = Nodes(refreshedLibrary.Root).Single(node =>
            node.ActionId == "playnite-library.launch" &&
            (node.AccessibilityLabel ?? string.Empty).Contains(
                "Game 00001", StringComparison.Ordinal));
        await widget.OnActionAsync(new(PlayniteLibraryActionSheet.OpenAction, currentSecond.Id));
        await widget.OnActionAsync(new("playnite-library.hide", "playnite-library.actions.hide"));
        await Bounded(widget.WhenLibraryIdleAsync(), "action-sheet hidden library refresh");
        Assert.IsTrue(host.Authority.HiddenGameIds.Contains("saved-00001"));
        Assert.IsFalse(Nodes(Snapshot(widget, 969).Root).Any(node =>
            node.Id == "playnite-library.actions.sheet" || node.Id == currentSecond.Id));
        await Background(widget);
    }

    [TestMethod, Timeout(30_000)]
    public async Task CommittedQueryReplacesGenerationAndStaleCompletionCannotPublish()
    {
        var host = new FakeHost(2);
        var widget = Create(host);
        await Interactive(widget);
        await Ready(widget, host);

        var staleStarted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseStale = new TaskCompletionSource<WidgetAppLibraryPage>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        host.QueryHandler = (request, _) =>
        {
            if (request.Query.SearchText == "Alpha")
            {
                staleStarted.TrySetResult();
                return new ValueTask<WidgetAppLibraryPage>(releaseStale.Task);
            }
            var item = WithPresentation(
                Item(1), displayName: request.Query.SearchText ?? "All");
            return ValueTask.FromResult(new WidgetAppLibraryPage(
                [item], null, null, "query-" + (request.Query.SearchText ?? "all")));
        };

        await widget.OnActionAsync(new WidgetActionEvent(
            "playnite-library.search.commit", "playnite-library.search")
            { CommittedText = "  Alpha  " });
        await Bounded(staleStarted.Task, "stale query admission");
        await widget.OnActionAsync(new WidgetActionEvent(
            "playnite-library.search.commit", "playnite-library.search")
            { CommittedText = "Beta" });
        releaseStale.TrySetResult(new([WithPresentation(Item(0), displayName: "Alpha")],
            null, null, "query-alpha"));
        await Bounded(widget.WhenLibraryIdleAsync(), "replacement query drain");
        Assert.AreEqual("Beta",
            widget.Collection.Items.Single().Presentation.DisplayName);

        var snapshot = Snapshot(widget, 99);
        var search = Nodes(snapshot.Root).Single(node => node.Id == "playnite-library.search");
        Assert.AreEqual(ViewNodeKind.TextEntry, search.Kind);
        Assert.AreEqual("Beta", search.TextEntryValue);
        await Background(widget);
    }

    [TestMethod]
    public void RenderStateHasOneModelOwnerAndNoRetiredScalarOwners()
    {
        var fields = typeof(LauncherWidget).GetFields(
            System.Reflection.BindingFlags.Instance |
            System.Reflection.BindingFlags.NonPublic);
        var models = fields.Where(field => field.FieldType.IsGenericType &&
            field.FieldType.GetGenericTypeDefinition() == typeof(WidgetModel<>)).ToArray();

        Assert.AreEqual(1, models.Length,
            "Render-facing state must have one WidgetModel owner.");
        Assert.AreEqual(typeof(PlayniteLibraryRenderState),
            models[0].FieldType.GetGenericArguments()[0]);
        CollectionAssert.AreEquivalent(new[]
        {
            "_application", "_gate", "_launchPersistence", "_launchStateRecency",
            "_launchStates", "_library", "_model", "_navigation", "_organization",
            "_playniteAuthority", "_playniteClient", "_playniteGeneration",
            "_stateGate", "_stateRevision",
        }, fields.Select(field => field.Name).ToArray(),
            "A mutable presentation owner was added outside the model boundary.");
    }

    [TestMethod, Timeout(30_000)]
    public async Task EqualHeroTransitionPublishesNoRevisionOrInvalidation()
    {
        var host = new FakeHost(2);
        var widget = Create(host);
        await Interactive(widget);
        await Ready(widget, host);
        await Bounded(widget.WhenWarmStateIdleAsync(), "model warm-state drain");
        await Bounded(widget.WhenLibraryIdleAsync(), "model projection drain");
        var tiles = Nodes(Snapshot(widget, 98).Root).Where(node =>
            node.ActionId == "playnite-library.launch").ToArray();
        var invalidations = 0;
        EventHandler<WidgetInvalidatedEventArgs> handler = (_, _) => invalidations++;
        widget.Invalidated += handler;
        try
        {
            var before = widget.RenderState;
            await widget.OnActionAsync(new("unhandled.fixture", tiles[1].Id));
            var changed = widget.RenderState;
            Assert.AreEqual(before.Revision + 1, changed.Revision,
                "One semantic hero change must commit one model revision.");
            Assert.AreEqual("saved-00001", changed.Value.HeroSavedId);
            Assert.AreEqual(1, invalidations,
                "One semantic hero change must publish one invalidation.");

            await widget.OnActionAsync(new("unhandled.fixture", tiles[1].Id));
            var unchanged = widget.RenderState;
            Assert.AreEqual(changed.Revision, unchanged.Revision,
                "An equal model replacement must not advance the revision.");
            Assert.AreEqual(1, invalidations,
                "An equal model replacement must not publish an invalidation.");
        }
        finally
        {
            widget.Invalidated -= handler;
        }
        await Background(widget);
    }

    [TestMethod, Timeout(30_000)]
    [DataRow(2_000)]
    [DataRow(10_000)]
    public async Task LargeLibrariesTraverseInBoundedCursorWindow(int total)
    {
        var host = new FakeHost(total);
        var widget = Create(host);
        await Interactive(widget);
        await Ready(widget, host);

        var pages = (total + LauncherWidget.PageSize - 1) / LauncherWidget.PageSize;
        for (var page = 1; page < pages; page++)
        {
            var revision = widget.Collection.Revision;
            await widget.OnActionAsync(new("playnite-library.next", "playnite-library.next"));
            await Bounded(widget.WhenLibraryIdleAsync(), "forward page drain");
            Assert.IsGreaterThan(revision, widget.Collection.Revision);
            Assert.AreEqual(WidgetPagedResourceStatus.Ready, widget.Collection.Status);
            Assert.IsLessThanOrEqualTo(LauncherWidget.MaximumRetainedItems,
                widget.Collection.Items.Count);
        }

        Assert.AreEqual(total, host.MaximumObservedIndex + 1);
        Assert.AreEqual(LauncherWidget.PageSize, host.MaximumRequestedLimit);
        Assert.IsLessThanOrEqualTo(LauncherWidget.MaximumRetainedItems,
            widget.Collection.Items.Count);
        Assert.AreEqual($"Game {total - 1:D5}",
            widget.Collection.Items[^1].Presentation.DisplayName);
        var serialized = Snapshot(widget, total);
        Assert.AreEqual(widget.Collection.Items.Count, Nodes(serialized.Root).Count(node =>
            node.ActionId == "playnite-library.launch"));
        Assert.IsLessThanOrEqualTo(
            WidgetCursorResource<PlayniteLibraryItem>.MaximumCursorHistory,
            widget.RetainedCursorCount);
        var beforeRevision = widget.Collection.Revision;
        await widget.OnActionAsync(new("playnite-library.previous", "playnite-library.previous"));
        await Bounded(widget.WhenLibraryIdleAsync(), "reverse page drain");
        Assert.IsGreaterThan(beforeRevision, widget.Collection.Revision);
        Assert.AreEqual(WidgetPagedResourceStatus.Ready, widget.Collection.Status);
        var finalPageStart = (pages - 1) * LauncherWidget.PageSize;
        var reversePageStart = finalPageStart - LauncherWidget.MaximumRetainedItems;
        Assert.AreEqual($"Game {reversePageStart:D5}",
            widget.Collection.Items[0].Presentation.DisplayName);
        Assert.AreEqual(
            PlayniteLibraryIdentity.FocusId("grid", widget.Collection.Items[63].Key),
            widget.Collection.RequestedFocusId);
        await Background(widget);
    }

    [TestMethod, Timeout(30_000)]
    public async Task ControllerBumpersAreScopedAndBoundaryExactAcrossPages()
    {
        var host = new FakeHost(260)
        {
            LaunchHandler = (_, _) => ValueTask.FromResult(new WidgetAppLaunchObservation(
                WidgetAppLaunchObservationState.LauncherStarted, false, false)),
        };
        var widget = Create(host);
        await Interactive(widget);
        await Ready(widget, host);

        var first = Snapshot(widget, 201);
        var firstTile = Nodes(first.Root).First(node =>
            node.ActionId == "playnite-library.launch");
        await widget.OnActionAsync(new("playnite-library.launch", firstTile.Id));
        await WaitUntil(() => widget.Organization.RecentSavedIds.Count == 1);
        AssertShortcutMap(first, before: false, after: true);
        Assert.IsFalse(await Route(widget, first, ControllerButton.LeftBumper, firstTile.Id));
        Assert.IsFalse(await Route(widget, first, ControllerButton.RightBumper, firstTile.Id,
            ControllerEventPhase.Repeated));
        Assert.IsTrue(await Route(widget, first, ControllerButton.RightBumper, firstTile.Id));
        await WaitUntil(() => host.Queries.Count == 2);
        await Bounded(widget.WhenLibraryIdleAsync(), "first bumper page");

        var retained = Snapshot(widget, 202);
        AssertShortcutMap(retained, before: false, after: true);
        Assert.IsTrue(Nodes(retained.Root).Any(node => node.Id == firstTile.Id),
            "The exact focused SavedId should remain available in the retained window.");
        Assert.IsTrue(await Route(widget, retained, ControllerButton.RightBumper, firstTile.Id));
        await WaitUntil(() => host.Queries.Count == 3);
        await Bounded(widget.WhenLibraryIdleAsync(), "second retained bumper page");

        var beforeEviction = Snapshot(widget, 203);
        AssertShortcutMap(beforeEviction, before: false, after: true);
        Assert.IsTrue(await Route(
            widget, beforeEviction, ControllerButton.RightBumper, firstTile.Id));
        await WaitUntil(() => host.Queries.Count == 4);
        await Bounded(widget.WhenLibraryIdleAsync(), "evicting bumper page");

        var middle = Snapshot(widget, 204);
        AssertShortcutMap(middle, before: true, after: true);
        Assert.IsFalse(Nodes(middle.Root).Any(node => node.Id == firstTile.Id));
        var nearest = PlayniteLibraryIdentity.FocusId(
            "grid", PlayniteLibraryIdentity.Key("saved-00192"));
        Assert.AreEqual(nearest, widget.Collection.RequestedFocusId);
        Assert.IsTrue(Nodes(middle.Root).Any(node => node.Id == nearest));
        Assert.IsTrue(await Route(widget, middle, ControllerButton.RightBumper, nearest));
        await WaitUntil(() => host.Queries.Count == 5);
        await Bounded(widget.WhenLibraryIdleAsync(), "final bumper page");

        var final = Snapshot(widget, 205);
        AssertShortcutMap(final, before: true, after: false);
        var finalTile = Nodes(final.Root).First(node =>
            node.ActionId == "playnite-library.launch");
        Assert.IsFalse(await Route(widget, final, ControllerButton.RightBumper, finalTile.Id));
        Assert.IsTrue(await Route(widget, final, ControllerButton.LeftBumper, finalTile.Id));
        await WaitUntil(() => host.Queries.Count == 6);
        await Bounded(widget.WhenLibraryIdleAsync(), "reverse bumper page");

        var reverse = Snapshot(widget, 206);
        AssertShortcutMap(reverse, before: true, after: true);
        Assert.IsFalse(await Route(
            widget, reverse, ControllerButton.RightBumper, "playnite-library.refresh"),
            "Bumpers outside the results scroll must keep their existing ownership.");

        await widget.OnActionAsync(new(
            "playnite-library.filter.recent", "playnite-library.filter.recent"));
        await Bounded(widget.WhenLibraryIdleAsync(), "recent-first reload");
        var fixedSnapshot = Snapshot(widget, 207);
        var fixedTile = Nodes(fixedSnapshot.Root).First(node =>
            node.ActionId == "playnite-library.launch" &&
            node.CollectionItemKey is null);
        Assert.IsTrue(await Route(
            widget, fixedSnapshot, ControllerButton.RightBumper, fixedTile.Id));
        await Bounded(widget.WhenLibraryIdleAsync(), "fixed-section bumper page");
        Assert.IsLessThanOrEqualTo(
            LauncherWidget.MaximumRetainedItems, widget.Collection.Items.Count);
        await Background(widget);

        var singleHost = new FakeHost(2);
        var single = Create(singleHost);
        await Interactive(single);
        await Ready(single, singleHost);
        var singleSnapshot = Snapshot(single, 208);
        AssertShortcutMap(singleSnapshot, before: false, after: false);
        var singleTile = Nodes(singleSnapshot.Root).First(node =>
            node.ActionId == "playnite-library.launch");
        Assert.AreEqual("playnite-library.variant", singleTile.Shortcuts.Single(shortcut =>
            shortcut.Button == ControllerButton.LeftBumper).ActionId);
        Assert.AreEqual("playnite-library.prefer", singleTile.Shortcuts.Single(shortcut =>
            shortcut.Button == ControllerButton.RightBumper).ActionId);
        await Background(single);
    }

    [TestMethod, Timeout(30_000)]
    public async Task BumperPagingRejectsBusyAndReplacementStaleCompletion()
    {
        var host = new FakeHost(130);
        var widget = Create(host);
        await Interactive(widget);
        await Ready(widget, host);
        var staleStarted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseStale = new TaskCompletionSource<WidgetAppLibraryPage>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        host.QueryHandler = (request, _) =>
        {
            if (request.Query.SearchText == "Current")
                return ValueTask.FromResult(new WidgetAppLibraryPage(
                    [WithPresentation(Item(1), displayName: "Current")], null, null,
                    "current-revision"));
            staleStarted.TrySetResult();
            return new(releaseStale.Task);
        };

        var admitted = Snapshot(widget, 211);
        var focused = Nodes(admitted.Root).First(node =>
            node.ActionId == "playnite-library.launch").Id;
        Assert.IsTrue(await Route(
            widget, admitted, ControllerButton.RightBumper, focused));
        await Bounded(staleStarted.Task, "bumper page admission");
        var busy = Snapshot(widget, 212);
        Assert.AreEqual(WidgetPagedResourceStatus.LoadingAdjacent, widget.Collection.Status);
        AssertShortcutMap(busy, before: false, after: false);
        Assert.IsFalse(await Route(widget, busy, ControllerButton.RightBumper, focused));

        await widget.OnActionAsync(new WidgetActionEvent(
            "playnite-library.search.commit", "playnite-library.search")
            { CommittedText = "Current" });
        releaseStale.TrySetResult(new(
            [WithPresentation(Item(64), displayName: "Stale")], "cursor.0", "cursor.65",
            "stale-revision"));
        await Bounded(widget.WhenLibraryIdleAsync(), "bumper replacement drain");
        Assert.AreEqual("Current",
            widget.Collection.Items.Single().Presentation.DisplayName);
        Assert.IsFalse(widget.Collection.Items.Any(item =>
            item.Presentation.DisplayName == "Stale"));
        await Background(widget);
    }

    [TestMethod, Timeout(30_000)]
    public async Task WarmProjectionIsVisibleButCannotAuthorizeLaunch()
    {
        var warm = new PlayniteLibraryPrivateState(PlayniteLibraryPrivateState.CurrentVersion,
            [new("saved-warm", "Warm game", "Steam")]);
        var state = new WidgetTestPrivateState(JsonSerializer.Serialize(warm), 1);
        var pending = new TaskCompletionSource<WidgetAppLibraryPage>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var host = new FakeHost(0, state)
        {
            QueryHandler = (_, token) => new ValueTask<WidgetAppLibraryPage>(
                pending.Task.WaitAsync(token)),
        };
        var widget = Create(host);
        await Visible(widget);
        await Bounded(host.FirstQueryStarted.Task, "warm projection query admission");
        await Bounded(widget.WhenWarmStateIdleAsync(), "warm state load");
        Assert.AreEqual(1, widget.WarmItems.Count);

        var snapshot = Snapshot(widget, 1);
        var tile = Nodes(snapshot.Root).Single(node =>
            node.ActionId == "playnite-library.launch");
        Assert.IsTrue(tile.IsDisabled);
        await widget.OnActionAsync(new("playnite-library.launch", tile.Id));
        Assert.AreEqual(0, host.Launches.Count);

        await Background(widget);
        pending.TrySetResult(new([], null, null, "rev-1"));
    }

    [TestMethod]
    public void PrivateProjectionIsBoundedAndContainsNoLaunchAuthority()
    {
        var items = Enumerable.Range(0, 128)
            .Select(index => new PlayniteLibraryDisplayItem(
                $"saved-{index:D3}-" + new string('s', 114),
                new string((char)('a' + index % 26), 96),
                new string((char)('A' + index % 26), 64)))
            .ToArray();
        var organized = items.Take(PlayniteLibraryPrivateState.MaximumOrganizedItems).ToArray();
        var groups = organized.Chunk(PlayniteLibraryPrivateState.MaximumVariantsPerGroup)
            .Select((members, index) => new PlayniteLibraryVariantGroup(
                "variant." + index.ToString("x20"),
                members.Select(item => item.SavedId).ToArray(),
                members[^1].SavedId)).ToArray();
        var state = new PlayniteLibraryPrivateState(
            PlayniteLibraryPrivateState.CurrentVersion, items)
        {
            FavoriteSavedIds = organized.Select(item => item.SavedId).ToArray(),
            VariantGroups = groups,
            RecentSavedIds = items.Skip(PlayniteLibraryPrivateState.MaximumOrganizedItems)
                .Take(PlayniteLibraryPrivateState.MaximumRecentItems)
                .Reverse().Select(item => item.SavedId).ToArray(),
            ManualSavedIds = items.Skip(PlayniteLibraryPrivateState.MaximumOrganizedItems +
                    PlayniteLibraryPrivateState.MaximumRecentItems)
                .Take(PlayniteLibraryPrivateState.MaximumManualItems)
                .Select(item => item.SavedId).ToArray(),
            ExcludedSavedIds = items.Skip(PlayniteLibraryPrivateState.MaximumOrganizedItems +
                    PlayniteLibraryPrivateState.MaximumRecentItems +
                    PlayniteLibraryPrivateState.MaximumManualItems)
                .Take(PlayniteLibraryPrivateState.MaximumExcludedItems)
                .Select(item => item.SavedId).ToArray(),
            Categories = Enumerable.Range(0, PlayniteLibraryPrivateState.MaximumCategories)
                .Select(index => new PlayniteLibraryCategory(
                    "category." + index.ToString("x32"),
                    new string((char)('A' + index),
                        PlayniteLibraryPrivateState.MaximumCategoryNameLength),
                    items.Skip(index * 2).Take(2)
                        .Select(item => item.SavedId).ToArray()))
                .ToArray(),
            ProvenSources = Enumerable.Range(
                    0, PlayniteLibraryPrivateState.MaximumProvenSources)
                .Select(index => $"Source {index:D2} " + new string('S', 54))
                .ToArray(),
        };
        var normalizedState = PlayniteLibraryOrganizationPolicy.Normalize(state);
        var json = JsonSerializer.SerializeToUtf8Bytes(normalizedState);
        var baseBytes = JsonSerializer.SerializeToUtf8Bytes(state with { Categories = [] });

        Assert.IsLessThanOrEqualTo(64 * 1024, json.Length,
            $"Base={baseBytes.Length}, categories={json.Length - baseBytes.Length}");
        Assert.AreEqual(0, normalizedState.Categories.Count,
            "Over-budget categories were not reset atomically.");
        Assert.AreEqual(items.Length, normalizedState.Items.Count);
        Assert.AreEqual(0, normalizedState.ProvenSources.Count,
            "Byte pressure did not reset only the non-authorizing source catalog.");
        var maximumSources = state.ProvenSources;
        var sourcesOnly = PlayniteLibraryOrganizationPolicy.Normalize(
            PlayniteLibraryPrivateState.Empty with { ProvenSources = maximumSources });
        Assert.AreEqual(PlayniteLibraryPrivateState.MaximumProvenSources,
            sourcesOnly.ProvenSources.Count);
        Assert.IsLessThanOrEqualTo(64 * 1024,
            JsonSerializer.SerializeToUtf8Bytes(sourcesOnly).Length);
        var invalidSources = PlayniteLibraryOrganizationPolicy.Normalize(state with
        {
            Categories = [],
            ProvenSources = Enumerable.Range(
                    0, PlayniteLibraryPrivateState.MaximumProvenSources + 1)
                .Select(index => $"Source {index:D2}").ToArray(),
        });
        Assert.AreEqual(0, invalidSources.ProvenSources.Count,
            "Invalid source evidence was not reset as one affected field.");
        Assert.AreEqual(items.Length, invalidSources.Items.Count,
            "Invalid source evidence reset unrelated organization state.");
        var validationOverflow = Enumerable.Range(
                0, PlayniteLibraryPrivateState.MaximumItems + 1)
            .Select(index => new PlayniteLibraryDisplayItem(
                $"saved-validation-{index:D4}", $"Game {index:D4}", "Local"))
            .ToArray();
        Assert.AreEqual(0, PlayniteLibraryOrganizationPolicy.Normalize(
            PlayniteLibraryPrivateState.Empty with { Items = validationOverflow }).Items.Count);
        var text = System.Text.Encoding.UTF8.GetString(json);
        Assert.IsFalse(text.Contains("AppId", StringComparison.Ordinal));
        Assert.IsFalse(text.Contains("Artwork", StringComparison.Ordinal));
    }

    [TestMethod, Timeout(30_000)]
    public async Task HideSurvivesRestartAndRestoreNeverAuthorizesLaunch()
    {
        var privateState = new WidgetTestPrivateState();
        var host = new FakeHost(3, privateState);
        var widget = Create(host);
        await Interactive(widget);
        await Ready(widget, host);
        var first = Nodes(Snapshot(widget, 301).Root).First(node =>
            node.ActionId == "playnite-library.launch");
        await widget.OnActionAsync(new("playnite-library.favorite", first.Id));
        await widget.OnActionAsync(new("playnite-library.hide", first.Id));
        await Bounded(widget.WhenLibraryIdleAsync(), "hidden library refresh");
        CollectionAssert.AreEqual(new[] { "saved-00000" },
            host.Authority.HiddenGameIds.ToArray());
        Assert.IsFalse(Nodes(Snapshot(widget, 302).Root).Any(node =>
            node.ActionId == "playnite-library.launch" && node.Id == first.Id));
        var afterHide = Snapshot(widget, 3021);
        Assert.IsNotNull(afterHide.InitialFocusId);
        Assert.IsTrue(Nodes(afterHide.Root).Any(node =>
            node.Id == afterHide.InitialFocusId &&
            node.ActionId == "playnite-library.launch" && node.IsDisabled is not true));
        await Background(widget);

        var restartedHost = new FakeHost(3, privateState);
        var restarted = Create(restartedHost);
        await Interactive(restarted);
        await Ready(restarted, restartedHost);
        Assert.IsFalse(Nodes(Snapshot(restarted, 303).Root).Any(node =>
            node.ActionId == "playnite-library.launch" && node.Id == first.Id));
        await restarted.OnActionAsync(new(
            "playnite-library.hidden.open", "playnite-library.hidden.open"));
        await Bounded(restarted.WhenLibraryIdleAsync(), "hidden route load");
        CollectionAssert.AreEqual(new[] { "saved-00000" },
            restartedHost.Queries[^1].Query.FavoriteSavedIds.ToArray());
        var hidden = Nodes(Snapshot(restarted, 304).Root).Single(node =>
            node.ActionId == "playnite-library.restore");
        StringAssert.Contains(hidden.AccessibilityLabel!, "Hidden · Restore");
        await restarted.OnActionAsync(new WidgetActionEvent(
            "playnite-library.search.commit", "playnite-library.search")
            { CommittedText = "No such hidden game" });
        await Bounded(restarted.WhenLibraryIdleAsync(), "hidden query filter");
        Assert.IsTrue(Nodes(Snapshot(restarted, 3041).Root).Any(node =>
            node.Id == "playnite-library.hidden.empty.action"));
        await restarted.OnActionAsync(new(
            "playnite-library.query.clear", "playnite-library.query.clear"));
        await Bounded(restarted.WhenLibraryIdleAsync(), "hidden query clear");
        hidden = Nodes(Snapshot(restarted, 3042).Root).Single(node =>
            node.ActionId == "playnite-library.restore");
        await restarted.OnActionAsync(new("playnite-library.launch", hidden.Id));
        Assert.AreEqual(0, restartedHost.Launches.Count,
            "A display-only hidden row must not authorize launch.");
        await restarted.OnActionAsync(new("playnite-library.restore", hidden.Id));
        Assert.AreEqual(0, restartedHost.Authority.HiddenGameIds.Count);
        CollectionAssert.AreEqual(new[] { "saved-00000" },
            restartedHost.Authority.FavoriteGameIds.ToArray());
        Assert.IsTrue(Nodes(Snapshot(restarted, 305).Root).Any(node =>
            node.Id == "playnite-library.hidden.empty.action"));
        await restarted.OnActionAsync(new(
            "playnite-library.hidden.back", "playnite-library.hidden.empty.action"));
        var restoredLibrary = Snapshot(restarted, 306);
        Assert.IsTrue(Nodes(restoredLibrary.Root).Any(node =>
            node.ActionId == "playnite-library.launch" && node.Id == first.Id &&
            node.IsDisabled is not true));
        await Background(restarted);
    }

    [TestMethod, Timeout(30_000)]
    public async Task RestoreSupersedesCancellationIgnoringHiddenLoadBeforeBack()
    {
        var display = new PlayniteLibraryDisplayItem("saved-00000", "Game 00000", "Steam");
        var persisted = new PlayniteLibraryPrivateState(
            PlayniteLibraryPrivateState.CurrentVersion, [display])
        {
            ExcludedSavedIds = [display.SavedId],
        };
        var host = new FakeHost(1, new WidgetTestPrivateState(
            JsonSerializer.Serialize(persisted), 1));
        var widget = Create(host);
        await Interactive(widget);
        await Ready(widget, host);
        var hiddenLoadStarted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseHiddenLoad = new TaskCompletionSource<WidgetAppLibraryPage>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var queryCount = 0;
        host.QueryHandler = (_, _) =>
        {
            if (Interlocked.Increment(ref queryCount) == 1)
            {
                hiddenLoadStarted.TrySetResult();
                return new(releaseHiddenLoad.Task);
            }
            return ValueTask.FromResult(new WidgetAppLibraryPage(
                [Item(0)], null, null, "current-library"));
        };

        await widget.OnActionAsync(new(
            "playnite-library.hidden.open", "playnite-library.hidden.open"));
        await Bounded(hiddenLoadStarted.Task, "cancellation-ignoring hidden load admission");
        var hidden = Nodes(Snapshot(widget, 307).Root).Single(node =>
            node.ActionId == "playnite-library.restore");
        var restore = widget.OnActionAsync(new(
            "playnite-library.restore", hidden.Id)).AsTask();
        Assert.IsFalse(restore.IsCompleted,
            "Restore must drain the replaced Hidden generation before reporting readiness.");
        releaseHiddenLoad.TrySetResult(new(
            [Item(0)], null, null, "late-hidden"));
        await Bounded(restore, "cancellation-ignoring Hidden replacement drain");
        Assert.IsTrue(Nodes(Snapshot(widget, 3071).Root).Any(node =>
            node.Id == "playnite-library.hidden.empty.action"));

        using var canceledRoute = new CancellationTokenSource();
        canceledRoute.Cancel();
        await widget.OnActionAsync(new(
            "playnite-library.hidden.back", "playnite-library.hidden.empty.action"),
            canceledRoute.Token);
        var current = Snapshot(widget, 308);
        var launch = Nodes(current.Root).Single(node =>
            node.ActionId == "playnite-library.launch" &&
            (node.AccessibilityLabel ?? string.Empty).Contains(
                display.DisplayName, StringComparison.Ordinal));
        Assert.IsTrue(launch.IsDisabled is not true);
        Assert.IsNotNull(current.InitialFocusId);
        Assert.IsTrue(Nodes(current.Root).Any(node =>
            node.Id == current.InitialFocusId &&
            node.ActionId == "playnite-library.launch" && node.IsDisabled is not true));

        var afterLateCompletion = Snapshot(widget, 309);
        Assert.IsTrue(Nodes(afterLateCompletion.Root).Any(node =>
            node.Id == launch.Id && node.ActionId == "playnite-library.launch" &&
            node.IsDisabled is not true));
        await Background(widget);
    }

    [TestMethod, Timeout(30_000)]
    public async Task MissingAndReplacementRowsRemainIndependentFromHiddenIdentity()
    {
        var hidden = new PlayniteLibraryDisplayItem("saved-00000", "Shared title", "Steam");
        var persisted = new PlayniteLibraryPrivateState(
            PlayniteLibraryPrivateState.CurrentVersion, [hidden])
        {
            ExcludedSavedIds = [hidden.SavedId],
        };
        var privateState = new WidgetTestPrivateState(JsonSerializer.Serialize(persisted), 1);
        var host = new FakeHost(1, privateState)
        {
            ItemFactory = _ => WithPresentation(Item(1), displayName: hidden.DisplayName),
        };
        var widget = Create(host);
        await Interactive(widget);
        await Ready(widget, host);
        var library = Nodes(Snapshot(widget, 311).Root).Where(node =>
            node.ActionId == "playnite-library.launch").ToArray();
        Assert.AreEqual(1, library.Length);
        StringAssert.Contains(library[0].AccessibilityLabel!, hidden.DisplayName);
        await widget.OnActionAsync(new(
            "playnite-library.hidden.open", "playnite-library.hidden.open"));
        await Bounded(widget.WhenLibraryIdleAsync(), "missing hidden route");
        var unavailable = Nodes(Snapshot(widget, 312).Root).Single(node =>
            node.ActionId == "playnite-library.restore");
        StringAssert.Contains(unavailable.AccessibilityLabel!, "Unavailable · Restore");
        Assert.AreEqual("playnite-library.item.hidden." +
            PlayniteLibraryIdentity.Key(hidden.SavedId).Value, unavailable.Id);
        await Background(widget);

        var reclassifiedHost = new FakeHost(1, privateState)
        {
            ItemFactory = _ => WithPresentation(
                Item(0), kind: WidgetAppLibraryKind.Application),
        };
        var reclassified = Create(reclassifiedHost);
        await Interactive(reclassified);
        await Ready(reclassified, reclassifiedHost);
        Assert.IsFalse(Nodes(Snapshot(reclassified, 313).Root).Any(node =>
            node.ActionId == "playnite-library.launch"));
        await reclassified.OnActionAsync(new(
            "playnite-library.hidden.open", "playnite-library.hidden.open"));
        await Bounded(reclassified.WhenLibraryIdleAsync(), "reclassified hidden route");
        var current = Nodes(Snapshot(reclassified, 314).Root).Single(node =>
            node.ActionId == "playnite-library.restore");
        StringAssert.Contains(current.AccessibilityLabel!,
            "Game 00000, Steam, Hidden · Restore");
        await Background(reclassified);
    }

    [TestMethod]
    public async Task HiddenBoundAndCasReplayPreserveConcurrentOrganization()
    {
        var displays = Enumerable.Range(0, PlayniteLibraryPrivateState.MaximumExcludedItems + 2)
            .Select(index => new PlayniteLibraryDisplayItem(
                $"saved-{index:D5}", $"Game {index}", "Steam"))
            .ToArray();
        var state = PlayniteLibraryPrivateState.Empty;
        for (var index = 0; index < PlayniteLibraryPrivateState.MaximumExcludedItems; index++)
        {
            var mutation = PlayniteLibraryOrganizationPolicy.SetExcluded(
                state, displays[index], excluded: true);
            Assert.IsTrue(mutation.Accepted);
            state = mutation.State;
        }
        var overflow = PlayniteLibraryOrganizationPolicy.SetExcluded(
            state, displays[^1], excluded: true);
        Assert.IsFalse(overflow.Accepted);
        Assert.AreEqual(PlayniteLibraryPrivateState.MaximumExcludedItems,
            overflow.State.ExcludedSavedIds.Count);

        var concurrent = state with
        {
            Items = [displays[^2], .. state.Items],
            FavoriteSavedIds = [displays[^2].SavedId],
            VariantGroups =
            [
                new(PlayniteLibraryIdentity.GroupId(
                        displays[^2].SavedId, displays[31].SavedId),
                    [displays[^2].SavedId, displays[31].SavedId],
                    displays[^2].SavedId),
            ],
            RecentSavedIds = [displays[^2].SavedId],
            ManualSavedIds = [displays[^2].SavedId],
        };
        PlayniteLibraryPrivateState? written = null;
        var attempts = 0;
        var restored = displays[0];
        var result = await PlayniteLibraryStateStore.SaveAsync(
            current => PlayniteLibraryOrganizationPolicy.SetExcluded(
                current, restored, excluded: false),
            (candidate, _, _) =>
            {
                if (attempts++ == 0)
                    return ValueTask.FromException<WidgetPrivateStateMutation>(
                        new WidgetCapabilityException("state_conflict", "fixture"));
                written = candidate;
                return ValueTask.FromResult(new WidgetPrivateStateMutation(3));
            },
            _ => ValueTask.FromResult(new WidgetPrivateStateValue<PlayniteLibraryPrivateState>(
                true, concurrent, 2)), state, 1, CancellationToken.None);
        Assert.IsTrue(result.Saved);
        Assert.IsFalse(written!.ExcludedSavedIds.Contains(restored.SavedId));
        Assert.AreEqual(PlayniteLibraryPrivateState.MaximumExcludedItems - 1,
            written.ExcludedSavedIds.Count);
        CollectionAssert.AreEqual(new[] { displays[^2].SavedId },
            written.FavoriteSavedIds.ToArray());
        CollectionAssert.AreEqual(new[] { displays[^2].SavedId },
            written.RecentSavedIds.ToArray());
        CollectionAssert.AreEqual(new[] { displays[^2].SavedId },
            written.ManualSavedIds.ToArray());
        Assert.AreEqual(displays[^2].SavedId,
            written.VariantGroups.Single().PreferredSavedId);

        await Assert.ThrowsExactlyAsync<IOException>(async () =>
            await PlayniteLibraryStateStore.SaveAsync(
                current => PlayniteLibraryOrganizationPolicy.SetExcluded(
                    current, displays[1], excluded: false),
                (_, _, _) => ValueTask.FromException<WidgetPrivateStateMutation>(
                    new IOException("fixture")),
                _ => ValueTask.FromResult(new WidgetPrivateStateValue<PlayniteLibraryPrivateState>(
                    true, state, 1)), state, 1, CancellationToken.None));
        Assert.IsTrue(state.ExcludedSavedIds.Contains(displays[1].SavedId));
    }

    [TestMethod, Timeout(30_000)]
    public async Task FavoritesAndExplicitVariantsSurviveRestartAndDisappearance()
    {
        var state = new WidgetTestPrivateState();
        var host = new FakeHost(2, state)
        {
            ItemFactory = index => WithPresentation(Item(index), displayName: "Shared title"),
        };
        var widget = Create(host);
        await Interactive(widget);
        await Ready(widget, host);
        var tiles = Nodes(Snapshot(widget, 10).Root)
            .Where(node => node.ActionId == "playnite-library.launch").ToArray();

        await widget.OnActionAsync(new("playnite-library.favorite", tiles[0].Id));
        await widget.OnActionAsync(new("playnite-library.variant", tiles[0].Id));
        await widget.OnActionAsync(new("playnite-library.variant", tiles[1].Id));
        await widget.OnActionAsync(new("playnite-library.prefer", tiles[1].Id));
        Assert.IsTrue(host.Authority.FavoriteGameIds.Contains("saved-00000"));
        Assert.AreEqual(1, widget.Organization.VariantGroups.Count);
        var group = widget.Organization.VariantGroups.Single();
        Assert.AreEqual("saved-00001", group.PreferredSavedId);
        await Background(widget);

        var missingHost = new FakeHost(0, state);
        var missing = Create(missingHost);
        await Interactive(missing);
        await Ready(missing, missingHost);
        var missingSnapshot = Snapshot(missing, 11);
        Assert.IsTrue(Nodes(missingSnapshot.Root).Any(node =>
            node.ActionId == "playnite-library.launch" && node.IsDisabled == true &&
            (node.AccessibilityLabel ?? string.Empty).Contains(
                "Preferred variant", StringComparison.Ordinal)));
        Assert.AreEqual(1, missing.Organization.VariantGroups.Count);
        Assert.AreEqual("saved-00001",
            missing.Organization.VariantGroups.Single().PreferredSavedId);
        await Background(missing);

        var restoredHost = new FakeHost(2, state);
        var restored = Create(restoredHost);
        await Interactive(restored);
        await Ready(restored, restoredHost);
        var restoredPreferred = Nodes(Snapshot(restored, 12).Root).Single(node =>
            node.ActionId == "playnite-library.launch" &&
            (node.AccessibilityLabel ?? string.Empty).Contains(
                "Preferred variant", StringComparison.Ordinal));
        Assert.IsTrue(restoredPreferred.IsDisabled != true);
        var restoredTiles = Nodes(Snapshot(restored, 13).Root)
            .Where(node => node.ActionId == "playnite-library.launch").ToArray();
        await restored.OnActionAsync(new("playnite-library.variant", restoredTiles[0].Id));
        await restored.OnActionAsync(new("playnite-library.variant", restoredTiles[1].Id));
        Assert.AreEqual(0, restored.Organization.VariantGroups.Count);
        Assert.IsTrue(restoredHost.Authority.FavoriteGameIds.Contains("saved-00000"));
        await restored.OnActionAsync(new(
            "playnite-library.organization.reset", "playnite-library.organization.reset"));
        Assert.IsTrue(restoredHost.Authority.FavoriteGameIds.Contains("saved-00000"));
        Assert.IsTrue(Nodes(Snapshot(restored, 14).Root).Any(node =>
            node.ActionId == "playnite-library.launch" &&
            (node.AccessibilityLabel ?? string.Empty).Contains(
                "Favorite", StringComparison.Ordinal)));
        await Background(restored);
    }

    [TestMethod]
    public async Task CasConflictReappliesOnlyRequestedFavoriteDelta()
    {
        var a = new PlayniteLibraryDisplayItem("saved-a", "A", "Steam");
        var b = new PlayniteLibraryDisplayItem("saved-b", "B", "Steam");
        var c = new PlayniteLibraryDisplayItem("saved-c", "C", "Windows");
        var d = new PlayniteLibraryDisplayItem("saved-d", "D", "Windows");
        var baseline = new PlayniteLibraryPrivateState(PlayniteLibraryPrivateState.CurrentVersion,
            [a, b, c, d])
        {
            FavoriteSavedIds = [a.SavedId],
        };
        var latest = baseline with
        {
            FavoriteSavedIds = [a.SavedId, c.SavedId],
            VariantGroups =
            [
                new(PlayniteLibraryIdentity.GroupId(c.SavedId, d.SavedId),
                    [c.SavedId, d.SavedId], d.SavedId),
            ],
        };
        PlayniteLibraryPrivateState? written = null;
        var writes = 0;

        var result = await PlayniteLibraryStateStore.SaveAsync(
            state => PlayniteLibraryOrganizationPolicy.SetFavorite(state, b, favorite: true),
            (state, _, _) =>
            {
                if (writes++ == 0)
                    return ValueTask.FromException<WidgetPrivateStateMutation>(
                        new WidgetCapabilityException("state_conflict", "conflict"));
                written = state;
                return ValueTask.FromResult(new WidgetPrivateStateMutation(3));
            },
            _ => ValueTask.FromResult(new WidgetPrivateStateValue<PlayniteLibraryPrivateState>(
                true, latest, 2)),
            baseline, 1, CancellationToken.None);

        Assert.IsTrue(result.Saved);
        CollectionAssert.AreEqual(
            new[] { a.SavedId, c.SavedId, b.SavedId },
            written!.FavoriteSavedIds.ToArray());
        Assert.AreEqual(1, written.VariantGroups.Count);
        CollectionAssert.AreEqual(new[] { c.SavedId, d.SavedId },
            written.VariantGroups[0].SavedIds.ToArray());
    }

    [TestMethod]
    public void SourceRevisionReplacementUsesOnlyExactSavedIdentity()
    {
        var old = new PlayniteLibraryDisplayItem("saved-a", "Old title", "Steam");
        var state = new PlayniteLibraryPrivateState(PlayniteLibraryPrivateState.CurrentVersion, [old])
        {
            FavoriteSavedIds = [old.SavedId],
        };
        var sameIdentity = PlayniteLibraryItem.From(InstalledItem(
            "app-new", old.SavedId, "Updated title", WidgetAppLibraryKind.Game,
            "source-steam", "Steam"));
        var refreshed = PlayniteLibraryOrganizationPolicy.ProjectPage(state, [sameIdentity]);
        Assert.AreEqual("Updated title",
            refreshed.Items.Single(item => item.SavedId == old.SavedId).DisplayName);
        Assert.IsTrue(refreshed.FavoriteSavedIds.Contains(old.SavedId));

        var replacement = PlayniteLibraryItem.From(InstalledItem(
            "app-replacement", "saved-replacement", "Updated title",
            WidgetAppLibraryKind.Game, "source-steam", "Steam"));
        var replaced = PlayniteLibraryOrganizationPolicy.ProjectPage(refreshed, [replacement]);
        Assert.IsTrue(replaced.FavoriteSavedIds.Contains(old.SavedId));
        Assert.IsFalse(replaced.FavoriteSavedIds.Contains("saved-replacement"));
        Assert.IsTrue(replaced.Items.Any(item => item.SavedId == old.SavedId));
        Assert.IsTrue(replaced.Items.Any(item => item.SavedId == "saved-replacement"));
    }

    [TestMethod, Timeout(30_000)]
    public async Task IncompatibleStateResetsWholeSchemaBeforeReconciliation()
    {
        var legacyJson = """
            {"Version":4,"Items":[{"SavedId":"saved-stale","DisplayName":"Stale","SourceAttribution":"Old"}],"FavoriteSavedIds":["saved-stale"],"RecentSavedIds":["saved-stale"],"ManualSavedIds":["saved-stale"],"ExcludedSavedIds":["saved-stale"]}
            """;
        var state = new WidgetTestPrivateState(legacyJson, 1);
        var page = new TaskCompletionSource<WidgetAppLibraryPage>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var host = new FakeHost(1, state)
        {
            QueryHandler = (_, token) => new(page.Task.WaitAsync(token)),
        };
        var widget = Create(host);
        await Interactive(widget);
        await Bounded(host.FirstQueryStarted.Task, "legacy reset query admission");
        await Bounded(widget.WhenWarmStateIdleAsync(), "legacy whole-state reset");

        var reset = JsonSerializer.Deserialize<PlayniteLibraryPrivateState>(state.Json!);
        Assert.AreEqual(PlayniteLibraryPrivateState.CurrentVersion, reset!.Version);
        Assert.AreEqual(0, reset.Items.Count);
        Assert.AreEqual(0, reset.FavoriteSavedIds.Count);
        Assert.AreEqual(0, reset.RecentSavedIds.Count);
        Assert.AreEqual(0, reset.ManualSavedIds.Count);
        Assert.AreEqual(0, reset.ExcludedSavedIds.Count);
        Assert.IsFalse(Nodes(Snapshot(widget, 13).Root).Any(node =>
            (node.Text ?? string.Empty).Contains("Stale", StringComparison.Ordinal)));

        page.SetResult(new([Item(0)], null, null, "revision-1"));
        await Bounded(widget.WhenLibraryIdleAsync(), "authoritative reconciliation");

        Assert.AreEqual(PlayniteLibraryPrivateState.CurrentVersion, widget.Organization.Version);
        Assert.IsFalse(widget.Organization.Items.Any(item => item.SavedId == "saved-stale"));
        Assert.AreEqual(0, widget.Organization.FavoriteSavedIds.Count);
        Assert.AreEqual(0, widget.Organization.VariantGroups.Count);
        Assert.AreEqual(0, widget.Organization.RecentSavedIds.Count);
        Assert.AreEqual(0, widget.Organization.ManualSavedIds.Count);
        Assert.AreEqual(0, widget.Organization.ExcludedSavedIds.Count);
        Assert.IsFalse(Nodes(Snapshot(widget, 14).Root).Any(node =>
            (node.Text ?? string.Empty).Contains("Stale", StringComparison.Ordinal)));
        await Background(widget);
    }

    [TestMethod]
    public async Task FailedPersistenceLeavesCommittedOrganizationUnchanged()
    {
        var display = new PlayniteLibraryDisplayItem("saved-a", "A", "Steam");
        var baseline = new PlayniteLibraryPrivateState(
            PlayniteLibraryPrivateState.CurrentVersion, [display]);
        await Assert.ThrowsExactlyAsync<IOException>(async () =>
            await PlayniteLibraryStateStore.SaveAsync(
                state => PlayniteLibraryOrganizationPolicy.SetFavorite(
                    state, display, favorite: true),
                (_, _, _) => ValueTask.FromException<WidgetPrivateStateMutation>(
                    new IOException("fixture")),
                _ => ValueTask.FromResult(new WidgetPrivateStateValue<PlayniteLibraryPrivateState>(
                    true, baseline, 1)),
                baseline, 1, CancellationToken.None));
        Assert.AreEqual(0, baseline.FavoriteSavedIds.Count);
    }

    [TestMethod, Timeout(30_000)]
    public async Task AddGamesRouteShowsAllTrustedKindsAndRestoresLibraryFocus()
    {
        var host = new FakeHost(3)
        {
            ItemFactory = index => WithPresentation(Item(index), kind: index switch
                {
                    0 => WidgetAppLibraryKind.Game,
                    1 => WidgetAppLibraryKind.Application,
                    _ => WidgetAppLibraryKind.Unknown,
                }),
        };
        var widget = Create(host);
        await Interactive(widget);
        await Ready(widget, host);

        await widget.OnActionAsync(new("playnite-library.add.open", "playnite-library.add.open"));
        await Bounded(widget.WhenLibraryIdleAsync(), "add-games route load");
        Assert.IsNull(host.Queries[^1].Query.Kind);
        var addSnapshot = Snapshot(widget, 70);
        var automatic = Nodes(addSnapshot.Root).Single(node =>
            node.ActionId == "playnite-library.manual.included");
        Assert.IsTrue(automatic.IsDisabled);
        StringAssert.Contains(automatic.AccessibilityLabel!, "Game");
        var addTiles = Nodes(addSnapshot.Root)
            .Where(node => node.ActionId == "playnite-library.manual.toggle").ToArray();
        Assert.AreEqual(2, addTiles.Length);
        StringAssert.Contains(addTiles[0].AccessibilityLabel!, "Application");
        StringAssert.Contains(addTiles[1].AccessibilityLabel!, "Unknown");

        await widget.OnActionAsync(new("playnite-library.manual.toggle", addTiles[0].Id));
        CollectionAssert.AreEqual(new[] { "saved-00001" },
            widget.Organization.ManualSavedIds.ToArray());
        await widget.OnActionAsync(new("playnite-library.manual.toggle", addTiles[0].Id));
        Assert.AreEqual(0, widget.Organization.ManualSavedIds.Count);

        await widget.OnActionAsync(new("playnite-library.add.back", "playnite-library.add.back"));
        await Bounded(widget.WhenLibraryIdleAsync(), "library route restore");
        Assert.AreEqual("playnite-library.add.open", Snapshot(widget, 71).InitialFocusId);
        Assert.AreEqual(WidgetAppLibraryKind.Game, host.Queries[^1].Query.Kind);
        await Background(widget);
    }

    [TestMethod, Timeout(30_000)]
    public async Task RunningRouteConfirmsExactObservationBeforeManualAdd()
    {
        var host = new FakeHost(1)
        {
            RunningObservation = new([
                new("saved-00000", "Automatic game", WidgetAppLibraryKind.Game, "Steam"),
                new("saved-running", "Visible app", WidgetAppLibraryKind.Application,
                    "Windows"),
            ], "running-revision"),
            ConfirmRunningHandler = request => request.Revision == "running-revision"
                ? InstalledItem(
                    "app-current-running", request.SavedId, "Visible app",
                    WidgetAppLibraryKind.Application, "source-windows", "Windows")
                : null,
        };
        var widget = Create(host);
        await Interactive(widget);
        await Ready(widget, host);

        await widget.OnActionAsync(new(
            "playnite-library.running.open", "playnite-library.running.open"));
        await Bounded(widget.WhenLibraryIdleAsync(), "running route load");
        var snapshot = Snapshot(widget, 700);
        var automatic = Nodes(snapshot.Root).Single(node =>
            node.ActionId == "playnite-library.manual.included");
        Assert.IsTrue(automatic.IsDisabled);
        var available = Nodes(snapshot.Root).Single(node =>
            node.ActionId == "playnite-library.manual.toggle");
        await widget.OnActionAsync(new("playnite-library.manual.toggle", available.Id));

        Assert.AreEqual(1, host.RunningConfirmations.Count);
        Assert.AreEqual("saved-running", host.RunningConfirmations[0].SavedId);
        Assert.AreEqual("running-revision", host.RunningConfirmations[0].Revision);
        CollectionAssert.AreEqual(new[] { "saved-running" },
            widget.Organization.ManualSavedIds.ToArray());

        host.RunningObservation = new([], "replacement-revision");
        await widget.OnActionAsync(new("playnite-library.refresh", "playnite-library.refresh"));
        await Bounded(widget.WhenLibraryIdleAsync(), "running route refresh");
        Assert.AreEqual(0, widget.Collection.Items.Count);
        await Background(widget);
    }

    [TestMethod, Timeout(30_000)]
    public async Task MalformedRunningConfirmationCannotMutateOrganization()
    {
        var state = new WidgetTestPrivateState();
        var host = new FakeHost(1, state)
        {
            RunningObservation = new([
                new("saved-running", "Visible app", WidgetAppLibraryKind.Application,
                    "Windows"),
            ], "running-revision"),
            ConfirmRunningHandler = request => WithPresentation(
                InstalledItem(
                    "app-current-running", request.SavedId, "Visible app",
                    WidgetAppLibraryKind.Application, "source-windows", "Windows"),
                source: "bad\nsource"),
        };
        var widget = Create(host);
        await Interactive(widget);
        await Ready(widget, host);
        var revision = state.Revision;
        var retained = widget.Organization.Items
            .Select(item => item.SavedId).ToArray();

        await widget.OnActionAsync(new(
            "playnite-library.running.open", "playnite-library.running.open"));
        await Bounded(widget.WhenLibraryIdleAsync(), "running route load");
        var available = Nodes(Snapshot(widget, 700).Root).Single(node =>
            node.ActionId == "playnite-library.manual.toggle");
        WidgetCapabilityException? failure = null;
        try
        {
            await widget.OnActionAsync(new(
                "playnite-library.manual.toggle", available.Id));
        }
        catch (WidgetCapabilityException exception)
        {
            failure = exception;
        }

        Assert.AreEqual("malformed_response", failure?.ErrorCode);
        Assert.AreEqual(revision, state.Revision);
        CollectionAssert.AreEqual(retained,
            widget.Organization.Items.Select(item => item.SavedId).ToArray());
        Assert.AreEqual(0, widget.Organization.ManualSavedIds.Count);
        await Background(widget);
    }

    [TestMethod, Timeout(30_000)]
    public async Task ManualEntrySurvivesRestartButLaunchStillRevalidatesExactSavedId()
    {
        var state = new WidgetTestPrivateState();
        var host = new FakeHost(2, state)
        {
            ItemFactory = index => WithPresentation(Item(index), kind: index == 1
                    ? WidgetAppLibraryKind.Application
                    : WidgetAppLibraryKind.Game),
        };
        var widget = Create(host);
        await Interactive(widget);
        await Ready(widget, host);
        await widget.OnActionAsync(new("playnite-library.add.open", "playnite-library.add.open"));
        await Bounded(widget.WhenLibraryIdleAsync(), "manual add route");
        var application = Nodes(Snapshot(widget, 72).Root)
            .Single(node => node.ActionId == "playnite-library.manual.toggle" &&
                node.AccessibilityLabel!.Contains("Application", StringComparison.Ordinal));
        await widget.OnActionAsync(new("playnite-library.manual.toggle", application.Id));
        await Background(widget);

        var restartedHost = new FakeHost(1, state)
        {
            ResolveHandler = request => request.SavedIds.Select(_ =>
                WithPresentation(Item(1) with { AppId = "app-fresh-manual" },
                    kind: WidgetAppLibraryKind.Application)).ToArray(),
            LaunchHandler = (_, _) => ValueTask.FromResult(new WidgetAppLaunchObservation(
                WidgetAppLaunchObservationState.LauncherStarted, false, false)),
        };
        var restarted = Create(restartedHost);
        await Interactive(restarted);
        await Ready(restarted, restartedHost);
        var manualTile = Nodes(Snapshot(restarted, 73).Root)
            .Single(node => node.ActionId == "playnite-library.launch" &&
                node.AccessibilityLabel!.Contains("Game 00001", StringComparison.Ordinal));
        await restarted.OnActionAsync(new("playnite-library.launch", manualTile.Id));

        CollectionAssert.AreEqual(new[] { "saved-00001" },
            restartedHost.ResolveRequests[^1].ToArray());
        CollectionAssert.AreEqual(new[] { "app-fresh-manual" },
            restartedHost.Launches.ToArray());
        await Background(restarted);
    }

    [TestMethod, Timeout(30_000)]
    public async Task FullProviderPageAndMaximumFixedSlicesStayIndependentlyBounded()
    {
        var recentIds = Enumerable.Range(64, PlayniteLibraryPrivateState.MaximumRecentItems)
            .Reverse().Select(index => $"saved-{index:D5}").ToArray();
        var manualIds = Enumerable.Range(96, PlayniteLibraryPrivateState.MaximumManualItems)
            .Select(index => $"saved-{index:D5}").ToArray();
        var displays = recentIds.Concat(manualIds).Select(savedId =>
        {
            var index = int.Parse(savedId.AsSpan(savedId.LastIndexOf('-') + 1));
            return new PlayniteLibraryDisplayItem(savedId, $"Game {index:D5}",
                index % 2 == 0 ? "Steam" : "Windows");
        }).ToArray();
        var persisted = new PlayniteLibraryPrivateState(
            PlayniteLibraryPrivateState.CurrentVersion, displays)
        {
            RecentSavedIds = recentIds,
            ManualSavedIds = manualIds,
            FavoriteSavedIds = [recentIds[0], manualIds[0]],
        };
        var host = new FakeHost(LauncherWidget.PageSize, new WidgetTestPrivateState(
            JsonSerializer.Serialize(persisted), 1))
        {
            ItemFactory = index => WithPresentation(Item(index), kind: index >= 96
                    ? WidgetAppLibraryKind.Application
                    : WidgetAppLibraryKind.Game),
        };
        var widget = Create(host);
        await Interactive(widget);
        await Ready(widget, host);

        Assert.AreEqual(LauncherWidget.PageSize, widget.Collection.Items.Count,
            "fixed rows must not enter the cursor page");
        await widget.OnActionAsync(new(
            "playnite-library.filter.recent", "playnite-library.filter.recent"));
        await Bounded(widget.WhenLibraryIdleAsync(), "maximum fixed-slice reload");
        Assert.AreEqual(0, widget.Collection.Items.Count,
            "Recent is an exact collection, not a hidden recent-first intersection.");
        Assert.AreEqual(LauncherWidget.PageSize, host.MaximumRequestedLimit);
        CollectionAssert.AreEquivalent(recentIds.Concat(manualIds).ToArray(),
            host.ResolveRequests[^1].ToArray());

        var snapshot = Snapshot(widget, 75);
        var launches = Nodes(snapshot.Root)
            .Where(node => node.ActionId == "playnite-library.launch").ToArray();
        Assert.AreEqual(PlayniteLibraryPrivateState.MaximumRecentItems, launches.Length);
        Assert.AreEqual(0,
            launches.Count(node => node.CollectionItemKey is not null),
            "only provider rows participate in cursor anchor accounting");
        Assert.AreEqual(PlayniteLibraryIdentity.FocusId("grid",
            PlayniteLibraryIdentity.Key(recentIds[0])), launches[0].Id);
        Assert.AreEqual(launches.Length,
            launches.Select(node => node.Id).Distinct(StringComparer.Ordinal).Count());

        await widget.OnActionAsync(new(
            "playnite-library.filter.sort", "playnite-library.filter.sort"));
        await Bounded(widget.WhenLibraryIdleAsync(), "fixed-slice descending sort");
        var sortedFixed = Nodes(Snapshot(widget, 751).Root)
            .Where(node => node.ActionId == "playnite-library.launch" &&
                node.CollectionItemKey is null).ToArray();
        Assert.AreEqual(PlayniteLibraryIdentity.FocusId("grid",
            PlayniteLibraryIdentity.Key(recentIds[0])), sortedFixed[0].Id,
            "recent order remains exact instead of following catalog sort");

        var manualOption = Nodes(Snapshot(widget, 7511).Root).Single(node =>
            node.ActionId?.StartsWith(PlayniteLibraryCollectionPolicy.ActionPrefix,
                StringComparison.Ordinal) == true &&
            Nodes(node).Any(child =>
                (child.Text ?? string.Empty).StartsWith("Manual (32)  ", StringComparison.Ordinal)));
        await widget.OnActionAsync(new(manualOption.ActionId!, manualOption.Id));
        await Bounded(widget.WhenLibraryIdleAsync(), "manual fixed-slice collection");
        sortedFixed = Nodes(Snapshot(widget, 7512).Root)
            .Where(node => node.ActionId == "playnite-library.launch" &&
                node.CollectionItemKey is null).ToArray();
        Assert.AreEqual(PlayniteLibraryPrivateState.MaximumManualItems, sortedFixed.Length);
        Assert.AreEqual(PlayniteLibraryIdentity.FocusId("grid",
            PlayniteLibraryIdentity.Key(manualIds[^1])),
            sortedFixed[0].Id,
            "manual fixed rows follow the selected display sort");

        await widget.OnActionAsync(new(
            "playnite-library.filter.favorites", "playnite-library.filter.favorites"));
        await Bounded(widget.WhenLibraryIdleAsync(), "fixed-slice favorite collection");
        var favoriteFixed = Nodes(Snapshot(widget, 753).Root)
            .Where(node => node.ActionId == "playnite-library.launch" &&
                node.CollectionItemKey is null).ToArray();
        Assert.AreEqual(1, favoriteFixed.Length);
        Assert.AreEqual(PlayniteLibraryIdentity.FocusId("grid",
            PlayniteLibraryIdentity.Key(manualIds[0])), favoriteFixed[0].Id);
        await Background(widget);
    }

    [TestMethod, Timeout(30_000)]
    public async Task LaterPageRecentLeadsColdFirstPageAndDeduplicatesTraversal()
    {
        var state = new WidgetTestPrivateState();
        var host = new FakeHost(130, state)
        {
            LaunchHandler = (_, _) => ValueTask.FromResult(new WidgetAppLaunchObservation(
                WidgetAppLaunchObservationState.LauncherStarted, false, false)),
        };
        var widget = Create(host);
        await Interactive(widget);
        await Ready(widget, host);
        await widget.OnActionAsync(new("playnite-library.next", "playnite-library.next"));
        await Bounded(widget.WhenLibraryIdleAsync(), "later catalog page");
        var laterId = PlayniteLibraryIdentity.FocusId(
            "grid", PlayniteLibraryIdentity.Key("saved-00100"));
        await widget.OnActionAsync(new("playnite-library.launch", laterId));
        CollectionAssert.AreEqual(new[] { "saved-00100" },
            widget.Organization.RecentSavedIds.ToArray());
        await Background(widget);

        var restartedHost = new FakeHost(130, state)
        {
            LaunchHandler = (_, _) => ValueTask.FromResult(new WidgetAppLaunchObservation(
                WidgetAppLaunchObservationState.LauncherStarted, false, false)),
        };
        var restarted = Create(restartedHost);
        await Interactive(restarted);
        await Ready(restarted, restartedHost);
        Assert.IsFalse(restarted.Collection.Items.Any(item =>
            item.Value.SavedId == "saved-00100"));
        await restarted.OnActionAsync(new(
            "playnite-library.filter.recent", "playnite-library.filter.recent"));
        await Bounded(restarted.WhenLibraryIdleAsync(), "cold recent-first resolution");
        var first = Nodes(Snapshot(restarted, 76).Root)
            .First(node => node.ActionId == "playnite-library.launch");
        Assert.AreEqual(laterId, first.Id);
        await restarted.OnActionAsync(new("playnite-library.launch", first.Id));
        CollectionAssert.AreEqual(new[] { "saved-00100" },
            restartedHost.ResolveRequests[^1].ToArray());
        CollectionAssert.AreEqual(new[] { "app-00100" }, restartedHost.Launches.ToArray());

        await restarted.OnActionAsync(new("playnite-library.next", "playnite-library.next"));
        await Bounded(restarted.WhenLibraryIdleAsync(), "recent catalog overlap traversal");
        Assert.AreEqual(1, Nodes(Snapshot(restarted, 77).Root).Count(node =>
            node.ActionId == "playnite-library.launch" && node.Id == laterId));
        await Background(restarted);
    }

    [TestMethod, Timeout(30_000)]
    public async Task AutomaticGamesAreIncludedAndCannotRetainManualMembership()
    {
        var display = new PlayniteLibraryDisplayItem(
            "saved-00000", "Game 00000", "Steam");
        var persisted = new PlayniteLibraryPrivateState(
            PlayniteLibraryPrivateState.CurrentVersion, [display])
        {
            ManualSavedIds = [display.SavedId],
            FavoriteSavedIds = [display.SavedId],
            RecentSavedIds = [display.SavedId],
        };
        var host = new FakeHost(1, new WidgetTestPrivateState(
            JsonSerializer.Serialize(persisted), 1));
        var widget = Create(host);
        await Interactive(widget);
        await Ready(widget, host);
        Assert.AreEqual(0, widget.Organization.ManualSavedIds.Count,
            "fresh classification removes obsolete manual Game membership");
        CollectionAssert.AreEqual(new[] { display.SavedId },
            widget.Organization.FavoriteSavedIds.ToArray(),
            "automatic membership cleanup must preserve unrelated favorites");
        CollectionAssert.AreEqual(new[] { display.SavedId },
            widget.Organization.RecentSavedIds.ToArray(),
            "automatic membership cleanup must preserve unrelated recents");

        await widget.OnActionAsync(new("playnite-library.add.open", "playnite-library.add.open"));
        await Bounded(widget.WhenLibraryIdleAsync(), "automatic game add route");
        var automatic = Nodes(Snapshot(widget, 78).Root).Single(node =>
            node.ActionId == "playnite-library.manual.included");
        Assert.IsTrue(automatic.IsDisabled);
        StringAssert.Contains(automatic.AccessibilityLabel!, "Included automatically");
        await widget.OnActionAsync(new("playnite-library.manual.toggle", automatic.Id));
        Assert.AreEqual(0, widget.Organization.ManualSavedIds.Count);
        await Background(widget);
    }

    [TestMethod]
    public async Task ManualCasReplayPreservesFavoritesGroupsAndRecentOrder()
    {
        var a = new PlayniteLibraryDisplayItem("saved-a", "A", "Steam");
        var b = new PlayniteLibraryDisplayItem("saved-b", "B", "Windows");
        var c = new PlayniteLibraryDisplayItem("saved-c", "C", "Xbox");
        var baseline = new PlayniteLibraryPrivateState(
            PlayniteLibraryPrivateState.CurrentVersion, [a, b])
        {
            FavoriteSavedIds = [a.SavedId],
            RecentSavedIds = [b.SavedId],
        };
        var latest = baseline with
        {
            Items = [c, a, b],
            RecentSavedIds = [c.SavedId, b.SavedId],
        };
        PlayniteLibraryPrivateState? written = null;
        var attempt = 0;
        await PlayniteLibraryStateStore.SaveAsync(
            state => PlayniteLibraryOrganizationPolicy.SetManual(state, a, true),
            (value, _, _) =>
            {
                if (attempt++ == 0)
                    return ValueTask.FromException<WidgetPrivateStateMutation>(
                        new WidgetCapabilityException("state_conflict", "fixture"));
                written = value;
                return ValueTask.FromResult(new WidgetPrivateStateMutation(3));
            },
            _ => ValueTask.FromResult(new WidgetPrivateStateValue<PlayniteLibraryPrivateState>(
                true, latest, 2)), baseline, 1, CancellationToken.None);

        CollectionAssert.AreEqual(new[] { a.SavedId }, written!.ManualSavedIds.ToArray());
        CollectionAssert.AreEqual(new[] { a.SavedId }, written.FavoriteSavedIds.ToArray());
        CollectionAssert.AreEqual(new[] { c.SavedId, b.SavedId },
            written.RecentSavedIds.ToArray());
    }

    [TestMethod]
    public void ManualMembershipIsBoundedAndReplacementIdentityIsIndependent()
    {
        var state = PlayniteLibraryPrivateState.Empty;
        for (var index = 0; index < PlayniteLibraryPrivateState.MaximumManualItems; index++)
        {
            var display = new PlayniteLibraryDisplayItem(
                $"saved-{index:D5}", $"Game {index}", "Fixture");
            var mutation = PlayniteLibraryOrganizationPolicy.SetManual(state, display, true);
            Assert.IsTrue(mutation.Accepted);
            state = mutation.State;
        }
        var overflow = PlayniteLibraryOrganizationPolicy.SetManual(state,
            new("saved-overflow", "Overflow", "Fixture"), true);
        Assert.IsFalse(overflow.Accepted);
        Assert.AreEqual(PlayniteLibraryPrivateState.MaximumManualItems,
            overflow.State.ManualSavedIds.Count);

        var replacement = PlayniteLibraryOrganizationPolicy.ProjectPage(state,
            [PlayniteLibraryItem.From(Item(0) with { SavedId = "saved-replacement" })]);
        Assert.IsFalse(replacement.ManualSavedIds.Contains(
            "saved-replacement", StringComparer.Ordinal));
    }

    [TestMethod, Timeout(30_000)]
    public async Task MissingManualEntryStaysVisibleButCannotAuthorizeLaunch()
    {
        var display = new PlayniteLibraryDisplayItem(
            "saved-00999", "Unavailable manual game", "Fixture");
        var persisted = new PlayniteLibraryPrivateState(
            PlayniteLibraryPrivateState.CurrentVersion, [display])
        {
            ManualSavedIds = [display.SavedId],
        };
        var state = new WidgetTestPrivateState(JsonSerializer.Serialize(persisted), 1);
        var host = new FakeHost(1, state) { ResolveHandler = _ => [] };
        var widget = Create(host);
        await Interactive(widget);
        await Ready(widget, host);

        var unavailable = Nodes(Snapshot(widget, 74).Root).Single(node =>
            node.ActionId == "playnite-library.launch" &&
            node.AccessibilityLabel!.Contains("Unavailable manual game",
                StringComparison.Ordinal));
        Assert.IsTrue(unavailable.IsDisabled);
        await widget.OnActionAsync(new("playnite-library.launch", unavailable.Id));
        Assert.AreEqual(0, host.Launches.Count);
        await Background(widget);
    }

    [TestMethod, Timeout(30_000)]
    public async Task ExactLaunchRevalidatesSavedIdentity()
    {
        var host = new FakeHost(1)
        {
            ResolveHandler = request =>
                [Item(0) with { AppId = "app-current" }],
            LaunchHandler = (_, _) => ValueTask.FromResult(new WidgetAppLaunchObservation(
                WidgetAppLaunchObservationState.LauncherStarted, false, false)),
        };
        var widget = Create(host);
        await Interactive(widget);
        await Ready(widget, host);
        var tile = Nodes(widget.RenderSnapshot("launcher.test", 2).Root)
            .Single(node => node.ActionId == "playnite-library.launch");

        await widget.OnActionAsync(new("playnite-library.launch", tile.Id));

        CollectionAssert.AreEqual(new[] { "saved-00000" }, host.ResolveRequests[^1].ToArray());
        CollectionAssert.AreEqual(new[] { "app-current" }, host.Launches.ToArray());
        CollectionAssert.AreEqual(new[] { "saved-00000" },
            widget.Organization.RecentSavedIds.ToArray());
        await Background(widget);
    }

    [TestMethod, Timeout(30_000)]
    public async Task AcceptedLaunchesDriveBoundedRecentOrderingAndFilter()
    {
        var host = new FakeHost(40)
        {
            LaunchHandler = (_, _) => ValueTask.FromResult(new WidgetAppLaunchObservation(
                WidgetAppLaunchObservationState.LauncherStarted, false, false)),
        };
        var widget = Create(host);
        await Interactive(widget);
        await Ready(widget, host);

        for (var index = 0; index < 35; index++)
        {
            var tileId = PlayniteLibraryIdentity.FocusId("grid",
                PlayniteLibraryIdentity.Key($"saved-{index:D5}"));
            await widget.OnActionAsync(new("playnite-library.launch", tileId));
        }

        Assert.AreEqual(PlayniteLibraryPrivateState.MaximumRecentItems,
            widget.Organization.RecentSavedIds.Count);
        Assert.AreEqual("saved-00034", widget.Organization.RecentSavedIds[0]);
        Assert.AreEqual("saved-00003", widget.Organization.RecentSavedIds[^1]);

        await widget.OnActionAsync(new("playnite-library.filter.recent",
            "playnite-library.filter.recent"));
        await Bounded(widget.WhenLibraryIdleAsync(), "recent collection reload");
        var first = Nodes(Snapshot(widget, 60).Root)
            .First(node => node.ActionId == "playnite-library.launch");
        Assert.AreEqual(PlayniteLibraryIdentity.FocusId("grid",
            PlayniteLibraryIdentity.Key("saved-00034")), first.Id);
        CollectionAssert.AreEqual(widget.Organization.RecentSavedIds.ToArray(),
            host.Queries[^1].Query.FavoriteSavedIds.ToArray());

        await widget.OnActionAsync(new("playnite-library.filter.recent",
            "playnite-library.filter.recent"));
        await Bounded(widget.WhenLibraryIdleAsync(), "all collection reload");
        Assert.AreEqual(0, host.Queries[^1].Query.FavoriteSavedIds.Count);
        await Background(widget);
    }

    [TestMethod, Timeout(30_000)]
    public async Task ImmediateRecentPromotionRetainsCurrentItemAndRelaunchesExactly()
    {
        const string artwork = "library.art.0123456789abcdef0123456789abcdef";
        var resolveGeneration = 0;
        var host = new FakeHost(3)
        {
            ItemFactory = index => WithPresentation(Item(index),
                displayName: $"Current game {index}", source: "Current catalog",
                artworkHandle: artwork),
            LaunchHandler = (_, _) => ValueTask.FromResult(new WidgetAppLaunchObservation(
                WidgetAppLaunchObservationState.LauncherStarted, false, false)),
        };
        host.ResolveHandler = request => request.SavedIds.Select(savedId =>
        {
            var index = int.Parse(savedId.AsSpan(savedId.LastIndexOf('-') + 1));
            return WithPresentation(
                host.ItemFactory(index) with
                    { AppId = $"fresh-app-{++resolveGeneration}" },
                displayName: $"Resolved game {index}", source: "Resolved catalog");
        }).ToArray();
        var widget = Create(host);
        await Interactive(widget);
        await Ready(widget, host);
        await widget.OnActionAsync(new(
            "playnite-library.filter.recent", "playnite-library.filter.recent"));
        await Bounded(widget.WhenLibraryIdleAsync(), "empty recent-first reload");
        var queriesBeforeLaunch = host.Queries.Count;
        var promotedId = PlayniteLibraryIdentity.FocusId(
            "grid", PlayniteLibraryIdentity.Key("saved-00001"));

        await widget.OnActionAsync(new("playnite-library.launch", promotedId));

        var promotedSnapshot = Snapshot(widget, 601);
        var launchTiles = Nodes(promotedSnapshot.Root)
            .Where(node => node.ActionId == "playnite-library.launch").ToArray();
        Assert.AreEqual(3, launchTiles.Length);
        Assert.AreEqual(promotedId, launchTiles[0].Id);
        Assert.AreEqual(1, launchTiles.Count(node => node.Id == promotedId),
            "the promoted identity must not remain duplicated in the catalog section");
        Assert.IsFalse(launchTiles[0].IsDisabled ?? false);
        Assert.IsNull(launchTiles[0].CollectionItemKey,
            "the promoted fixed row must stay outside cursor anchor accounting");
        Assert.AreEqual(artwork,
            Nodes(launchTiles[0]).Single(node => node.ArtworkHandle is not null).ArtworkHandle);
        StringAssert.Contains(launchTiles[0].AccessibilityLabel!, "Current game 1");
        StringAssert.Contains(launchTiles[0].AccessibilityLabel!, "Current catalog");
        Assert.AreEqual(queriesBeforeLaunch, host.Queries.Count,
            "promotion must not reload the provider page");

        await widget.OnActionAsync(new("playnite-library.launch", promotedId));

        Assert.AreEqual(2, host.ResolveRequests.Count);
        CollectionAssert.AreEqual(new[] { "saved-00001" },
            host.ResolveRequests[0].ToArray());
        CollectionAssert.AreEqual(new[] { "saved-00001" },
            host.ResolveRequests[1].ToArray());
        CollectionAssert.AreEqual(new[] { "fresh-app-1", "fresh-app-2" },
            host.Launches.ToArray());
        await Background(widget);
    }

    [TestMethod, Timeout(30_000)]
    public async Task SourceHealthRetainsPartialRowsRejectsStaleAndRecovers()
    {
        var staleStarted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var currentReturned = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseStale = new TaskCompletionSource<WidgetAppLibraryPage>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        var host = new FakeHost(2)
        {
            QueryHandler = (_, _) => Interlocked.Increment(ref calls) switch
            {
                1 => ValueTask.FromResult(SourcePage(
                    [Source("source-windows", "Windows",
                        WidgetAppLibrarySourceHealth.Healthy, 1, "healthy"),
                     Source("source-steam", "Steam",
                        WidgetAppLibrarySourceHealth.Degraded, 1,
                        "source_degraded")], "revision-1")),
                2 => WaitForStaleSourcePage(),
                3 => ReturnRecoveredPage(),
                4 => ValueTask.FromResult(SourcePage(
                    [Source("source-windows", "Windows",
                        WidgetAppLibrarySourceHealth.Healthy, 3, "healthy")],
                    "revision-3")),
                _ => throw new InvalidOperationException("Unexpected source query."),
            },
        };
        var widget = Create(host);
        await Interactive(widget);
        await Ready(widget, host);

        var partial = Snapshot(widget, 602);
        StringAssert.Contains(Nodes(partial.Root).Single(node =>
            node.Id == "playnite-library.source.source-windows").Text!, "Healthy");
        StringAssert.Contains(Nodes(partial.Root).Single(node =>
            node.Id == "playnite-library.source.source-steam").Text!, "Degraded");
        Assert.IsTrue(Nodes(partial.Root).Where(node =>
            node.ActionId == "playnite-library.launch").All(node =>
                !(node.IsDisabled ?? false)),
            "partial source health must not suppress usable games");
        var initialFocus = partial.InitialFocusId;

        await widget.OnActionAsync(new("playnite-library.refresh", "playnite-library.refresh"));
        await Bounded(staleStarted.Task, "stale source refresh admission");
        var refreshing = Snapshot(widget, 603);
        Assert.AreEqual(2, Nodes(refreshing.Root).Count(node =>
            node.Id.StartsWith("playnite-library.source.", StringComparison.Ordinal) &&
            (node.Text ?? string.Empty).Contains("Refreshing", StringComparison.Ordinal)));

        await widget.OnActionAsync(new(
            "playnite-library.filter.source", "playnite-library.filter.source"));
        releaseStale.TrySetResult(SourcePage(
            [Source("source-windows", "Windows",
                WidgetAppLibrarySourceHealth.Healthy, 2, "healthy"),
             Source("source-steam", "Steam",
                WidgetAppLibrarySourceHealth.Unavailable, 2,
                "source_unavailable")], "revision-stale"));
        await Bounded(currentReturned.Task, "recovered source revision");
        await Bounded(widget.WhenLibraryIdleAsync(), "stale source drain");
        var recovered = Snapshot(widget, 604);
        StringAssert.Contains(Nodes(recovered.Root).Single(node =>
            node.Id == "playnite-library.source.source-steam").Text!, "Healthy");
        Assert.IsFalse(Nodes(recovered.Root).Any(node =>
            (node.Text ?? string.Empty).Contains("Unavailable", StringComparison.Ordinal)));
        Assert.AreEqual(initialFocus, recovered.InitialFocusId);

        await widget.OnActionAsync(new("playnite-library.refresh", "playnite-library.refresh"));
        await Bounded(widget.WhenLibraryIdleAsync(), "source disappearance refresh");
        var disappeared = Snapshot(widget, 605);
        Assert.IsFalse(Nodes(disappeared.Root).Any(node =>
            node.Id == "playnite-library.source.source-steam"));
        StringAssert.Contains(Nodes(disappeared.Root).Single(node =>
            node.Id == "playnite-library.source.source-windows").Text!, "Healthy");
        await Background(widget);

        ValueTask<WidgetAppLibraryPage> WaitForStaleSourcePage()
        {
            staleStarted.TrySetResult();
            return new(releaseStale.Task);
        }

        ValueTask<WidgetAppLibraryPage> ReturnRecoveredPage()
        {
            currentReturned.TrySetResult();
            return ValueTask.FromResult(SourcePage(
                [Source("source-windows", "Windows",
                    WidgetAppLibrarySourceHealth.Healthy, 2, "healthy"),
                 Source("source-steam", "Steam",
                    WidgetAppLibrarySourceHealth.Healthy, 2, "healthy")],
                "revision-2"));
        }

        WidgetAppLibraryPage SourcePage(
            IReadOnlyList<WidgetAppLibrarySource> sources,
            string revision) => new(
                [Item(0), Item(1)], null, null, revision)
            {
                Sources = sources,
            };

        static WidgetAppLibrarySource Source(
            string id, string label, WidgetAppLibrarySourceHealth health,
            long revision, string statusCode) =>
            new(id, label, health, revision, statusCode);
    }

    [TestMethod, Timeout(30_000)]
    public async Task AvailabilityStatesRemainDistinctNavigableAndRecoverLastGood()
    {
        var items = new[]
        {
            AvailabilityItem(0, WidgetAppLibraryAvailabilityState.Installed,
                true, "installed"),
            AvailabilityItem(1, WidgetAppLibraryAvailabilityState.Unavailable,
                false, "offline"),
            AvailabilityItem(2, WidgetAppLibraryAvailabilityState.StaleSource,
                false, "source_degraded"),
            AvailabilityItem(3, WidgetAppLibraryAvailabilityState.Installed,
                false, "source_disabled"),
            AvailabilityItem(4, WidgetAppLibraryAvailabilityState.Installed,
                true, "installed") with
            {
                Presentation = AvailabilityItem(
                    4, WidgetAppLibraryAvailabilityState.Installed,
                    true, "installed").Presentation with
                {
                    ActiveOperation = new(
                        "operation.0123456789abcdef0123456789abcdef",
                        WidgetAppLibraryOperationKind.Update,
                        WidgetAppLibraryOperationState.Running,
                        "running"),
                    Capabilities = new([
                        WidgetAppLibraryAction.Launch,
                        WidgetAppLibraryAction.Update,
                    ]),
                },
            },
        };
        var fail = false;
        var host = new FakeHost(0)
        {
            QueryHandler = (_, token) =>
            {
                token.ThrowIfCancellationRequested();
                return fail
                    ? ValueTask.FromException<WidgetAppLibraryPage>(
                        new WidgetCapabilityException("platform_unavailable", "private"))
                    : ValueTask.FromResult(new WidgetAppLibraryPage(
                        items, null, null, "availability")
                    {
                        Sources =
                        [
                            new("source-healthy", "Healthy Store",
                                WidgetAppLibrarySourceHealth.Healthy, 1, "healthy"),
                            new("source-degraded", "Degraded Store",
                                WidgetAppLibrarySourceHealth.Degraded, 1,
                                "source_degraded"),
                        ],
                    });
            },
        };
        var widget = Create(host);
        await Interactive(widget);
        await Bounded(widget.WhenLibraryIdleAsync(), "availability-state load");
        Assert.AreEqual(WidgetPagedResourceStatus.Ready, widget.Collection.Status,
            widget.Collection.Error is null ? "No resource error" :
                $"{widget.Collection.Error.Code}: {widget.Collection.Error.Message}");
        var ready = Snapshot(widget, 650);
        AssertTile(ready, 0, "Ready", disabled: false, busy: false);
        AssertTile(ready, 1, "Offline", disabled: true, busy: false);
        AssertTile(ready, 2, "Source degraded", disabled: true, busy: false);
        AssertTile(ready, 3, "Disabled", disabled: true, busy: false);
        AssertTile(ready, 4, "Busy", disabled: true, busy: true);
        StringAssert.Contains(Nodes(ready.Root).Single(node =>
            node.Id == "playnite-library.source.source-degraded").Text!, "Degraded");
        var focus = ready.InitialFocusId;

        fail = true;
        await widget.OnActionAsync(new("playnite-library.refresh", "playnite-library.refresh"));
        await Bounded(widget.WhenLibraryIdleAsync(), "offline last-good refresh");
        var offline = Snapshot(widget, 651);
        Assert.AreEqual(5, Nodes(offline.Root).Count(node =>
            node.ActionId == "playnite-library.launch"));
        Assert.AreEqual(focus, offline.InitialFocusId);
        var retained = Nodes(offline.Root).Single(node =>
            node.Id == "playnite-library.retained-error");
        Assert.IsTrue(Nodes(retained).Any(node =>
            (node.Text ?? string.Empty).Contains("Game library offline",
                StringComparison.Ordinal)));
        Assert.IsFalse(Nodes(offline.Root).Any(node => node.Id == "playnite-library.empty"));

        fail = false;
        await widget.OnActionAsync(new("playnite-library.retry", "playnite-library.retry"));
        await Bounded(widget.WhenLibraryIdleAsync(), "offline retry recovery");
        var recovered = Snapshot(widget, 652);
        Assert.IsFalse(Nodes(recovered.Root).Any(node =>
            node.Id == "playnite-library.retained-error"));
        Assert.AreEqual(focus, recovered.InitialFocusId);
        await Background(widget);

        static WidgetAppLibraryItem AvailabilityItem(
            int index,
            WidgetAppLibraryAvailabilityState state,
            bool launchable,
            string statusCode)
        {
            var item = Item(index);
            return item with
            {
                Presentation = item.Presentation with
                {
                    Availability = new(state, launchable, statusCode),
                    Capabilities = launchable
                        ? new([WidgetAppLibraryAction.Launch])
                        : new([]),
                },
            };
        }

        static void AssertTile(
            ViewSnapshot snapshot,
            int index,
            string reason,
            bool disabled,
            bool busy)
        {
            var tile = Nodes(snapshot.Root).Single(node =>
                node.Id == PlayniteLibraryIdentity.FocusId(
                    "grid", PlayniteLibraryIdentity.Key($"saved-{index:D5}")));
            StringAssert.Contains(tile.AccessibilityLabel!, reason);
            Assert.AreEqual(disabled, tile.IsDisabled == true);
            Assert.AreEqual(busy, tile.IsBusy == true);
            Assert.IsNotNull(tile.CollectionItemKey,
                "Disabled and busy tiles must remain in controller navigation.");
        }
    }

    [TestMethod, Timeout(30_000)]
    public async Task OwnedNotInstalledRowsStayNavigableNonLaunchingAndCapabilityTruthful()
    {
        var installed = Item(0);
        var installable = OwnedItem(1, "owned_not_installed",
            [WidgetAppLibraryAction.Install]);
        var signedOut = OwnedItem(2, "owned_signed_out", []);
        var unsupported = OwnedItem(3, "owned_unsupported", []);
        var stale = OwnedItem(4, "owned_not_installed",
            [WidgetAppLibraryAction.Install]) with
        {
            Presentation = OwnedItem(4, "owned_not_installed",
                [WidgetAppLibraryAction.Install]).Presentation with
            {
                Availability = new(
                    WidgetAppLibraryAvailabilityState.StaleSource,
                    false,
                    "owned_not_installed"),
            },
        };
        var items = new[] { installed, installable, signedOut, unsupported, stale };
        var host = new FakeHost(0)
        {
            QueryHandler = (_, token) =>
            {
                token.ThrowIfCancellationRequested();
                return ValueTask.FromResult(new WidgetAppLibraryPage(
                    items, null, null, "mixed-owned"));
            },
            ResolveHandler = request => items.Where(item =>
                request.SavedIds.Contains(item.SavedId, StringComparer.Ordinal)).ToArray(),
        };
        var widget = Create(host);
        await Interactive(widget);
        await Bounded(widget.WhenLibraryIdleAsync(), "mixed installed and owned load");

        var snapshot = Snapshot(widget, 653);
        AssertOwnedTile(snapshot, installable, "Owned · Not installed · Install available");
        AssertOwnedTile(snapshot, signedOut, "Owned · Sign in to install");
        AssertOwnedTile(snapshot, unsupported, "Owned · Install unsupported");
        AssertOwnedTile(snapshot, stale, "Owned · Source degraded");
        var installedTile = Nodes(snapshot.Root).Single(node =>
            node.Id == PlayniteLibraryIdentity.FocusId(
                "grid", PlayniteLibraryIdentity.Key(installed.SavedId)));
        Assert.IsFalse(installedTile.IsDisabled == true);

        var ownedTile = Nodes(snapshot.Root).Single(node =>
            node.Id == PlayniteLibraryIdentity.FocusId(
                "grid", PlayniteLibraryIdentity.Key(installable.SavedId)));
        await widget.OnActionAsync(new("playnite-library.launch", ownedTile.Id));
        Assert.AreEqual(0, host.Launches.Count,
            "Owned-but-not-installed rows must never enter launch admission.");

        await widget.OnActionAsync(new("playnite-library.details.open", ownedTile.Id));
        var details = Snapshot(widget, 654);
        Assert.AreEqual("Primary action · Install from source",
            Nodes(details.Root).Single(node =>
                node.Id == "playnite-library.details.primary-action").Text);
        Assert.IsTrue(Nodes(details.Root).Single(node =>
            node.ActionId == "playnite-library.launch").IsDisabled == true);
        await Background(widget);

        static WidgetAppLibraryItem OwnedItem(
            int index,
            string statusCode,
            IReadOnlyList<WidgetAppLibraryAction> capabilities)
        {
            var item = Item(index);
            return item with
            {
                Presentation = item.Presentation with
                {
                    Availability = new(
                        WidgetAppLibraryAvailabilityState.Unavailable,
                        false,
                        statusCode),
                    Capabilities = new(capabilities),
                },
            };
        }

        static void AssertOwnedTile(
            ViewSnapshot snapshot,
            WidgetAppLibraryItem item,
            string expected)
        {
            var tile = Nodes(snapshot.Root).Single(node =>
                node.Id == PlayniteLibraryIdentity.FocusId(
                    "grid", PlayniteLibraryIdentity.Key(item.SavedId)));
            StringAssert.Contains(tile.AccessibilityLabel!, expected);
            Assert.IsTrue(tile.IsDisabled == true);
            Assert.IsNotNull(tile.CollectionItemKey,
                "Owned unavailable rows must remain in controller navigation.");
        }
    }

    [TestMethod, Timeout(30_000)]
    public async Task OfflineLastGoodLaunchRequiresFreshExactInstalledAuthority()
    {
        var failCatalog = false;
        var resolveMode = "installed";
        var items = new[] { Item(0), Item(1) };
        var host = new FakeHost(0)
        {
            QueryHandler = (_, token) =>
            {
                token.ThrowIfCancellationRequested();
                return failCatalog
                    ? ValueTask.FromException<WidgetAppLibraryPage>(
                        new WidgetCapabilityException(
                            "platform_unavailable", "source offline"))
                    : ValueTask.FromResult(new WidgetAppLibraryPage(
                        items, null, null, "offline-revision"));
            },
            ResolveHandler = request => resolveMode switch
            {
                "missing" => [],
                "stale" => request.SavedIds.Select(savedId =>
                {
                    var item = items.Single(candidate => candidate.SavedId == savedId);
                    return item with
                    {
                        Presentation = item.Presentation with
                        {
                            Availability = new(
                                WidgetAppLibraryAvailabilityState.StaleSource,
                                false,
                                "source_degraded"),
                            Capabilities = new([]),
                        },
                    };
                }).ToArray(),
                _ => request.SavedIds.Select(savedId =>
                    items.Single(candidate => candidate.SavedId == savedId)).ToArray(),
            },
            LaunchHandler = (_, token) =>
            {
                token.ThrowIfCancellationRequested();
                return ValueTask.FromResult(new WidgetAppLaunchObservation(
                    WidgetAppLaunchObservationState.LauncherStarted,
                    SupportsRunning: false,
                    SupportsEnded: false));
            },
        };
        var widget = Create(host);
        await Interactive(widget);
        await Bounded(widget.WhenLibraryIdleAsync(), "offline fixture initial load");
        var initial = Snapshot(widget, 655);
        var tile = Nodes(initial.Root).Single(node =>
            node.Id == PlayniteLibraryIdentity.FocusId(
                "grid", PlayniteLibraryIdentity.Key(items[0].SavedId)));
        await widget.OnActionAsync(new("playnite-library.launch", tile.Id));
        CollectionAssert.AreEqual(new[] { items[0].SavedId },
            widget.Organization.RecentSavedIds.ToArray());

        var withRecent = Snapshot(widget, 656);
        var recent = Nodes(withRecent.Root).Single(node =>
            node.ActionId?.StartsWith(
                PlayniteLibraryCollectionPolicy.ActionPrefix,
                StringComparison.Ordinal) == true &&
            Nodes(node).Any(child =>
                (child.Text ?? string.Empty).StartsWith(
                    "Continue (1)  ", StringComparison.Ordinal)));
        await widget.OnActionAsync(new(recent.ActionId!, recent.Id));
        await Bounded(widget.WhenLibraryIdleAsync(), "Continue collection load");
        var selected = Snapshot(widget, 657);
        var focus = selected.InitialFocusId;
        Assert.IsNotNull(focus);

        failCatalog = true;
        await widget.OnActionAsync(new("playnite-library.refresh", "playnite-library.refresh"));
        await Bounded(widget.WhenLibraryIdleAsync(), "offline retained refresh");
        var offline = Snapshot(widget, 658);
        Assert.AreEqual(focus, offline.InitialFocusId);
        Assert.IsTrue(Nodes(offline.Root).Any(node =>
            node.Id == "playnite-library.retained-error"));
        Assert.IsTrue(Nodes(offline.Root).Any(node =>
            node.ActionId == recent.ActionId && node.IsSelected == true));

        tile = Nodes(offline.Root).Single(node => node.Id == focus);
        await widget.OnActionAsync(new("playnite-library.launch", tile.Id));
        Assert.AreEqual(2, host.Launches.Count,
            "A current exact installed resolve should launch while browsing last-good rows.");
        CollectionAssert.AreEqual(new[] { items[0].SavedId },
            widget.Organization.RecentSavedIds.ToArray(),
            "Recovery launch duplicated or reordered Recent.");

        resolveMode = "stale";
        await widget.OnActionAsync(new("playnite-library.launch", tile.Id));
        Assert.AreEqual(2, host.Launches.Count,
            "Stale exact evidence entered launch admission.");
        resolveMode = "missing";
        await widget.OnActionAsync(new("playnite-library.launch", tile.Id));
        Assert.AreEqual(2, host.Launches.Count,
            "Missing exact evidence entered launch admission.");

        resolveMode = "installed";
        failCatalog = false;
        await widget.OnActionAsync(new("playnite-library.retry", "playnite-library.retry"));
        await Bounded(widget.WhenLibraryIdleAsync(), "offline recovery");
        var recovered = Snapshot(widget, 659);
        Assert.AreEqual(focus, recovered.InitialFocusId);
        Assert.IsFalse(Nodes(recovered.Root).Any(node =>
            node.Id == "playnite-library.retained-error"));
        CollectionAssert.AreEqual(new[] { items[0].SavedId },
            widget.Organization.RecentSavedIds.ToArray());
        await Background(widget);
    }

    [TestMethod, Timeout(30_000)]
    public async Task PermissionDeniedIsNotRenderedAsEmptyOrOffline()
    {
        var host = new FakeHost(0)
        {
            QueryHandler = (_, _) => ValueTask.FromException<WidgetAppLibraryPage>(
                new WidgetCapabilityException("permission_denied", "private")),
        };
        var widget = Create(host);
        await Interactive(widget);
        await Bounded(widget.WhenLibraryIdleAsync(), "permission-denied load");
        var denied = Snapshot(widget, 653);
        Assert.IsTrue(Nodes(denied.Root).Any(node =>
            (node.Text ?? string.Empty).Contains("permission denied",
                StringComparison.OrdinalIgnoreCase)));
        Assert.IsTrue(Nodes(denied.Root).Any(node =>
            node.ActionId == "playnite-library.retry"));
        Assert.IsFalse(Nodes(denied.Root).Any(node => node.Id == "playnite-library.empty"));
        await Background(widget);
    }

    [TestMethod, Timeout(30_000)]
    public async Task RecentMutationConflictPreservesOrganizationAndConcurrentOrder()
    {
        var a = new PlayniteLibraryDisplayItem("saved-a", "A", "Steam");
        var b = new PlayniteLibraryDisplayItem("saved-b", "B", "Steam");
        var c = new PlayniteLibraryDisplayItem("saved-c", "C", "Windows");
        var d = new PlayniteLibraryDisplayItem("saved-d", "D", "Windows");
        var e = new PlayniteLibraryDisplayItem("saved-e", "E", "Xbox");
        var baseline = new PlayniteLibraryPrivateState(
            PlayniteLibraryPrivateState.CurrentVersion, [a, b, c, d])
        {
            FavoriteSavedIds = [c.SavedId],
            VariantGroups =
            [
                new(PlayniteLibraryIdentity.GroupId(c.SavedId, d.SavedId),
                    [c.SavedId, d.SavedId], d.SavedId),
            ],
            RecentSavedIds = [a.SavedId, b.SavedId],
        };
        var latest = baseline with
        {
            Items = [e, a, b, c, d],
            RecentSavedIds = [e.SavedId, a.SavedId, b.SavedId],
        };
        PlayniteLibraryPrivateState? written = null;
        var attempts = 0;

        var result = await PlayniteLibraryStateStore.SaveAsync(
            state => PlayniteLibraryOrganizationPolicy.RecordRecent(state, b),
            (state, _, _) =>
            {
                if (attempts++ == 0)
                    return ValueTask.FromException<WidgetPrivateStateMutation>(
                        new WidgetCapabilityException("state_conflict", "fixture"));
                written = state;
                return ValueTask.FromResult(new WidgetPrivateStateMutation(3));
            },
            _ => ValueTask.FromResult(new WidgetPrivateStateValue<PlayniteLibraryPrivateState>(
                true, latest, 2)), baseline, 1, CancellationToken.None);

        Assert.IsTrue(result.Saved);
        CollectionAssert.AreEqual(new[] { b.SavedId, e.SavedId, a.SavedId },
            written!.RecentSavedIds.ToArray());
        CollectionAssert.AreEqual(new[] { c.SavedId },
            written.FavoriteSavedIds.ToArray());
        Assert.AreEqual(d.SavedId, written.VariantGroups.Single().PreferredSavedId);
    }

    [TestMethod, Timeout(30_000)]
    public async Task RecentHistorySurvivesRestartAndClearsWithoutOrganizationLoss()
    {
        var state = new WidgetTestPrivateState();
        var host = new FakeHost(2, state)
        {
            LaunchHandler = (_, _) => ValueTask.FromResult(new WidgetAppLaunchObservation(
                WidgetAppLaunchObservationState.LauncherStarted, false, false)),
        };
        var widget = Create(host);
        await Interactive(widget);
        await Ready(widget, host);
        var first = Nodes(Snapshot(widget, 63).Root)
            .First(node => node.ActionId == "playnite-library.launch");
        await widget.OnActionAsync(new("playnite-library.favorite", first.Id));
        await widget.OnActionAsync(new("playnite-library.launch", first.Id));
        await Background(widget);

        var restartedHost = new FakeHost(2, state);
        var restarted = Create(restartedHost);
        await Interactive(restarted);
        await Ready(restarted, restartedHost);
        await Bounded(restarted.WhenWarmStateIdleAsync(), "recent warm-state restart");
        CollectionAssert.AreEqual(new[] { "saved-00000" },
            restarted.Organization.RecentSavedIds.ToArray());
        CollectionAssert.AreEqual(new[] { "saved-00000" },
            restartedHost.Authority.FavoriteGameIds.ToArray());

        await restarted.OnActionAsync(new("playnite-library.recent.clear",
            "playnite-library.recent.clear"));
        Assert.AreEqual(0, restarted.Organization.RecentSavedIds.Count);
        CollectionAssert.AreEqual(new[] { "saved-00000" },
            restartedHost.Authority.FavoriteGameIds.ToArray());
        await Background(restarted);
    }

    [TestMethod, Timeout(30_000)]
    public async Task FailedStaleAndCanceledLaunchesNeverRecordRecentHistory()
    {
        var missing = new FakeHost(1) { ResolveHandler = _ => [] };
        var missingWidget = Create(missing);
        await Interactive(missingWidget);
        await Ready(missingWidget, missing);
        var missingTile = Nodes(Snapshot(missingWidget, 61).Root)
            .Single(node => node.ActionId == "playnite-library.launch");
        await missingWidget.OnActionAsync(new("playnite-library.launch", missingTile.Id));
        Assert.AreEqual(0, missingWidget.Organization.RecentSavedIds.Count);
        await Background(missingWidget);

        var admitted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource<WidgetAppLaunchObservation>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var stale = new FakeHost(1)
        {
            LaunchHandler = (_, _) =>
            {
                admitted.TrySetResult();
                return new ValueTask<WidgetAppLaunchObservation>(release.Task);
            },
        };
        var staleWidget = Create(stale);
        await Interactive(staleWidget);
        await Ready(staleWidget, stale);
        var staleTile = Nodes(Snapshot(staleWidget, 62).Root)
            .Single(node => node.ActionId == "playnite-library.launch");
        var launch = staleWidget.OnActionAsync(
            new("playnite-library.launch", staleTile.Id)).AsTask();
        await Bounded(admitted.Task, "stale recent admission");
        await staleWidget.OnActionAsync(new("playnite-library.refresh", "playnite-library.refresh"));
        await Bounded(staleWidget.WhenLibraryIdleAsync(), "stale recent replacement");
        release.TrySetResult(new(WidgetAppLaunchObservationState.Running, true, false));
        await Bounded(launch, "stale recent completion");
        Assert.AreEqual(0, staleWidget.Organization.RecentSavedIds.Count);
        await Background(staleWidget);
    }

    [TestMethod, Timeout(30_000)]
    public async Task NewerLaunchMakesCancellationIgnoringCompletionStale()
    {
        var firstWriteStarted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFirstWrite = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var secondStarted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var host = new FakeHost(2)
        {
            LaunchHandler = (request, token) =>
            {
                token.ThrowIfCancellationRequested();
                if (request.AppId.EndsWith("1", StringComparison.Ordinal))
                    secondStarted.TrySetResult();
                return ValueTask.FromResult(new WidgetAppLaunchObservation(
                    WidgetAppLaunchObservationState.LauncherStarted, false, false));
            },
        };
        var widget = Create(host);
        await Interactive(widget);
        await Ready(widget, host);
        var blockFirstRecent = true;
        host.StateWriteHandler = async (request, _) =>
        {
            var json = Encoding.UTF8.GetString(Convert.FromBase64String(
                request.CanonicalJsonBase64));
            var state = JsonSerializer.Deserialize<PlayniteLibraryPrivateState>(json)!;
            if (blockFirstRecent && state.RecentSavedIds.SequenceEqual(
                    ["saved-00000"], StringComparer.Ordinal))
            {
                blockFirstRecent = false;
                firstWriteStarted.TrySetResult();
                await releaseFirstWrite.Task.ConfigureAwait(false);
            }
            return await host.State.WriteAsync(request, CancellationToken.None)
                .ConfigureAwait(false);
        };
        var tiles = Nodes(Snapshot(widget, 64).Root)
            .Where(node => node.ActionId == "playnite-library.launch").ToArray();

        var first = widget.OnActionAsync(new("playnite-library.launch", tiles[0].Id)).AsTask();
        await Bounded(firstWriteStarted.Task, "accepted first launch Recent write");
        var second = widget.OnActionAsync(new("playnite-library.launch", tiles[1].Id)).AsTask();
        releaseFirstWrite.TrySetResult();
        await Bounded(secondStarted.Task, "replacement launch admission");
        await Bounded(Task.WhenAll(first, second), "replacement launch completion");

        CollectionAssert.AreEqual(new[] { "saved-00001" },
            widget.Organization.RecentSavedIds.ToArray());
        var durable = JsonSerializer.Deserialize<PlayniteLibraryPrivateState>(host.State.Json!)!;
        CollectionAssert.AreEqual(new[] { "saved-00001" },
            durable.RecentSavedIds.ToArray());
        var rendered = Nodes(Snapshot(widget, 65).Root)
            .Where(node => node.ActionId == "playnite-library.launch").ToArray();
        Assert.IsFalse(rendered[0].AccessibilityLabel!.Contains(
            "Running", StringComparison.Ordinal));
        StringAssert.Contains(rendered[1].AccessibilityLabel!, "Launcher started");
        await Background(widget);
    }

    [TestMethod]
    public void RejectedLaunchAdmissionCannotReserveGeneration()
    {
        var coordinator = new PlayniteLibraryLaunchPersistenceCoordinator();
        foreach (var admission in new[]
                 {
                     WidgetOperationAdmission.RejectedInactive,
                     WidgetOperationAdmission.RejectedCapacity,
                 })
        {
            var ready = new TaskCompletionSource<long>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var handle = new WidgetOperationHandle(admission,
                Task.FromResult(new WidgetOperationResult(
                    WidgetOperationStatus.Rejected)));

            Assert.IsFalse(coordinator.CompleteAdmission(handle, ready));
            Assert.AreEqual(0L, coordinator.CurrentGeneration,
                $"{admission} invalidated the active launch generation.");
            Assert.IsTrue(ready.Task.IsCanceled);
        }
    }

    [TestMethod]
    public async Task LaunchPersistenceCoordinatorRestoresCurrentRecentAfterReplacement()
    {
        var coordinator = new PlayniteLibraryLaunchPersistenceCoordinator();
        var ready = new TaskCompletionSource<long>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var handle = new WidgetOperationHandle(
            WidgetOperationAdmission.Started,
            Task.FromResult(new WidgetOperationResult(WidgetOperationStatus.Succeeded)));
        Assert.IsTrue(coordinator.CompleteAdmission(handle, ready));
        var generation = await ready.Task;
        var first = new PlayniteLibraryDisplayItem("saved-first", "First", "Steam");
        var current = new PlayniteLibraryDisplayItem("saved-current", "Current", "Xbox");
        var durable = PlayniteLibraryPrivateState.Empty with
        {
            Items = [first, current],
            RecentSavedIds = [current.SavedId],
        };
        var writes = 0;

        async Task<bool> Save(
            Func<PlayniteLibraryPrivateState, PlayniteLibraryStateMutation> apply,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var mutation = apply(durable);
            Assert.IsTrue(mutation.Accepted);
            durable = mutation.State;
            if (++writes == 1) coordinator.Invalidate();
            await Task.CompletedTask;
            return true;
        }

        await coordinator.CommitRecentAsync(
            generation,
            WidgetAppLaunchObservationState.LauncherStarted,
            first,
            Save,
            () => [current.SavedId],
            CancellationToken.None);

        Assert.AreEqual(2, writes);
        CollectionAssert.AreEqual(new[] { current.SavedId },
            durable.RecentSavedIds.ToArray());
    }

    [TestMethod, Timeout(30_000)]
    public async Task FailedLaunchRestoresExactFocusAndRetriesSameGame()
    {
        var attempts = 0;
        var host = new FakeHost(1)
        {
            LaunchHandler = (_, _) => ++attempts == 1
                ? ValueTask.FromException<WidgetAppLaunchObservation>(
                    new WidgetCapabilityException("launch_failed", "fixture"))
                : ValueTask.FromResult(new WidgetAppLaunchObservation(
                    WidgetAppLaunchObservationState.LauncherStarted, false, false)),
        };
        var widget = Create(host);
        await Interactive(widget);
        await Ready(widget, host);
        var tile = Nodes(Snapshot(widget, 66).Root)
            .Single(node => node.ActionId == "playnite-library.launch");

        await widget.OnActionAsync(new("playnite-library.launch", tile.Id));
        var failed = Snapshot(widget, 67);
        Assert.AreEqual(tile.Id, failed.InitialFocusId);
        StringAssert.Contains(Nodes(failed.Root).Single(node => node.Id == tile.Id)
            .AccessibilityLabel!, "Failed");
        await widget.OnActionAsync(new("playnite-library.launch", tile.Id));
        var retried = Snapshot(widget, 68);
        Assert.AreEqual(tile.Id, retried.InitialFocusId);
        StringAssert.Contains(Nodes(retried.Root).Single(node => node.Id == tile.Id)
            .AccessibilityLabel!, "Launcher started");
        CollectionAssert.AreEqual(new[] { "saved-00000" },
            widget.Organization.RecentSavedIds.ToArray());
        await Background(widget);
    }

    [TestMethod]
    public void MissingAndReplacementIdentitiesDoNotInheritRecentHistory()
    {
        var old = new PlayniteLibraryDisplayItem("saved-old", "Game", "Steam");
        var state = new PlayniteLibraryPrivateState(
            PlayniteLibraryPrivateState.CurrentVersion, [old])
        {
            RecentSavedIds = [old.SavedId],
        };
        var replacement = PlayniteLibraryItem.From(WithPresentation(
            Item(0) with { SavedId = "saved-new" }, displayName: old.DisplayName));

        var projected = PlayniteLibraryOrganizationPolicy.ProjectPage(state, [replacement]);

        CollectionAssert.AreEqual(new[] { old.SavedId },
            projected.RecentSavedIds.ToArray());
        Assert.IsTrue(projected.Items.Any(item => item.SavedId == old.SavedId));
        Assert.IsTrue(projected.Items.Any(item => item.SavedId == "saved-new"));
    }

    [TestMethod, Timeout(30_000)]
    public async Task AdapterEvidenceProjectsExactLifecyclePerSavedIdentity()
    {
        var host = new FakeHost(3)
        {
            LaunchHandler = (request, token) =>
            {
                token.ThrowIfCancellationRequested();
                var state = request.AppId.EndsWith("0", StringComparison.Ordinal)
                    ? WidgetAppLaunchObservationState.Running
                    : request.AppId.EndsWith("1", StringComparison.Ordinal)
                        ? WidgetAppLaunchObservationState.Ended
                        : WidgetAppLaunchObservationState.LauncherStarted;
                return ValueTask.FromResult(new WidgetAppLaunchObservation(
                    state, SupportsRunning: true, SupportsEnded: true));
            },
        };
        var widget = Create(host);
        await Interactive(widget);
        await Ready(widget, host);
        var first = Nodes(Snapshot(widget, 20).Root)
            .Where(node => node.ActionId == "playnite-library.launch").ToArray();

        await widget.OnActionAsync(new("playnite-library.launch", first[0].Id));
        await widget.OnActionAsync(new("playnite-library.launch", first[1].Id));
        await widget.OnActionAsync(new("playnite-library.launch", first[2].Id));

        var tiles = Nodes(Snapshot(widget, 21).Root)
            .Where(node => node.ActionId == "playnite-library.launch").ToArray();
        StringAssert.Contains(tiles[0].AccessibilityLabel!, "Running");
        StringAssert.Contains(tiles[1].AccessibilityLabel!, "Ended");
        StringAssert.Contains(tiles[2].AccessibilityLabel!, "Launcher started");
        await Background(widget);
    }

    [TestMethod, Timeout(30_000)]
    public async Task UnsupportedAdapterUsesRequestAcceptedAndDoesNotClaimRunning()
    {
        var host = new FakeHost(1);
        var widget = Create(host);
        await Interactive(widget);
        await Ready(widget, host);
        var tile = Nodes(Snapshot(widget, 22).Root)
            .Single(node => node.ActionId == "playnite-library.launch");

        await widget.OnActionAsync(new("playnite-library.launch", tile.Id));

        var rendered = Nodes(Snapshot(widget, 23).Root)
            .Single(node => node.ActionId == "playnite-library.launch");
        StringAssert.Contains(rendered.AccessibilityLabel!, "Request accepted");
        Assert.IsFalse(rendered.AccessibilityLabel!.Contains("Running", StringComparison.Ordinal));
        Assert.AreEqual(0, widget.Organization.RecentSavedIds.Count);
        await Background(widget);
    }

    [TestMethod, Timeout(30_000)]
    public async Task LateCanceledEvidenceCannotPublishAfterDeactivation()
    {
        var admitted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource<WidgetAppLaunchObservation>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var host = new FakeHost(1)
        {
            LaunchHandler = (_, _) =>
            {
                admitted.TrySetResult();
                return new ValueTask<WidgetAppLaunchObservation>(release.Task);
            },
        };
        var widget = Create(host);
        await Interactive(widget);
        await Ready(widget, host);
        var tile = Nodes(Snapshot(widget, 24).Root)
            .Single(node => node.ActionId == "playnite-library.launch");
        var launch = widget.OnActionAsync(new("playnite-library.launch", tile.Id)).AsTask();
        await Bounded(admitted.Task, "launch evidence admission");
        StringAssert.Contains(
            Nodes(Snapshot(widget, 25).Root)
                .Single(node => node.ActionId == "playnite-library.launch")
                .AccessibilityLabel!,
            "Pending");

        var background = Background(widget);
        Assert.IsFalse(background.IsCompleted);
        release.TrySetResult(new(WidgetAppLaunchObservationState.Running, true, false));
        await Bounded(background, "launch lifecycle drain");
        await Bounded(launch, "late launch completion");

        Assert.IsFalse(Nodes(Snapshot(widget, 26).Root).Any(node =>
            node.AccessibilityLabel?.Contains("Running", StringComparison.Ordinal) == true));
    }

    [TestMethod, Timeout(30_000)]
    public async Task RefreshGenerationRejectsLateLaunchEvidence()
    {
        var admitted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource<WidgetAppLaunchObservation>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var host = new FakeHost(1)
        {
            LaunchHandler = (_, _) =>
            {
                admitted.TrySetResult();
                return new ValueTask<WidgetAppLaunchObservation>(release.Task);
            },
        };
        var widget = Create(host);
        await Interactive(widget);
        await Ready(widget, host);
        var tile = Nodes(Snapshot(widget, 28).Root)
            .Single(node => node.ActionId == "playnite-library.launch");
        var launch = widget.OnActionAsync(new("playnite-library.launch", tile.Id)).AsTask();
        await Bounded(admitted.Task, "stale launch admission");

        var revision = widget.Collection.Revision;
        await widget.OnActionAsync(new("playnite-library.refresh", "playnite-library.refresh"));
        await Bounded(widget.WhenLibraryIdleAsync(), "collection refresh");
        Assert.IsGreaterThan(revision, widget.Collection.Revision);
        release.TrySetResult(new(WidgetAppLaunchObservationState.Running, true, false));
        await Bounded(launch, "stale launch completion");

        Assert.IsFalse(Nodes(Snapshot(widget, 29).Root).Any(node =>
            node.AccessibilityLabel?.Contains("Running", StringComparison.Ordinal) == true));
        await Background(widget);
    }

    [TestMethod, Timeout(30_000)]
    public async Task MissingCurrentIdentityCannotLaunch()
    {
        var host = new FakeHost(1) { ResolveHandler = _ => [] };
        var widget = Create(host);
        await Interactive(widget);
        await Ready(widget, host);
        var tile = Nodes(widget.RenderSnapshot("launcher.test", 3).Root)
            .Single(node => node.ActionId == "playnite-library.launch");

        await widget.OnActionAsync(new("playnite-library.launch", tile.Id));

        Assert.AreEqual(0, host.Launches.Count);
        Assert.AreEqual("Failed · The selected game is no longer installed",
            Nodes(widget.RenderSnapshot("launcher.test", 4).Root)
                .Single(node => node.Id == "playnite-library.status").Text);
        StringAssert.Contains(
            Nodes(widget.RenderSnapshot("launcher.test", 5).Root)
                .Single(node => node.ActionId == "playnite-library.launch")
                .AccessibilityLabel!, "Failed");
        await Background(widget);
    }

    [TestMethod, Timeout(30_000)]
    public async Task LaunchStateRetentionIsBounded()
    {
        var host = new FakeHost(LauncherWidget.MaximumRetainedLaunchStates + 4);
        var widget = Create(host);
        await Interactive(widget);
        await Ready(widget, host);
        var tiles = Nodes(Snapshot(widget, 27).Root)
            .Where(node => node.ActionId == "playnite-library.launch").ToArray();

        foreach (var tile in tiles)
            await widget.OnActionAsync(new("playnite-library.launch", tile.Id));

        Assert.AreEqual(LauncherWidget.MaximumRetainedLaunchStates,
            widget.RetainedLaunchStateCount);
        await Background(widget);
    }

    [TestMethod, Timeout(30_000)]
    public async Task SameTitleVariantsKeepDistinctIdentityAndExactRouting()
    {
        var host = new FakeHost(2)
        {
            ItemFactory = index => WithPresentation(Item(index),
                displayName: "Shared title", source: index == 0 ? "Steam" : "Windows"),
        };
        var widget = Create(host);
        await Interactive(widget);
        await Ready(widget, host);
        var tiles = Nodes(Snapshot(widget, 5).Root)
            .Where(node => node.ActionId == "playnite-library.launch").ToArray();

        Assert.AreEqual(2, tiles.Length);
        Assert.AreEqual(0, widget.Organization.VariantGroups.Count);
        Assert.AreNotEqual(tiles[0].Id, tiles[1].Id);
        StringAssert.Contains(tiles[0].AccessibilityLabel ?? string.Empty, "Steam");
        StringAssert.Contains(tiles[1].AccessibilityLabel ?? string.Empty, "Windows");
        await widget.OnActionAsync(new("playnite-library.launch", tiles[1].Id));
        CollectionAssert.AreEqual(new[] { "saved-00001" },
            host.ResolveRequests[^1].ToArray());
        CollectionAssert.AreEqual(new[] { "app-00001" }, host.Launches.ToArray());
        await Background(widget);
    }

    [TestMethod, Timeout(30_000)]
    public async Task M1CollectionsAreVisibleExactAndDetailsReturnToTheirOrigin()
    {
        var displays = Enumerable.Range(0, 6)
            .Select(index => new PlayniteLibraryDisplayItem(
                $"saved-{index:D5}", $"Game {index:D5}",
                index % 2 == 0 ? "Steam" : "Windows"))
            .ToArray();
        var persisted = new PlayniteLibraryPrivateState(
            PlayniteLibraryPrivateState.CurrentVersion, displays)
        {
            FavoriteSavedIds = [displays[1].SavedId],
            RecentSavedIds = [displays[2].SavedId],
            ManualSavedIds = [displays[3].SavedId],
        };
        var host = new FakeHost(6, new WidgetTestPrivateState(
            JsonSerializer.Serialize(persisted), 1))
        {
            QueryHandler = (request, token) =>
            {
                token.ThrowIfCancellationRequested();
                var offset = request.Cursor is null ? 0 : int.Parse(
                    request.Cursor.AsSpan(request.Cursor.LastIndexOf('.') + 1),
                    CultureInfo.InvariantCulture);
                var count = Math.Min(request.Limit, Math.Max(0, 6 - offset));
                var items = Enumerable.Range(offset, count).Select(index => index == 3
                    ? InstalledItem(
                        "app-00003", "saved-00003", "Manual App",
                        WidgetAppLibraryKind.Application, "source-windows", "Windows")
                    : Item(index)).ToArray();
                return ValueTask.FromResult(new WidgetAppLibraryPage(
                    items,
                    offset == 0 ? null : $"cursor.{Math.Max(0, offset - request.Limit)}",
                    offset + count < 6 ? $"cursor.{offset + count}" : null,
                    "revision-m1")
                {
                    Sources =
                    [
                        new("source-steam", "Steam", WidgetAppLibrarySourceHealth.Healthy,
                            1, "healthy"),
                        new("source-windows", "Windows", WidgetAppLibrarySourceHealth.Healthy,
                            1, "healthy"),
                        new("source-empty", "Empty Store", WidgetAppLibrarySourceHealth.Healthy,
                            1, "healthy"),
                    ],
                });
            },
            ItemFactory = index => index == 3
                ? InstalledItem(
                    "app-00003", "saved-00003", "Manual App",
                    WidgetAppLibraryKind.Application, "source-windows", "Windows")
                : Item(index),
        };
        var widget = Create(host);
        await Interactive(widget);
        await Ready(widget, host);
        await Bounded(widget.WhenWarmStateIdleAsync(), "M1 collection warm state");
        await Bounded(widget.WhenLibraryIdleAsync(), "M1 collection projection");

        var library = Snapshot(widget, 35);
        var collections = Nodes(library.Root).Single(node =>
            node.Id == "playnite-library.collections");
        foreach (var label in new[]
                 {
                     "All installed", "Continue (1)", "Favorites (1)", "Manual (1)",
                     "Steam", "Windows",
                 })
            Assert.IsTrue(Nodes(collections).Any(node => MatchesToggle(node, label)),
                $"Missing M1 collection {label}.");
        Assert.IsFalse(Nodes(collections).Any(node => MatchesToggle(node, "Empty Store")),
            "A healthy advertised source without a matching game became an empty collection.");

        await SelectCollection("Continue (1)");
        CollectionAssert.AreEqual(new[] { "saved-00002" },
            host.Queries[^1].Query.FavoriteSavedIds.ToArray());
        await SelectCollection("Favorites (1)");
        CollectionAssert.AreEqual(new[] { "saved-00001" },
            host.Queries[^1].Query.FavoriteSavedIds.ToArray());
        await SelectCollection("Manual (1)");
        CollectionAssert.AreEqual(new[] { "saved-00003" },
            host.Queries[^1].Query.FavoriteSavedIds.ToArray());
        await SelectCollection("Windows");
        Assert.AreEqual("Windows", host.Queries[^1].Query.SourceAttribution);
        Assert.AreEqual(0, host.Queries[^1].Query.FavoriteSavedIds.Count);

        var sourceView = Snapshot(widget, 36);
        var origin = Nodes(sourceView.Root).First(node =>
            node.ActionId == "playnite-library.launch" &&
            (node.AccessibilityLabel ?? string.Empty).Contains(
                "Windows", StringComparison.Ordinal));
        var revision = widget.Collection.Revision;
        var anchor = widget.Collection.Anchor;
        await widget.OnActionAsync(new("playnite-library.details.open", origin.Id));
        var details = Snapshot(widget, 37);
        Assert.IsFalse(Nodes(details.Root).Any(node =>
            node.ActionId?.StartsWith(PlayniteLibraryCollectionPolicy.ActionPrefix,
                StringComparison.Ordinal) == true));
        Assert.IsFalse(Nodes(details.Root).Any(node => node.Shortcuts.Any(shortcut =>
            shortcut.Button is ControllerButton.LeftBumper or ControllerButton.RightBumper)));

        await widget.OnActionAsync(new(PlayniteLibraryActionSheet.OpenAction,
            PlayniteLibraryDetailsPolicy.ActionSourceId));
        var sheet = Snapshot(widget, 38);
        Assert.AreEqual(PlayniteLibraryActionSheet.ScopeId, sheet.ActiveInputScopeId);
        Assert.IsFalse(Nodes(sheet.Root).Any(node => node.Shortcuts.Any(shortcut =>
            shortcut.Button is ControllerButton.LeftBumper or ControllerButton.RightBumper)));
        await widget.OnActionAsync(new(PlayniteLibraryActionSheet.CloseAction,
            PlayniteLibraryActionSheet.InitialFocusId));
        var detailView = Snapshot(widget, 39);
        var detailBack = detailView.Root.Shortcuts.Single(shortcut =>
            shortcut.Button == ControllerButton.B);
        await widget.OnActionAsync(new(detailBack.ActionId,
            PlayniteLibraryDetailsPolicy.ActionSourceId, ControllerButton.B,
            ControllerEventPhase.Pressed, InputScopeId: detailView.ActiveInputScopeId));
        var returned = Snapshot(widget, 40);
        Assert.AreEqual(origin.Id, returned.InitialFocusId);
        Assert.AreEqual(revision, widget.Collection.Revision);
        Assert.AreEqual(anchor, widget.Collection.Anchor);
        Assert.IsTrue(Nodes(returned.Root).Any(node =>
            node.ActionId?.StartsWith(PlayniteLibraryCollectionPolicy.ActionPrefix,
                StringComparison.Ordinal) == true && node.IsSelected == true &&
            Nodes(node).Any(child => MatchesToggle(child, "Windows"))));

        await widget.OnActionAsync(new(PlayniteLibraryActionSheet.OpenAction, origin.Id));
        await widget.OnActionAsync(new("playnite-library.hide", "playnite-library.actions.hide"));
        await Bounded(widget.WhenLibraryIdleAsync(), "M1 hidden library reload");
        var afterHide = Snapshot(widget, 41);
        Assert.IsTrue(afterHide.InitialFocusId is null || Nodes(afterHide.Root).Any(node =>
            node.Id == afterHide.InitialFocusId),
            "Hide must not retain a removed tile as the Library focus target.");
        await Background(widget);

        async Task SelectCollection(string label)
        {
            var snapshot = Snapshot(widget, 100 + host.Queries.Count);
            var option = Nodes(snapshot.Root).Single(node =>
                node.ActionId?.StartsWith(PlayniteLibraryCollectionPolicy.ActionPrefix,
                    StringComparison.Ordinal) == true &&
                Nodes(node).Any(child => MatchesToggle(child, label)));
            await widget.OnActionAsync(new(option.ActionId!, option.Id));
            await Bounded(widget.WhenLibraryIdleAsync(), label + " collection load");
        }

        static bool MatchesToggle(ViewNode node, string label) =>
            string.Equals(node.Text, label + "  On", StringComparison.Ordinal) ||
            string.Equals(node.Text, label + "  Off", StringComparison.Ordinal) ||
            string.Equals(node.AccessibilityLabel, label + ", On", StringComparison.Ordinal) ||
            string.Equals(node.AccessibilityLabel, label + ", Off", StringComparison.Ordinal);
    }

    [TestMethod, Timeout(30_000)]
    public async Task CollectionSelectionIsExclusiveAndSourceCatalogSurvivesPagingAndRefresh()
    {
        var removedSteam = false;
        var persisted = new PlayniteLibraryPrivateState(
            PlayniteLibraryPrivateState.CurrentVersion,
            [new("saved-00000", "Steam game", "Steam"),
             new("saved-00064", "Windows game", "Windows")])
        {
            FavoriteSavedIds = ["saved-00000"],
            RecentSavedIds = ["saved-00064"],
            ManualSavedIds = ["saved-00000"],
        };
        var host = new FakeHost(0, new WidgetTestPrivateState(
            JsonSerializer.Serialize(persisted), 1))
        {
            QueryHandler = (request, token) =>
            {
                token.ThrowIfCancellationRequested();
                var source = request.Query.SourceAttribution;
                var offset = request.Cursor is null ? 0 : 64;
                WidgetAppLibraryItem[] items = source switch
                {
                    "Steam" when !removedSteam => [WithPresentation(Item(0), source: "Steam")],
                    "Windows" => [WithPresentation(Item(64), source: "Windows")],
                    _ when offset == 0 => removedSteam
                        ? [WithPresentation(Item(64), source: "Windows")]
                        : [WithPresentation(Item(0), source: "Steam")],
                    _ => [WithPresentation(Item(64), source: "Windows")],
                };
                var sources = removedSteam
                    ? new WidgetAppLibrarySource[]
                    {
                        new("source-windows", "Windows", WidgetAppLibrarySourceHealth.Healthy,
                            2, "healthy"),
                        new("source-empty", "Empty Store", WidgetAppLibrarySourceHealth.Healthy,
                            2, "healthy"),
                    }
                    :
                    [
                        new("source-steam", "Steam", WidgetAppLibrarySourceHealth.Healthy,
                            1, "healthy"),
                        new("source-windows", "Windows", WidgetAppLibrarySourceHealth.Healthy,
                            1, "healthy"),
                        new("source-empty", "Empty Store", WidgetAppLibrarySourceHealth.Healthy,
                            1, "healthy"),
                    ];
                var after = source is null && !removedSteam && offset == 0
                    ? "cursor.64"
                    : null;
                return ValueTask.FromResult(new WidgetAppLibraryPage(
                    items, offset == 0 ? null : "cursor.0", after, "sources")
                { Sources = sources });
            },
        };
        var widget = Create(host);
        await Interactive(widget);
        await Ready(widget, host);
        await Bounded(widget.WhenWarmStateIdleAsync(), "collection warm state");

        await Select("Continue (1)", PlayniteLibraryCollectionKind.Recent, null,
            ["saved-00064"]);
        await Select("Favorites (1)", PlayniteLibraryCollectionKind.Favorites, null,
            ["saved-00000"]);
        await Select("Continue (1)", PlayniteLibraryCollectionKind.Recent, null,
            ["saved-00064"]);
        await Select("All installed", PlayniteLibraryCollectionKind.AllInstalled, null, []);

        var next = Nodes(Snapshot(widget, 510).Root).Single(node =>
            node.ActionId == "playnite-library.next");
        await widget.OnActionAsync(new(next.ActionId!, next.Id));
        await Bounded(widget.WhenLibraryIdleAsync(), "second source page");
        AssertOptions("All installed", "Steam", "Windows");

        await Background(widget);
        widget = Create(host);
        await Interactive(widget);
        await Ready(widget, host);
        await Bounded(widget.WhenWarmStateIdleAsync(), "persisted source catalog");
        AssertOptions("All installed", "Steam", "Windows");

        await Select("Steam", PlayniteLibraryCollectionKind.Source, "Steam", []);
        await Select("Windows", PlayniteLibraryCollectionKind.Source, "Windows", []);
        await widget.OnActionAsync(new WidgetActionEvent(
            "playnite-library.search.commit", "playnite-library.search")
            { CommittedText = "Conformance" });
        await Bounded(widget.WhenLibraryIdleAsync(), "source search");
        await widget.OnActionAsync(new(
            "playnite-library.filter.sort", "playnite-library.filter.sort"));
        await Bounded(widget.WhenLibraryIdleAsync(), "source sort");
        await widget.OnActionAsync(new(
            "playnite-library.query.clear", "playnite-library.query.clear"));
        await Bounded(widget.WhenLibraryIdleAsync(), "source clear");
        var cleared = host.Queries[^1].Query;
        Assert.IsNull(cleared.SearchText);
        Assert.AreEqual(WidgetAppLibrarySortOrder.DisplayName, cleared.Sort);
        Assert.AreEqual("Windows", cleared.SourceAttribution);
        Assert.IsTrue(Nodes(Snapshot(widget, 510).Root).Any(node =>
            node.IsSelected == true && Nodes(node).Any(child =>
                MatchesToggle(child, "Windows"))));
        await Select("Steam", PlayniteLibraryCollectionKind.Source, "Steam", []);

        removedSteam = true;
        await Select("All installed", PlayniteLibraryCollectionKind.AllInstalled, null, []);
        var refreshed = Snapshot(widget, 511);
        Assert.IsFalse(Nodes(refreshed.Root).Any(node => MatchesToggle(node, "Steam")));
        Assert.IsTrue(Nodes(refreshed.Root).Any(node => MatchesToggle(node, "Windows")));
        Assert.IsFalse(Nodes(refreshed.Root).Any(node => MatchesToggle(node, "Empty Store")));

        await Background(widget);
        var restarted = Create(host);
        await Interactive(restarted);
        await Ready(restarted, host);
        var restart = Snapshot(restarted, 512);
        Assert.AreEqual(1, Nodes(restart.Root).Count(node => node.IsSelected == true &&
            node.ActionId?.StartsWith(PlayniteLibraryCollectionPolicy.ActionPrefix,
                StringComparison.Ordinal) == true));
        Assert.IsTrue(Nodes(restart.Root).Any(node => MatchesToggle(node, "Windows")));
        Assert.IsFalse(Nodes(restart.Root).Any(node => MatchesToggle(node, "Steam")));
        await Background(restarted);

        async Task Select(
            string label,
            PlayniteLibraryCollectionKind kind,
            string? source,
            IReadOnlyList<string> savedIds)
        {
            var snapshot = Snapshot(widget, 520 + host.Queries.Count);
            var option = Nodes(snapshot.Root).Single(node =>
                node.ActionId?.StartsWith(PlayniteLibraryCollectionPolicy.ActionPrefix,
                    StringComparison.Ordinal) == true &&
                Nodes(node).Any(child => MatchesToggle(child, label)));
            await widget.OnActionAsync(new(option.ActionId!, option.Id));
            await Bounded(widget.WhenLibraryIdleAsync(), label + " load");
            var selected = Nodes(Snapshot(widget, 530 + host.Queries.Count).Root)
                .Where(node => node.IsSelected == true &&
                    node.ActionId?.StartsWith(PlayniteLibraryCollectionPolicy.ActionPrefix,
                        StringComparison.Ordinal) == true).ToArray();
            Assert.AreEqual(1, selected.Length, "Exactly one collection must be selected.");
            Assert.IsTrue(Nodes(selected[0]).Any(child => MatchesToggle(child, label)));
            Assert.AreEqual(source, host.Queries[^1].Query.SourceAttribution);
            CollectionAssert.AreEqual(savedIds.ToArray(),
                host.Queries[^1].Query.FavoriteSavedIds.ToArray());
            Assert.AreEqual(kind == PlayniteLibraryCollectionKind.Source, source is not null);
        }

        void AssertOptions(params string[] labels)
        {
            var snapshot = Snapshot(widget, 540 + host.Queries.Count);
            foreach (var label in labels)
                Assert.IsTrue(Nodes(snapshot.Root).Any(node => MatchesToggle(node, label)),
                    $"Missing stable collection option {label}.");
        }

        static bool MatchesToggle(ViewNode node, string label) =>
            string.Equals(node.Text, label + "  On", StringComparison.Ordinal) ||
            string.Equals(node.Text, label + "  Off", StringComparison.Ordinal) ||
            string.Equals(node.AccessibilityLabel, label + ", On", StringComparison.Ordinal) ||
            string.Equals(node.AccessibilityLabel, label + ", Off", StringComparison.Ordinal);
    }

    [TestMethod, Timeout(30_000)]
    public async Task SearchCancelClearAndNestedRoutesPreserveCollectionScope()
    {
        var persisted = new PlayniteLibraryPrivateState(
            PlayniteLibraryPrivateState.CurrentVersion,
            [new("saved-00001", "Game 00001", "Windows")])
        {
            FavoriteSavedIds = ["saved-00001"],
        };
        var host = new FakeHost(3, new WidgetTestPrivateState(
            JsonSerializer.Serialize(persisted), 1));
        var widget = Create(host);
        await Interactive(widget);
        await Ready(widget, host);
        await Bounded(widget.WhenWarmStateIdleAsync(), "search collection warm state");
        var compactSurface = Snapshot(widget, 400);
        Assert.AreEqual(420, compactSurface.Surface!.MinimumWidth);
        Assert.AreEqual(340, compactSurface.Surface.MinimumHeight);
        var compactSearch = Nodes(compactSurface.Root).Single(node =>
            node.Id == "playnite-library.search");
        Assert.AreEqual(ViewNodeKind.TextEntry, compactSearch.Kind);
        Assert.AreEqual("playnite-library.search.commit", compactSearch.ActionId);
        Assert.AreNotEqual(ResponsiveVisibility.ExpandedOnly, compactSearch.VisibleWhen);

        await widget.OnActionAsync(new(
            "playnite-library.filter.favorites", "playnite-library.filter.favorites"));
        await Bounded(widget.WhenLibraryIdleAsync(), "favorite collection load");
        await widget.OnActionAsync(new WidgetActionEvent(
            "playnite-library.search.commit", "playnite-library.search")
            { CommittedText = "Game 00001" });
        await Bounded(widget.WhenLibraryIdleAsync(), "committed favorite search");
        var searched = Snapshot(widget, 401);
        var search = Nodes(searched.Root).Single(node =>
            node.Id == "playnite-library.search");
        Assert.AreEqual("Game 00001", search.TextEntryValue);
        CollectionAssert.AreEqual(new[] { "saved-00001" },
            host.Queries[^1].Query.FavoriteSavedIds.ToArray());

        // The host emits no committed text when its modal is canceled. Even if
        // a defensive null event reaches the widget, the committed query remains.
        await widget.OnActionAsync(new WidgetActionEvent(
            "playnite-library.search.commit", "playnite-library.search"));
        var canceled = Snapshot(widget, 402);
        var returnedSearch = Nodes(canceled.Root).Single(node =>
            node.Id == "playnite-library.search");
        Assert.AreEqual("Game 00001", returnedSearch.TextEntryValue);
        Assert.AreEqual(compactSearch.Id, returnedSearch.Id);
        Assert.AreEqual(compactSearch.ActionId, returnedSearch.ActionId);

        var origin = Nodes(canceled.Root).Single(node =>
            node.ActionId == "playnite-library.launch" &&
            (node.AccessibilityLabel ?? string.Empty).Contains(
                "Game 00001", StringComparison.Ordinal));
        await widget.OnActionAsync(new("playnite-library.details.open", origin.Id));
        var details = Snapshot(widget, 403);
        Assert.IsFalse(Nodes(details.Root).Any(node => node.Shortcuts.Any(shortcut =>
            shortcut.Button is ControllerButton.LeftBumper or ControllerButton.RightBumper)));
        await widget.OnActionAsync(new(
            PlayniteLibraryActionSheet.OpenAction,
            PlayniteLibraryDetailsPolicy.ActionSourceId));
        var sheet = Snapshot(widget, 404);
        Assert.IsFalse(Nodes(sheet.Root).Any(node => node.Shortcuts.Any(shortcut =>
            shortcut.Button is ControllerButton.LeftBumper or ControllerButton.RightBumper)));
        await widget.OnActionAsync(new(
            PlayniteLibraryActionSheet.CloseAction,
            PlayniteLibraryActionSheet.InitialFocusId));
        var back = details.Root.Shortcuts.Single(shortcut =>
            shortcut.Button == ControllerButton.B);
        await widget.OnActionAsync(new(
            back.ActionId,
            PlayniteLibraryDetailsPolicy.ActionSourceId,
            ControllerButton.B,
            ControllerEventPhase.Pressed,
            InputScopeId: details.ActiveInputScopeId));
        var returned = Snapshot(widget, 405);
        Assert.AreEqual(origin.Id, returned.InitialFocusId);
        Assert.AreEqual("Game 00001", Nodes(returned.Root).Single(node =>
            node.Id == "playnite-library.search").TextEntryValue);

        await widget.OnActionAsync(new(
            "playnite-library.query.clear", "playnite-library.query.clear"));
        await Bounded(widget.WhenLibraryIdleAsync(), "clear search within favorites");
        var cleared = Snapshot(widget, 406);
        Assert.AreEqual(string.Empty, Nodes(cleared.Root).Single(node =>
            node.Id == "playnite-library.search").TextEntryValue);
        CollectionAssert.AreEqual(new[] { "saved-00001" },
            host.Queries[^1].Query.FavoriteSavedIds.ToArray());
        Assert.IsTrue(Nodes(cleared.Root).Any(node =>
            node.ActionId?.StartsWith(PlayniteLibraryCollectionPolicy.ActionPrefix,
                StringComparison.Ordinal) == true && node.IsSelected == true &&
            Nodes(node).Any(child =>
                (child.Text ?? string.Empty).StartsWith(
                    "Favorites (1)  ", StringComparison.Ordinal))));
        await Background(widget);
    }

    [TestMethod, Timeout(30_000)]
    public async Task DetailsExposeOnlyKnownNormalizedMetadataAndOperationState()
    {
        var display = new PlayniteLibraryDisplayItem("saved-00000", "Metadata Game", "Steam");
        var state = new PlayniteLibraryPrivateState(
            PlayniteLibraryPrivateState.CurrentVersion, [display])
        {
            FavoriteSavedIds = [display.SavedId],
            Categories = [new("category.0123456789abcdef0123456789abcdef",
                "Couch", [display.SavedId])],
        };
        var host = new FakeHost(1, new WidgetTestPrivateState(
            JsonSerializer.Serialize(state), 1))
        {
            ItemFactory = _ => Item(0) with
            {
                Presentation = Item(0).Presentation with
                {
                    DisplayName = "Metadata Game",
                    Metadata = new(
                        "metadata-revision",
                        new("fixture", "record-revision", "Fixture metadata", 1_700_000_000_000))
                    {
                        Version = "1.2.3",
                        LastPlayedAtUnixMilliseconds = 1_700_000_000_000,
                        PlaytimeMinutes = 125,
                        Categories = ["RPG"],
                    },
                    ActiveOperation = new(
                        "operation.0123456789abcdef0123456789abcdef",
                        WidgetAppLibraryOperationKind.Update,
                        WidgetAppLibraryOperationState.Running,
                        "running"),
                    Capabilities = new([
                        WidgetAppLibraryAction.Launch,
                        WidgetAppLibraryAction.Update,
                    ]),
                },
            },
        };
        var widget = Create(host);
        await Interactive(widget);
        await Ready(widget, host);
        await Bounded(widget.WhenWarmStateIdleAsync(), "details metadata warm state");
        var tile = Nodes(Snapshot(widget, 60).Root).Single(node =>
            node.ActionId == "playnite-library.launch");
        await widget.OnActionAsync(new("playnite-library.details.open", tile.Id));
        var details = Snapshot(widget, 61);

        Assert.AreEqual("Primary action · Launch", Nodes(details.Root).Single(node =>
            node.Id == "playnite-library.details.primary-action").Text);
        Assert.AreEqual("Version · 1.2.3", Nodes(details.Root).Single(node =>
            node.Id == "playnite-library.details.version").Text);
        Assert.AreEqual("Last played · 2023-11-14 22:13 UTC",
            Nodes(details.Root).Single(node =>
                node.Id == "playnite-library.details.last-played").Text);
        Assert.AreEqual("Playtime · 2 h 5 min", Nodes(details.Root).Single(node =>
            node.Id == "playnite-library.details.playtime").Text);
        Assert.AreEqual("Operation · Update · Running", Nodes(details.Root).Single(node =>
            node.Id == "playnite-library.details.operation").Text);
        var organization = Nodes(details.Root).Single(node =>
            node.Id == "playnite-library.details.organization").Text!;
        StringAssert.Contains(organization, "Favorite");
        StringAssert.Contains(organization, "Couch");
        StringAssert.Contains(organization, "RPG");
        await Background(widget);
    }

    [TestMethod, Timeout(30_000)]
    public async Task DetailsOmitSdkValidUnrenderableLastPlayedTimestamp()
    {
        var host = new FakeHost(1)
        {
            ItemFactory = _ => Item(0) with
            {
                Presentation = Item(0).Presentation with
                {
                    Metadata = new(
                        "metadata-revision",
                        new("fixture", "record-revision", "Fixture metadata", 1))
                    {
                        LastPlayedAtUnixMilliseconds = long.MaxValue,
                    },
                },
            },
        };
        var widget = Create(host);
        await Interactive(widget);
        await Ready(widget, host);
        var tile = Nodes(Snapshot(widget, 62).Root).Single(node =>
            node.ActionId == "playnite-library.launch");

        await widget.OnActionAsync(new("playnite-library.details.open", tile.Id));
        var details = Snapshot(widget, 63);

        Assert.IsTrue(Nodes(details.Root).Any(node =>
            node.Id == "playnite-library.details.title"));
        Assert.IsFalse(Nodes(details.Root).Any(node =>
            node.Id == "playnite-library.details.last-played"));
        await Background(widget);
    }

    [TestMethod, Timeout(30_000)]
    public async Task YActionsOpenExactDetailsAndBackRestoresOriginTile()
    {
        var host = new FakeHost(2)
        {
            ItemFactory = index => WithPresentation(Item(index),
                displayName: "Shared title", source: index == 0 ? "Steam" : "Windows"),
        };
        var widget = Create(host);
        await Interactive(widget);
        await Ready(widget, host);
        var library = Snapshot(widget, 40);
        var tiles = Nodes(library.Root)
            .Where(node => node.ActionId == "playnite-library.launch").ToArray();

        Assert.IsFalse(tiles[1].Shortcuts.Any(shortcut =>
            shortcut.Button == ControllerButton.View));
        Assert.AreEqual(PlayniteLibraryActionSheet.OpenAction,
            tiles[1].Shortcuts.Single(shortcut =>
                shortcut.Button == ControllerButton.Y).ActionId);
        await widget.OnActionAsync(new(PlayniteLibraryActionSheet.OpenAction,
            tiles[1].Id, ControllerButton.Y, ControllerEventPhase.Pressed,
            InputScopeId: library.ActiveInputScopeId));
        var librarySheet = Snapshot(widget, 401);
        var viewDetails = Nodes(librarySheet.Root).Single(node =>
            node.Id == PlayniteLibraryActionSheet.DetailsItemId);
        Assert.AreEqual("View details", viewDetails.Text);
        Assert.AreEqual(PlayniteLibraryActionSheet.DetailsAction,
            viewDetails.ActionId);
        await widget.OnActionAsync(new(PlayniteLibraryActionSheet.DetailsAction,
            viewDetails.Id, ControllerButton.A, ControllerEventPhase.Pressed,
            InputScopeId: librarySheet.ActiveInputScopeId));
        var details = Snapshot(widget, 41);
        Assert.AreEqual("Shared title", Nodes(details.Root).Single(node =>
            node.Id == "playnite-library.details.title").Text);
        StringAssert.Contains(Nodes(details.Root).Single(node =>
            node.Id == "playnite-library.details.source").Text!, "Windows");
        Assert.AreEqual("playnite-library.details.launch", details.InitialFocusId);

        await widget.OnActionAsync(new(PlayniteLibraryActionSheet.OpenAction,
            "playnite-library.details.launch"));
        var detailSheet = Snapshot(widget, 411);
        Assert.AreEqual(PlayniteLibraryActionSheet.ScopeId, detailSheet.ActiveInputScopeId);
        StringAssert.Contains(Nodes(detailSheet.Root).Single(node =>
            node.Id == "playnite-library.actions.sheet.title").Text!, "Shared title");
        Assert.IsTrue(Nodes(detailSheet.Root).Any(node =>
            node.ActionId == PlayniteLibraryActionSheet.DetailsAction));
        await widget.OnActionAsync(new(PlayniteLibraryActionSheet.CloseAction,
            PlayniteLibraryActionSheet.InitialFocusId));
        Assert.IsTrue(Nodes(Snapshot(widget, 412).Root).Any(node =>
            node.Id == "playnite-library.details.root"));

        await widget.OnActionAsync(new("playnite-library.favorite",
            "playnite-library.details.favorite"));
        Assert.IsTrue(host.Authority.FavoriteGameIds.Contains(
            "saved-00001", StringComparer.Ordinal));

        await widget.OnActionAsync(new("playnite-library.launch",
            "playnite-library.details.launch"));
        CollectionAssert.AreEqual(new[] { "saved-00001" },
            host.ResolveRequests[^1].ToArray());
        CollectionAssert.AreEqual(new[] { "app-00001" }, host.Launches.ToArray());

        var afterLaunch = Snapshot(widget, 42);
        var collectionRevision = widget.Collection.Revision;
        var collectionAnchor = widget.Collection.Anchor;
        var back = afterLaunch.Root.Shortcuts.Single(shortcut =>
            shortcut.Button == ControllerButton.B);
        await widget.OnActionAsync(new(back.ActionId, "playnite-library.details.launch",
            ControllerButton.B, ControllerEventPhase.Pressed,
            InputScopeId: afterLaunch.ActiveInputScopeId));
        var returned = Snapshot(widget, 43);
        Assert.AreEqual(tiles[1].Id, returned.InitialFocusId);
        Assert.IsTrue(Nodes(returned.Root).Any(node => node.Id == tiles[1].Id));
        Assert.AreEqual("Windows", Nodes(returned.Root).Single(node =>
            node.Id == "playnite-library.hero.source").Text);
        Assert.AreEqual(collectionRevision, widget.Collection.Revision);
        Assert.AreEqual(collectionAnchor, widget.Collection.Anchor);
        await Background(widget);
    }

    [TestMethod, Timeout(30_000)]
    public async Task DetailsPreserveContextShortcutsAndFailClosedWhenIdentityDisappears()
    {
        var host = new FakeHost(1);
        var widget = Create(host);
        await Interactive(widget);
        await Ready(widget, host);
        var library = Snapshot(widget, 44);
        var tile = Nodes(library.Root).Single(node =>
            node.ActionId == "playnite-library.launch");
        Assert.IsFalse(tile.Shortcuts.Any(shortcut =>
            shortcut.Button == ControllerButton.View));
        await widget.OnActionAsync(new(PlayniteLibraryActionSheet.OpenAction,
            tile.Id, ControllerButton.Y, ControllerEventPhase.Pressed,
            InputScopeId: library.ActiveInputScopeId));
        var sheet = Snapshot(widget, 441);
        var viewDetails = Nodes(sheet.Root).Single(node =>
            node.Id == PlayniteLibraryActionSheet.DetailsItemId);
        Assert.AreEqual(PlayniteLibraryActionSheet.DetailsAction,
            viewDetails.ActionId);
        await widget.OnActionAsync(new(PlayniteLibraryActionSheet.DetailsAction,
            viewDetails.Id, ControllerButton.A, ControllerEventPhase.Pressed,
            InputScopeId: sheet.ActiveInputScopeId));
        var details = Snapshot(widget, 45);
        var detailScroll = Nodes(details.Root).Single(node =>
            node.Id == "playnite-library.details.scroll");
        Assert.IsTrue(detailScroll.Shortcuts.Any(shortcut =>
            shortcut.Button == ControllerButton.X &&
            shortcut.ActionId == "playnite-library.favorite"));
        Assert.IsTrue(detailScroll.Shortcuts.Any(shortcut =>
            shortcut.Button == ControllerButton.Y &&
            shortcut.ActionId == PlayniteLibraryActionSheet.OpenAction));
        Assert.IsFalse(detailScroll.Shortcuts.Any(shortcut =>
            shortcut.Button is ControllerButton.LeftBumper or ControllerButton.RightBumper));

        host.QueryHandler = (_, _) => ValueTask.FromResult(
            new WidgetAppLibraryPage([], null, null, "removed"));
        await widget.OnActionAsync(new("playnite-library.refresh", "playnite-library.refresh"));
        await Bounded(widget.WhenLibraryIdleAsync(), "details disappearance refresh");
        var unavailable = Snapshot(widget, 46);
        StringAssert.Contains(Nodes(unavailable.Root).Single(node =>
            node.Id == "playnite-library.details.availability").Text!, "Unavailable");
        Assert.IsTrue(Nodes(unavailable.Root).Single(node =>
            node.Id == "playnite-library.details.launch").IsDisabled);
        await widget.OnActionAsync(new("playnite-library.launch",
            "playnite-library.details.launch"));
        Assert.AreEqual(0, host.Launches.Count);
        await Background(widget);
    }

    [TestMethod, Timeout(30_000)]
    public async Task PagedDetailsDoNotReplaceLibraryBumperSemantics()
    {
        var host = new FakeHost(LauncherWidget.PageSize + 1);
        var widget = Create(host);
        await Interactive(widget);
        await Ready(widget, host);
        var library = Snapshot(widget, 47);
        var tile = Nodes(library.Root).First(node =>
            node.ActionId == "playnite-library.launch");

        await widget.OnActionAsync(new("playnite-library.details.open", tile.Id));
        var details = Snapshot(widget, 48);
        var scroll = Nodes(details.Root).Single(node =>
            node.Id == "playnite-library.details.scroll");
        Assert.IsFalse(scroll.Shortcuts.Any(shortcut => shortcut.Button is
            ControllerButton.LeftBumper or ControllerButton.RightBumper));
        await Background(widget);
    }

    [TestMethod, Timeout(30_000)]
    public async Task DetailsVariantSelectionNamesEachExactStepAndReturnsForSecondChoice()
    {
        var host = new FakeHost(2);
        var widget = Create(host);
        await Interactive(widget);
        await Ready(widget, host);
        var library = Snapshot(widget, 49);
        var tiles = Nodes(library.Root).Where(node =>
            node.ActionId == "playnite-library.launch").ToArray();

        await widget.OnActionAsync(new("playnite-library.details.open", tiles[0].Id));
        var firstDetails = Snapshot(widget, 50);
        Assert.AreEqual("Choose another variant", Nodes(firstDetails.Root).Single(node =>
            node.Id == "playnite-library.details.variant").Text);
        await widget.OnActionAsync(new("playnite-library.variant",
            "playnite-library.details.variant"));

        var returned = Snapshot(widget, 51);
        Assert.IsFalse(Nodes(returned.Root).Any(node =>
            node.Id == "playnite-library.details.root"));
        Assert.AreEqual(tiles[0].Id, returned.InitialFocusId);
        StringAssert.Contains(Nodes(returned.Root).Single(node =>
            node.Id == "playnite-library.status").Text!,
            "Variant selection started with Game 00000");

        await widget.OnActionAsync(new("playnite-library.details.open", tiles[1].Id));
        var secondDetails = Snapshot(widget, 52);
        Assert.AreEqual("Group with selected game", Nodes(secondDetails.Root).Single(node =>
            node.Id == "playnite-library.details.variant").Text);
        await widget.OnActionAsync(new("playnite-library.variant",
            "playnite-library.details.variant"));

        var group = widget.Organization.VariantGroups.Single();
        CollectionAssert.AreEquivalent(new[] { "saved-00000", "saved-00001" },
            group.SavedIds.ToArray());
        StringAssert.Contains(Nodes(Snapshot(widget, 53).Root).Single(node =>
            node.Id == "playnite-library.details.feedback").Text!,
            "Grouped Game 00000 with Game 00001");
        await Background(widget);
    }

    [TestMethod, Timeout(30_000)]
    public async Task DetailsVariantRemovalAndSameIdentityRemainExplicit()
    {
        var host = new FakeHost(2);
        var widget = Create(host);
        await Interactive(widget);
        await Ready(widget, host);
        var tiles = Nodes(Snapshot(widget, 54).Root).Where(node =>
            node.ActionId == "playnite-library.launch").ToArray();
        await widget.OnActionAsync(new("playnite-library.variant", tiles[0].Id));
        await widget.OnActionAsync(new("playnite-library.variant", tiles[1].Id));
        Assert.AreEqual(1, widget.Organization.VariantGroups.Count);

        await widget.OnActionAsync(new("playnite-library.variant", tiles[0].Id));
        await widget.OnActionAsync(new("playnite-library.details.open", tiles[1].Id));
        var removal = Snapshot(widget, 55);
        Assert.AreEqual("Remove from variant group", Nodes(removal.Root).Single(node =>
            node.Id == "playnite-library.details.variant").Text);
        await widget.OnActionAsync(new("playnite-library.variant",
            "playnite-library.details.variant"));
        Assert.AreEqual(0, widget.Organization.VariantGroups.Count);
        StringAssert.Contains(Nodes(Snapshot(widget, 56).Root).Single(node =>
            node.Id == "playnite-library.details.feedback").Text!,
            "Removed Game 00001 from its variant group");

        var details = Snapshot(widget, 57);
        var back = details.Root.Shortcuts.Single(shortcut =>
            shortcut.Button == ControllerButton.B);
        await widget.OnActionAsync(new(back.ActionId, "playnite-library.details.variant",
            ControllerButton.B, ControllerEventPhase.Pressed,
            InputScopeId: details.ActiveInputScopeId));
        await widget.OnActionAsync(new("playnite-library.variant", tiles[0].Id));
        await widget.OnActionAsync(new("playnite-library.details.open", tiles[0].Id));
        var same = Snapshot(widget, 58);
        var sameAction = Nodes(same.Root).Single(node =>
            node.Id == "playnite-library.details.variant");
        Assert.AreEqual("Choose a different game", sameAction.Text);
        Assert.IsTrue(sameAction.IsDisabled);
        await widget.OnActionAsync(new("playnite-library.variant", sameAction.Id));
        StringAssert.Contains(Nodes(Snapshot(widget, 59).Root).Single(node =>
            node.Id == "playnite-library.details.feedback").Text!,
            "Choose a different game");
        Assert.AreEqual(0, widget.Organization.VariantGroups.Count);
        await Background(widget);
    }

    [TestMethod, Timeout(30_000)]
    public async Task StaleVariantSeedFailsClosedAndHideClosesOnlyAfterCommit()
    {
        var host = new FakeHost(2);
        var widget = Create(host);
        await Interactive(widget);
        await Ready(widget, host);
        var tiles = Nodes(Snapshot(widget, 60).Root).Where(node =>
            node.ActionId == "playnite-library.launch").ToArray();
        await widget.OnActionAsync(new("playnite-library.variant", tiles[0].Id));
        host.QueryHandler = (_, _) => ValueTask.FromResult(new WidgetAppLibraryPage(
            [Item(1)], null, null, "second-only"));
        await widget.OnActionAsync(new("playnite-library.refresh", "playnite-library.refresh"));
        await Bounded(widget.WhenLibraryIdleAsync(), "stale seed refresh");
        var second = Nodes(Snapshot(widget, 61).Root).Single(node =>
            node.ActionId == "playnite-library.launch");
        await widget.OnActionAsync(new("playnite-library.details.open", second.Id));
        await widget.OnActionAsync(new("playnite-library.variant",
            "playnite-library.details.variant"));
        Assert.AreEqual(0, widget.Organization.VariantGroups.Count);
        StringAssert.Contains(Nodes(Snapshot(widget, 62).Root).Single(node =>
            node.Id == "playnite-library.details.feedback").Text!,
            "first selected game is no longer available");

        await widget.OnActionAsync(new("playnite-library.hide", "playnite-library.details.hide"));
        Assert.IsTrue(host.Authority.HiddenGameIds.Contains(
            "saved-00001", StringComparer.Ordinal));
        Assert.IsFalse(Nodes(Snapshot(widget, 63).Root).Any(node =>
            node.Id == "playnite-library.details.root"));
        await Background(widget);
    }

    [TestMethod, Timeout(30_000)]
    public async Task DetailsVariantConflictReplaysExactPairAndPreservesConcurrentState()
    {
        var privateState = new WidgetTestPrivateState();
        var host = new FakeHost(3, privateState);
        var widget = Create(host);
        await Interactive(widget);
        await Ready(widget, host);
        var tiles = Nodes(Snapshot(widget, 64).Root).Where(node =>
            node.ActionId == "playnite-library.launch").ToArray();
        await widget.OnActionAsync(new("playnite-library.variant", tiles[0].Id));
        await widget.OnActionAsync(new("playnite-library.details.open", tiles[1].Id));

        var concurrent = widget.Organization with
        {
            FavoriteSavedIds = ["saved-00002"],
        };
        privateState.SimulateExternalWriteJson(JsonSerializer.Serialize(concurrent));
        await widget.OnActionAsync(new("playnite-library.variant",
            "playnite-library.details.variant"));

        Assert.IsTrue(widget.Organization.FavoriteSavedIds.Contains(
            "saved-00002", StringComparer.Ordinal));
        CollectionAssert.AreEquivalent(new[] { "saved-00000", "saved-00001" },
            widget.Organization.VariantGroups.Single().SavedIds.ToArray());
        StringAssert.Contains(Nodes(Snapshot(widget, 65).Root).Single(node =>
            node.Id == "playnite-library.details.feedback").Text!, "Grouped");
        await Background(widget);
    }

    [TestMethod, Timeout(30_000)]
    public async Task CanceledDetailsVariantDoesNotClaimACommittedGroup()
    {
        var host = new FakeHost(2);
        var widget = Create(host);
        await Interactive(widget);
        await Ready(widget, host);
        var tiles = Nodes(Snapshot(widget, 66).Root).Where(node =>
            node.ActionId == "playnite-library.launch").ToArray();
        await widget.OnActionAsync(new("playnite-library.variant", tiles[0].Id));
        await widget.OnActionAsync(new("playnite-library.details.open", tiles[1].Id));
        using var canceled = new CancellationTokenSource();
        canceled.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(async () =>
            await widget.OnActionAsync(new("playnite-library.variant",
                "playnite-library.details.variant"), canceled.Token));

        Assert.AreEqual(0, widget.Organization.VariantGroups.Count);
        StringAssert.Contains(Nodes(Snapshot(widget, 67).Root).Single(node =>
            node.Id == "playnite-library.details.feedback").Text!,
            "Organization change was not saved");
        await Background(widget);
    }

    [TestMethod, Timeout(30_000)]
    public async Task AdjacentFailureRetainsLastGoodWindow()
    {
        var host = new FakeHost(300);
        var widget = Create(host);
        await Interactive(widget);
        await Ready(widget, host);
        host.FailAfterOffset = LauncherWidget.PageSize;
        await widget.OnActionAsync(new("playnite-library.next", "playnite-library.next"));
        await Bounded(widget.WhenLibraryIdleAsync(), "adjacent failure drain");

        Assert.AreEqual(LauncherWidget.PageSize, widget.Collection.Items.Count);
        Assert.IsNotNull(widget.Collection.Error);
        Assert.IsTrue(Nodes(widget.RenderSnapshot("launcher.test", 5).Root)
            .Any(node => node.Id == "playnite-library.retained-error"));
        await Background(widget);
    }

    [TestMethod, Timeout(30_000)]
    public async Task ArtworkAndHeroRailRemainSemanticAndBounded()
    {
        var host = new FakeHost(1)
        {
            ItemFactory = index => WithPresentation(Item(index), artworkHandle:
                "library.art.0123456789abcdef0123456789abcdef"),
        };
        var widget = Create(host);
        await Interactive(widget);
        await Ready(widget, host);
        var snapshot = widget.RenderSnapshot("launcher.test", 6);

        Assert.AreEqual(WidgetSurfaceMode.Wide, snapshot.Surface?.Mode);
        Assert.AreEqual(WidgetSurfaceAxisMode.FillAvailable, snapshot.Surface?.WidthMode);
        Assert.AreEqual(WidgetSurfaceAxisMode.FillAvailable, snapshot.Surface?.HeightMode);
        Assert.AreEqual(1600, snapshot.Surface?.PreferredWidth);
        Assert.AreEqual(1200, snapshot.Surface?.PreferredHeight);
        var rail = Nodes(snapshot.Root).Single(node =>
            node.Id == PlayniteLibraryPresentation.ScrollId);
        Assert.AreEqual(ViewNodeKind.Scroll, rail.Kind);
        Assert.AreEqual(ScrollAxis.Horizontal, rail.ScrollAxis);
        Assert.AreEqual(2, Nodes(snapshot.Root).Count(node =>
            node.ArtworkHandle == "library.art.0123456789abcdef0123456789abcdef"));
        Assert.AreEqual("Game 00000", Nodes(snapshot.Root).Single(node =>
            node.Id == "playnite-library.hero.title").Text);
        Assert.IsLessThan(100, Nodes(snapshot.Root).Count());
        await Background(widget);
    }

    [TestMethod, Timeout(30_000)]
    public async Task RailFocusUpdatesHeroWithoutLaunchAndActionsRemainExact()
    {
        var host = new FakeHost(20);
        var widget = Create(host);
        await Interactive(widget);
        await Ready(widget, host);
        var first = Snapshot(widget, 9_000);
        var tiles = Nodes(first.Root).Where(node =>
                node.ActionId == "playnite-library.launch")
            .ToArray();
        Assert.AreEqual("Game 00000", Nodes(first.Root).Single(node =>
            node.Id == "playnite-library.hero.title").Text);

        Assert.IsFalse(await Route(
            widget, first, ControllerButton.DPadRight, tiles[0].Id));
        var moved = Snapshot(widget, 9_001);
        Assert.AreEqual("Game 00001", Nodes(moved.Root).Single(node =>
            node.Id == "playnite-library.hero.title").Text);
        Assert.AreEqual(0, host.Launches.Count,
            "Focus movement must never authorize launch.");

        var movedSecond = Nodes(moved.Root).Where(node =>
                node.ActionId == "playnite-library.launch")
            .ElementAt(1);
        Assert.IsTrue(await Route(
            widget, moved, ControllerButton.A, movedSecond.Id));
        await WaitUntil(() => host.Launches.Count == 1);
        CollectionAssert.AreEqual(new[] { "app-00001" }, host.Launches.ToArray());
        await Background(widget);
    }

    [TestMethod, Timeout(30_000)]
    public async Task DeactivationDrainsCancellationIgnoringPage()
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource<WidgetAppLibraryPage>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var host = new FakeHost(0)
        {
            QueryHandler = async (_, token) =>
            {
                started.TrySetResult();
                return await release.Task.ConfigureAwait(false);
            },
        };
        var widget = Create(host);
        await Visible(widget);
        await Bounded(started.Task, "cancellation-ignoring query admission");
        var transition = WidgetTestHost.SetLifecycleStateAsync(
            widget, WidgetLifecycleState.Background).AsTask();
        release.TrySetResult(new([Item(0)], null, null, "late"));
        await Bounded(transition, "cancellation-ignoring lifecycle drain");

        Assert.AreEqual(0, widget.Collection.Items.Count);
        Assert.AreEqual(WidgetPagedResourceStatus.NotLoaded, widget.Collection.Status);
    }

    private static LauncherWidget Create(FakeHost host)
    {
        var services = host.Services();
        return WidgetTestHost.Attach(
            new LauncherWidget(new TestApplicationService(services, host)), services);
    }

    private static Task Visible(LauncherWidget widget) => Bounded(
        WidgetTestHost.SetLifecycleStateAsync(widget, WidgetLifecycleState.Visible).AsTask(),
        "visible lifecycle");

    private static Task Interactive(LauncherWidget widget) => Bounded(
        WidgetTestHost.SetLifecycleStateAsync(widget, WidgetLifecycleState.Interactive).AsTask(),
        "interactive lifecycle");

    private static Task Background(LauncherWidget widget) => Bounded(
        WidgetTestHost.SetLifecycleStateAsync(widget, WidgetLifecycleState.Background).AsTask(),
        "background lifecycle");

    private static async Task Ready(LauncherWidget widget, FakeHost host)
    {
        await Bounded(host.FirstQueryStarted.Task, "first provider query");
        await Bounded(widget.WhenLibraryIdleAsync(), "initial page drain");
        Assert.AreEqual(WidgetPagedResourceStatus.Ready, widget.Collection.Status);
    }

    private static async Task Bounded(Task task, string phase)
    {
        try { await task.WaitAsync(TimeSpan.FromSeconds(3)); }
        catch (TimeoutException exception)
        {
            throw new TimeoutException($"Timed out during {phase}.", exception);
        }
    }

    private static async Task WaitUntil(Func<bool> predicate)
    {
        for (var attempt = 0; attempt < 20_000; attempt++)
        {
            if (predicate()) return;
            await Task.Yield();
        }
        Assert.Fail("Condition did not become true.");
    }

    private static ValueTask<bool> Route(
        LauncherWidget widget,
        ViewSnapshot snapshot,
        ControllerButton button,
        string focusedElementId,
        ControllerEventPhase phase = ControllerEventPhase.Pressed) =>
        widget.OnControllerInputAsync(new ControllerInputEvent(
            button, phase, ControllerInputContext.OpenWidget,
            FocusedElementId: focusedElementId,
            Sequence: snapshot.Sequence,
            ActiveInputScopeId: snapshot.ActiveInputScopeId,
            SnapshotSequence: snapshot.Sequence));

    private static void AssertShortcutMap(
        ViewSnapshot snapshot,
        bool before,
        bool after)
    {
        var scroll = Nodes(snapshot.Root).Single(node =>
            node.Id == PlayniteLibraryPresentation.ScrollId);
        Assert.AreEqual(before, scroll.Shortcuts.Any(shortcut =>
            shortcut.Button == ControllerButton.LeftBumper &&
            shortcut.ActionId == "playnite-library.previous"));
        Assert.AreEqual(after, scroll.Shortcuts.Any(shortcut =>
            shortcut.Button == ControllerButton.RightBumper &&
            shortcut.ActionId == "playnite-library.next"));
    }

    private static void AssertValidCollectionAnchor(ViewSnapshot snapshot)
    {
        var scroll = Nodes(snapshot.Root).SingleOrDefault(node =>
            node.Id == PlayniteLibraryPresentation.ScrollId);
        if (scroll is null) return;
        var keys = Nodes(scroll).Where(node => node.CollectionItemKey is not null)
            .Select(node => node.CollectionItemKey!).ToHashSet(StringComparer.Ordinal);
        if (keys.Count == 0)
            Assert.IsNull(scroll.CollectionAnchorKey);
        else
            Assert.IsTrue(keys.Contains(scroll.CollectionAnchorKey ?? string.Empty),
                "The collection anchor must identify one currently rendered keyed row.");
    }

    private static IEnumerable<ViewNode> Nodes(ViewNode root)
    {
        yield return root;
        foreach (var child in root.Children)
        foreach (var node in Nodes(child)) yield return node;
    }

    private static ViewSnapshot Snapshot(LauncherWidget widget, long sequence)
    {
        try { return widget.RenderSnapshot("launcher.test", sequence); }
        catch (ProtocolValidationException exception)
        {
            Assert.Fail(string.Join(Environment.NewLine, exception.Errors.Select(error =>
                $"{error.Path}: {error.Code}: {error.Message}")));
            throw;
        }
    }

    private sealed class FakeConnectionClient : IPlayniteBridgeClient
    {
        internal int ProbeCalls { get; private set; }

        public ValueTask<PlayniteBridgeConnectionResult> ProbeAsync(
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ProbeCalls++;
            return ValueTask.FromResult(new PlayniteBridgeConnectionResult(
                PlayniteBridgeConnectionKind.NotConfigured, "credential_missing"));
        }

        public ValueTask SaveCredentialAsync(
            string token, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Credential mutation is outside this fixture.");

        public ValueTask DeleteCredentialAsync(CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Credential mutation is outside this fixture.");

        public void Dispose() { }
    }

    private static WidgetAppLibraryItem Item(int index) =>
        InstalledItem(
            $"app-{index:D5}", $"saved-{index:D5}", $"Game {index:D5}",
            WidgetAppLibraryKind.Game,
            index % 2 == 0 ? "source-steam" : "source-windows",
            index % 2 == 0 ? "Steam" : "Windows");

    private static WidgetAppLibraryItem InstalledItem(
        string appId,
        string savedId,
        string displayName,
        WidgetAppLibraryKind kind,
        string sourceId,
        string sourceDisplayName) =>
        new(appId, savedId, new(
            displayName,
            kind,
            new(sourceId, sourceDisplayName),
            new(WidgetAppLibraryAvailabilityState.Installed, true, "installed"),
            new([]),
            Metadata: null,
            new([WidgetAppLibraryAction.Launch]),
            ActiveOperation: null));

    private static WidgetAppLibraryItem WithPresentation(
        WidgetAppLibraryItem item,
        string? displayName = null,
        WidgetAppLibraryKind? kind = null,
        string? source = null,
        string? artworkHandle = null)
    {
        var presentation = item.Presentation;
        var updatedKind = kind ?? presentation.Kind;
        WidgetAppLibraryArtworkSet artwork = artworkHandle is null
            ? presentation.Artwork
            : new([
                new(WidgetAppLibraryArtworkRole.Tile, artworkHandle, "fixture",
                    updatedKind == WidgetAppLibraryKind.Game
                        ? WidgetAppLibraryArtworkFallback.Game
                        : WidgetAppLibraryArtworkFallback.Application),
                new(WidgetAppLibraryArtworkRole.Hero, artworkHandle, "fixture",
                    updatedKind == WidgetAppLibraryKind.Game
                        ? WidgetAppLibraryArtworkFallback.Game
                        : WidgetAppLibraryArtworkFallback.Application),
            ]);
        return item with
        {
            Presentation = presentation with
            {
                DisplayName = displayName ?? presentation.DisplayName,
                Kind = updatedKind,
                Source = source is null ? presentation.Source :
                    presentation.Source with { DisplayName = source },
                Artwork = artwork,
            },
        };
    }

    private sealed class TestApplicationService(
        WidgetHostServices services,
        FakeHost host) :
        IPlayniteLibraryApplicationService
    {
        public bool OwnsArtworkContent => false;

        public ValueTask<WidgetAppLibraryPage> QueryAsync(
            WidgetAppLibraryQuery query, WidgetCollectionCursor? cursor,
            WidgetCursorDirection? direction, int limit, bool refresh,
            CancellationToken cancellationToken) => services.AppLibrary.QueryAsync(
                query, cursor, direction, limit, refresh, cancellationToken);

        public async ValueTask<PlayniteLibraryQueryResult> QueryWithAuthorityAsync(
            WidgetAppLibraryQuery query,
            PlayniteLibraryQueryContext context,
            WidgetCollectionCursor? cursor,
            WidgetCursorDirection? direction,
            int limit,
            bool refresh,
            CancellationToken cancellationToken)
        {
            return await host.QueryWithCurrentAuthorityAsync(
                query, context, cursor, direction, limit, refresh, cancellationToken);
        }

        public ValueTask<IReadOnlyList<WidgetAppLibraryItem>> ResolveSavedAsync(
            IReadOnlyList<string> savedIds, CancellationToken cancellationToken) =>
            services.AppLibrary.ResolveSavedAsync(savedIds, cancellationToken);

        public ValueTask<WidgetRunningAppObservation> ObserveRunningAsync(
            CancellationToken cancellationToken) =>
            services.AppLibrary.ObserveRunningAsync(cancellationToken);

        public ValueTask<WidgetAppLibraryItem?> ConfirmRunningAsync(
            string savedId, string revision, CancellationToken cancellationToken) =>
            services.AppLibrary.ConfirmRunningAsync(
                savedId, revision, cancellationToken);

        public ValueTask<WidgetAppLaunchObservation> LaunchObservedAsync(
            string appId, WidgetAppLaunchOverlayBehavior overlayBehavior,
            CancellationToken cancellationToken) =>
            services.AppLibrary.LaunchObservedAsync(
                appId, overlayBehavior, cancellationToken);

        public ValueTask<WidgetEncodedArtwork?> ResolveArtworkAsync(
            WidgetArtworkHandle handle, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult<WidgetEncodedArtwork?>(null);
        }

        public async ValueTask<WidgetAppLibraryItem?> SetFavoriteAsync(
            string gameId, bool favorite, CancellationToken cancellationToken)
        {
            host.SetFavorite(gameId, favorite);
            return (await ResolveSavedAsync([gameId], cancellationToken)).SingleOrDefault();
        }

        public async ValueTask<WidgetAppLibraryItem?> SetHiddenAsync(
            string gameId, bool hidden, CancellationToken cancellationToken)
        {
            host.SetHidden(gameId, hidden);
            return (await ResolveSavedAsync([gameId], cancellationToken)).SingleOrDefault();
        }

        public async ValueTask<WidgetAppLibraryItem?> SetCategoriesAsync(
            string gameId, IReadOnlyList<string> categories,
            CancellationToken cancellationToken)
        {
            host.SetCategories(gameId, categories);
            return (await ResolveSavedAsync([gameId], cancellationToken)).SingleOrDefault();
        }

        public async ValueTask<WidgetAppLibraryItem?> SetCompletionStatusAsync(
            string gameId, string completionStatus, CancellationToken cancellationToken)
        {
            host.SetCompletionStatus(gameId, completionStatus);
            return (await ResolveSavedAsync([gameId], cancellationToken)).SingleOrDefault();
        }

        public ValueTask<PlayniteLibraryCategory?> CreateCategoryAsync(
            string name, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(host.CreateCategory(name));
        }

        public ValueTask<IReadOnlyList<string>> GetCompletionStatusesAsync(
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult<IReadOnlyList<string>>(
                ["Not Played", "Playing", "Completed"]);
        }

        public ValueTask<WidgetPrivateStateValue<PlayniteLibraryPrivateState>> ReadStateAsync(
            CancellationToken cancellationToken) =>
            services.PrivateState.ReadAsync<PlayniteLibraryPrivateState>(
                cancellationToken: cancellationToken);

        public ValueTask<WidgetPrivateStateMutation> WriteStateAsync(
            PlayniteLibraryPrivateState state, long? expectedRevision,
            CancellationToken cancellationToken) => services.PrivateState.WriteAsync(
                state, expectedRevision, cancellationToken: cancellationToken);

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class FakeHost
    {
        private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<
            WidgetTestPrivateState, AuthorityBox> Authorities = new();
        private readonly int _count;
        private readonly WidgetTestPrivateState _state;
        private readonly AuthorityBox _authority;
        internal WidgetTestPrivateState State => _state;
        internal PlayniteLibraryAuthorityProjection Authority => _authority.Value;
        internal int MaximumObservedIndex { get; private set; } = -1;
        internal int MaximumRequestedLimit { get; private set; }
        internal int RunningObservationCount { get; private set; }
        internal int? FailAfterOffset { get; set; }
        internal Func<int, WidgetAppLibraryItem> ItemFactory { get; set; } = Item;
        internal Func<WidgetAppLibraryCursorRequest, CancellationToken,
            ValueTask<WidgetAppLibraryPage>>? QueryHandler { get; set; }
        internal Func<ResolveSavedWidgetAppLibraryItemsRequest,
            IReadOnlyList<WidgetAppLibraryItem>>? ResolveHandler { get; set; }
        internal List<IReadOnlyList<string>> ResolveRequests { get; } = [];
        internal List<string> Launches { get; } = [];
        internal List<WidgetAppLibraryCursorRequest> Queries { get; } = [];
        internal Func<LaunchWidgetAppLibraryItemRequest, CancellationToken,
            ValueTask<WidgetAppLaunchObservation>>? LaunchHandler { get; set; }
        internal Func<WriteWidgetPrivateStateTransportRequest, CancellationToken,
            ValueTask<WidgetPrivateStateTransportMutation>>? StateWriteHandler { get; set; }
        internal WidgetRunningAppObservation RunningObservation { get; set; } =
            new([], "running-empty");
        internal Func<ConfirmWidgetRunningAppRequest, WidgetAppLibraryItem?>?
            ConfirmRunningHandler { get; set; }
        internal List<ConfirmWidgetRunningAppRequest> RunningConfirmations { get; } = [];
        internal TaskCompletionSource FirstQueryStarted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        internal FakeHost(int count, WidgetTestPrivateState? state = null)
        {
            _count = count;
            _state = state ?? new WidgetTestPrivateState();
            _authority = Authorities.GetValue(_state, CreateAuthority);
        }

        internal async ValueTask<PlayniteLibraryQueryResult> QueryWithCurrentAuthorityAsync(
            WidgetAppLibraryQuery query,
            PlayniteLibraryQueryContext context,
            WidgetCollectionCursor? cursor,
            WidgetCursorDirection? direction,
            int limit,
            bool refresh,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var request = new WidgetAppLibraryCursorRequest(
                query, cursor?.Value, direction, limit, refresh);
            if (QueryHandler is not null)
            {
                var custom = await Query(request, cancellationToken);
                return new(FilterPage(custom, query, context), _authority.Value);
            }

            FirstQueryStarted.TrySetResult();
            Queries.Add(request);
            var source = Enumerable.Range(0, _count)
                .Select(index => (Index: index, Item: ItemFactory(index)));
            var authority = _authority.Value;
            source = Filter(source, query, context, authority);
            var values = source.ToArray();
            var offset = request.Cursor is null ? 0 : int.Parse(
                request.Cursor.AsSpan(request.Cursor.LastIndexOf('.') + 1),
                System.Globalization.CultureInfo.InvariantCulture);
            if (FailAfterOffset == offset)
                throw new WidgetCapabilityException("platform_unavailable", "private");
            MaximumRequestedLimit = Math.Max(MaximumRequestedLimit, limit);
            var pageValues = values.Skip(offset).Take(limit).ToArray();
            if (pageValues.Length != 0)
                MaximumObservedIndex = Math.Max(
                    MaximumObservedIndex, pageValues.Max(value => value.Index));
            var before = offset == 0 ? null :
                $"cursor.{Math.Max(0, offset - limit)}";
            var after = offset + pageValues.Length < values.Length
                ? $"cursor.{offset + pageValues.Length}"
                : null;
            var page = new WidgetAppLibraryPage(
                pageValues.Select(value => value.Item).ToArray(),
                before, after, "revision-1");
            return new(page, authority);
        }

        private WidgetAppLibraryPage FilterPage(
            WidgetAppLibraryPage page,
            WidgetAppLibraryQuery query,
            PlayniteLibraryQueryContext context)
        {
            var indexed = page.Items.Select((item, index) => (Index: index, Item: item));
            var filtered = Filter(indexed, query, context, _authority.Value)
                .Select(value => value.Item).ToArray();
            return new(filtered, page.Before, page.After, page.Revision)
            {
                Sources = page.Sources,
            };
        }

        private static IEnumerable<(int Index, WidgetAppLibraryItem Item)> Filter(
            IEnumerable<(int Index, WidgetAppLibraryItem Item)> source,
            WidgetAppLibraryQuery query,
            PlayniteLibraryQueryContext context,
            PlayniteLibraryAuthorityProjection authority)
        {
            var items = source;
            if (context.Scope == PlayniteLibraryQueryScope.Hidden)
                items = items.Where(value => authority.HiddenGameIds.Contains(
                    value.Item.SavedId, StringComparer.Ordinal));
            else
                items = items.Where(value => !authority.HiddenGameIds.Contains(
                    value.Item.SavedId, StringComparer.Ordinal));
            if (context.Scope == PlayniteLibraryQueryScope.Category)
            {
                var members = authority.Categories.FirstOrDefault(category =>
                    string.Equals(category.Name, context.CategoryName,
                        StringComparison.OrdinalIgnoreCase))?.SavedIds ?? [];
                items = items.Where(value => members.Contains(
                    value.Item.SavedId, StringComparer.Ordinal));
            }
            if (query.FavoriteSavedIds.Count != 0)
                items = items.Where(value => query.FavoriteSavedIds.Contains(
                    value.Item.SavedId, StringComparer.Ordinal));
            return items;
        }

        internal void SetFavorite(string gameId, bool favorite)
        {
            var values = _authority.Value.FavoriteGameIds
                .Where(value => value != gameId).ToList();
            if (favorite) values.Add(gameId);
            _authority.Value = _authority.Value with { FavoriteGameIds = values };
        }

        internal void SetHidden(string gameId, bool hidden)
        {
            var values = _authority.Value.HiddenGameIds
                .Where(value => value != gameId).ToList();
            if (hidden) values.Add(gameId);
            _authority.Value = _authority.Value with { HiddenGameIds = values };
        }

        internal void SetCategories(string gameId, IReadOnlyList<string> names)
        {
            _authority.Value = _authority.Value with
            {
                Categories = _authority.Value.Categories.Select(category => category with
                {
                    SavedIds = names.Contains(category.Name,
                            StringComparer.OrdinalIgnoreCase)
                        ? category.SavedIds.Append(gameId).Distinct(StringComparer.Ordinal).ToArray()
                        : category.SavedIds.Where(value => value != gameId).ToArray(),
                }).ToArray(),
            };
        }

        internal void SetCompletionStatus(string gameId, string completionStatus)
        {
            var values = new Dictionary<string, string?>(
                _authority.Value.CompletionStatuses, StringComparer.Ordinal)
            {
                [gameId] = completionStatus,
            };
            _authority.Value = _authority.Value with { CompletionStatuses = values };
        }

        internal PlayniteLibraryCategory? CreateCategory(string name)
        {
            if (_authority.Value.Categories.Any(category => string.Equals(
                    category.Name, name, StringComparison.OrdinalIgnoreCase))) return null;
            var created = new PlayniteLibraryCategory(
                "category." + (_authority.Value.Categories.Count + 1).ToString("x32"),
                name, []);
            _authority.Value = _authority.Value with
            {
                Categories = _authority.Value.Categories.Append(created).ToArray(),
            };
            return created;
        }

        private static AuthorityBox CreateAuthority(WidgetTestPrivateState state)
        {
            PlayniteLibraryPrivateState? persisted = null;
            if (state.Json is { } json)
                try { persisted = JsonSerializer.Deserialize<PlayniteLibraryPrivateState>(json); }
                catch (JsonException) { }
            persisted = PlayniteLibraryOrganizationPolicy.Normalize(persisted);
            return new(new(
                persisted.FavoriteSavedIds.ToArray(),
                persisted.ExcludedSavedIds.ToArray(),
                persisted.Categories.ToArray(),
                new Dictionary<string, string?>(StringComparer.Ordinal)));
        }

        private sealed class AuthorityBox(PlayniteLibraryAuthorityProjection value)
        {
            internal PlayniteLibraryAuthorityProjection Value { get; set; } = value;
        }

        internal WidgetHostServices Services()
        {
            var builder = new WidgetTestHostServicesBuilder()
            .WithHandler(WidgetAppLibraryCapabilities.GetPage, Query)
            .WithHandler(WidgetAppLibraryCapabilities.ResolveSaved, Resolve)
            .WithHandler(WidgetAppLibraryCapabilities.Launch, Launch)
            .WithHandler(WidgetAppLibraryCapabilities.LaunchObserved, LaunchObserved)
            .WithHandler(WidgetAppLibraryCapabilities.ObserveRunning,
                (_, token) =>
                {
                    token.ThrowIfCancellationRequested();
                    RunningObservationCount++;
                    return ValueTask.FromResult(RunningObservation);
                })
            .WithHandler(WidgetAppLibraryCapabilities.ConfirmRunning,
                (request, token) =>
                {
                    token.ThrowIfCancellationRequested();
                    RunningConfirmations.Add(request);
                    return ValueTask.FromResult(new ConfirmWidgetRunningAppResponse(
                        ConfirmRunningHandler?.Invoke(request)));
                })
            .WithHandler(WidgetPrivateStateCapabilities.Read, _state.ReadAsync)
            .WithHandler(WidgetPrivateStateCapabilities.Write,
                (request, token) => StateWriteHandler is null
                    ? _state.WriteAsync(request, token)
                    : StateWriteHandler(request, token))
            .WithHandler(WidgetPrivateStateCapabilities.Clear, _state.ClearAsync);
            return builder.Build();
        }

        private ValueTask<WidgetAppLibraryPage> Query(
            WidgetAppLibraryCursorRequest request,
            CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            FirstQueryStarted.TrySetResult();
            Queries.Add(request);
            if (QueryHandler is not null) return QueryHandler(request, token);
            var offset = request.Cursor is null ? 0 : int.Parse(
                request.Cursor.AsSpan(request.Cursor.LastIndexOf('.') + 1),
                System.Globalization.CultureInfo.InvariantCulture);
            if (FailAfterOffset == offset)
                return ValueTask.FromException<WidgetAppLibraryPage>(
                    new WidgetCapabilityException("platform_unavailable", "private"));
            MaximumRequestedLimit = Math.Max(MaximumRequestedLimit, request.Limit);
            var count = Math.Min(request.Limit, Math.Max(0, _count - offset));
            if (count != 0) MaximumObservedIndex = Math.Max(MaximumObservedIndex, offset + count - 1);
            var items = Enumerable.Range(offset, count).Select(ItemFactory).ToArray();
            var before = offset == 0 ? null : $"cursor.{Math.Max(0, offset - request.Limit)}";
            var after = offset + count < _count ? $"cursor.{offset + count}" : null;
            return ValueTask.FromResult(new WidgetAppLibraryPage(items, before, after, "revision-1"));
        }

        private ValueTask<ResolveSavedWidgetAppLibraryItemsResponse> Resolve(
            ResolveSavedWidgetAppLibraryItemsRequest request,
            CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            ResolveRequests.Add(request.SavedIds.ToArray());
            var values = ResolveHandler?.Invoke(request) ?? request.SavedIds.Select(saved =>
            {
                var index = int.Parse(saved.AsSpan(saved.LastIndexOf('-') + 1),
                    System.Globalization.CultureInfo.InvariantCulture);
                return ItemFactory(index);
            }).ToArray();
            return ValueTask.FromResult(new ResolveSavedWidgetAppLibraryItemsResponse(values));
        }

        private ValueTask<WidgetCapabilityAcknowledgement> Launch(
            LaunchWidgetAppLibraryItemRequest request,
            CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            Launches.Add(request.AppId);
            return ValueTask.FromResult(new WidgetCapabilityAcknowledgement(true));
        }

        private ValueTask<WidgetAppLaunchObservation> LaunchObserved(
            LaunchWidgetAppLibraryItemRequest request,
            CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            Launches.Add(request.AppId);
            return LaunchHandler?.Invoke(request, token) ??
                ValueTask.FromResult(new WidgetAppLaunchObservation(
                    WidgetAppLaunchObservationState.RequestAccepted, false, false));
        }
    }
}
