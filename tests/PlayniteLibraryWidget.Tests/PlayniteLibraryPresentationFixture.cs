using Microsoft.VisualStudio.TestTools.UnitTesting;
using WidgetRail.Samples.PlayniteLibrary;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

namespace WidgetRail.Tests.PlayniteLibrary;

public sealed partial class PlayniteLibraryLayoutTests
{
    private sealed record CapturedPresentation(WidgetView View, ViewSnapshot Snapshot,
        IReadOnlyList<IndexedCollectionRange> Ranges)
    {
        internal IEnumerable<ViewNode> RowNodes => Ranges.SelectMany(range => range.Items).SelectMany(row => Nodes(row.Root));
        internal IEnumerable<ViewNode> ParentAndRowNodes => Nodes(Snapshot.Root).Concat(RowNodes);
    }

    private static async Task<CapturedPresentation> CapturePresentationAsync(PlayniteLibraryPresentationState state)
    {
        await using var fixture = new IndexedPresentationFixture(state, state.Collection.Items);
        await fixture.StartAsync();
        using var host = WidgetTestHost.CreateIndexedCollectionHost(fixture, "playnite.presentation");
        var snapshot = host.CurrentSnapshot;
        Assert.AreEqual(0, ViewSnapshotValidator.Validate(snapshot).Count);
        var ranges = new List<IndexedCollectionRange>();
        foreach (var node in Nodes(snapshot.Root).Where(node => node.Kind == ViewNodeKind.IndexedCollection))
        {
            Assert.AreEqual(0, node.Children.Count);
            for (var start = 0; start < node.IndexedCollection!.Count; start += 64)
            {
                using var lease = await host.AcquireAsync(node.Id, start, Math.Min(64, node.IndexedCollection.Count - start));
                ranges.Add(lease.Range);
            }
        }
        return new(fixture.Render(), snapshot, ranges);
    }

    // Uses the same page and indexed renderers as RenderCore. Parent snapshots
    // deliberately contain no eager poster trees; tests must acquire row leases.
    private sealed class IndexedPresentationFixture : Widget, IAsyncDisposable
    {
        private readonly PlayniteLibraryPresentationState state;
        private readonly WidgetIndexedCollection<PlayniteLibraryHomeContent, PlayniteLibraryHomeItem>? home;
        private readonly WidgetIndexedCollection<PlayniteLibraryBrowseContent, PlayniteLibraryBrowseItem>? browse;

        internal IndexedPresentationFixture(PlayniteLibraryPresentationState state,
            IReadOnlyList<PlayniteLibraryItem> items, bool warmDisplayOnly = false)
        {
            this.state = state with { Collection = state.Collection with { Items = [] } };
            var byId = items.ToDictionary(item => item.Value.SavedId, StringComparer.Ordinal);
            var games = items.Select(item => new PlayniteBridgeGame(item.Value.SavedId,
                item.Presentation.DisplayName, item.Presentation.Source.DisplayName,
                true, false, false, null, [], [], [], 0, null)).ToArray();
            var capture = new PlayniteLibraryCapturedQuery(state.Query,
                new(state.Route == PlayniteLibraryRoute.Library ? PlayniteLibraryQueryScope.Home : PlayniteLibraryQueryScope.Library),
                games, PlayniteLibraryAuthorityProjection.Empty, state.Sources, "presentation-fixture", 0, false,
                game => byId[game.Id], (_, _, _) => ValueTask.FromResult<WidgetEncodedArtwork?>(null));
            if (state.Route == PlayniteLibraryRoute.Library)
            {
                var query = new PlayniteLibraryHomeQuery(capture, state.Organization,
                    state.FixedRows, state.FavoriteFilter, warmDisplayOnly);
                home = CreateIndexedCollection("playnite-library.home",
                    new PlayniteLibraryHomeContent(query, !warmDisplayOnly && !state.OrganizationBusy,
                        state.LaunchingSavedId, state.LaunchStates), query.Count,
                    PlayniteLibraryIndexedHome.Options((_, _) => ValueTask.FromResult<WidgetEncodedArtwork?>(null),
                        (_, _, _, _) => throw new InvalidOperationException("Presentation fixtures do not dispatch actions.")));
            }
            else if (state.Route == PlayniteLibraryRoute.Browse)
            {
                var query = new PlayniteLibraryBrowseQuery(capture, state.Organization, state.FavoriteFilter);
                browse = CreateIndexedCollection("playnite-library.browse",
                    new PlayniteLibraryBrowseContent(query, !state.OrganizationBusy && !state.BrowseRetained,
                        state.LaunchingSavedId, state.LaunchStates), query.Count,
                    PlayniteLibraryIndexedBrowse.Options((_, _, _, _) =>
                        throw new InvalidOperationException("Presentation fixtures do not dispatch actions.")));
            }
        }

        internal async Task StartAsync()
        {
            await WidgetTestHost.InitializeAsync(this);
            await WidgetTestHost.SetLifecycleStateAsync(this, WidgetLifecycleState.Visible);
        }

        public override WidgetView Render() => PlayniteLibraryPresentation.Render(state, indexedBrowse: browse, indexedHome: home);

        public ValueTask DisposeAsync() => WidgetTestHost.DestroyAsync(this);
    }
}
