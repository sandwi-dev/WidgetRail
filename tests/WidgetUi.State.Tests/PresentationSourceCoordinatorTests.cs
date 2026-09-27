using Microsoft.VisualStudio.TestTools.UnitTesting;
using WidgetRail.WidgetUi.State;

namespace WidgetRail.Tests.WidgetUi.State;

[TestClass]
public sealed class PresentationSourceCoordinatorTests
{
    private static readonly PresentationAuthority Authority = new("runtime-1", "playnite", "main");
    private static readonly PresentationSurfaceId Background = new("home", "background", PresentationSurfaceKind.Background);
    private static readonly PresentationSurfaceId Fragment = new("home", "summary", PresentationSurfaceKind.FocusFragment);
    private static readonly PresentationSourceId GameA = new("home", "poster-a", "poster", "games/game-a");
    private static readonly PresentationSourceId GameB = new("home", "poster-b", "poster", "games/game-b");
    private static readonly PresentationSourceId Modal = new("details", "play", "button");

    [TestMethod]
    public void DefaultsDifferAndExplicitRetentionKeepsBothSlots()
    {
        var coordinator = new PresentationSourceCoordinator<string>();
        var revision = Revision();
        AssertSlots(coordinator.Resolve(revision, GameA), "art-a", "title-a");
        AssertSlots(coordinator.Resolve(revision, Modal), "art-a", "default-title");
        revision = Revision(retainFragment: true);
        coordinator.Resolve(revision, GameA);
        AssertSlots(coordinator.Resolve(revision, null), "art-a", "title-a");
    }

    [TestMethod]
    public void SelectedSourceResolvesCurrentContentAndChangesBothSlotsTogether()
    {
        var coordinator = new PresentationSourceCoordinator<string>();
        var initial = coordinator.Resolve(Revision(retainFragment: true), GameA);
        AssertSlots(coordinator.Resolve(Revision(retainFragment: true, suffix: "-new"), Modal), "art-a-new", "title-a-new");
        AssertSlots(coordinator.Resolve(Revision(retainFragment: true), GameB), "art-b", "title-b");
        AssertSlots(initial, "art-a", "title-a");
    }

    [TestMethod]
    public void EvictionClearsSourceAndReadmissionDoesNotResurrectIt()
    {
        var coordinator = new PresentationSourceCoordinator<string>();
        coordinator.Resolve(Revision(retainFragment: true), GameA);
        AssertSlots(coordinator.Resolve(Revision(retainFragment: true, omitA: true), null), "default-art", "default-title");
        AssertSlots(coordinator.Resolve(Revision(retainFragment: true), null), "default-art", "default-title");
        AssertSlots(coordinator.Resolve(Revision(retainFragment: true), GameA), "art-a", "title-a");
    }

    [TestMethod]
    public void RecycledElementIdWithDifferentLogicalKeyCannotInheritSource()
    {
        var coordinator = new PresentationSourceCoordinator<string>();
        coordinator.Resolve(Revision(retainFragment: true), GameA);
        var replacement = GameA with { ItemIdentity = "games/replacement" };
        var revision = new PresentationRevision<string>(Authority,
            [new(Background, "default-art")], [new(replacement, Background, "replacement-art")]);
        Assert.AreEqual("default-art", coordinator.Resolve(revision, null)[0].Content);
        Assert.AreEqual("replacement-art", coordinator.Resolve(revision, replacement)[0].Content);
    }

    [TestMethod]
    public void RemovedSlotDoesNotRestoreSelectionWhenItReturns()
    {
        var coordinator = new PresentationSourceCoordinator<string>();
        coordinator.Resolve(Revision(), GameA);
        coordinator.Resolve(new(Authority, [], []), null);
        AssertSlots(coordinator.Resolve(Revision(), null), "default-art", "default-title");
    }

    [TestMethod]
    public void NestedOwnershipIsIndependentAndFocusDoesNotLeakOutward()
    {
        var nested = Background with { ElementId = "nested" };
        var revision = new PresentationRevision<string>(Authority,
            [new(Background, "outer-default"), new(nested, "inner-default"), new(Fragment, "summary-default")],
            [new(GameA, Background, "outer-a"), new(GameB, nested, "inner-b"), new(GameB, Fragment, "summary-b")]);
        var coordinator = new PresentationSourceCoordinator<string>();
        coordinator.Resolve(revision, GameA);
        var selected = coordinator.Resolve(revision, GameB);
        Assert.AreEqual("outer-a", selected[0].Content);
        Assert.AreEqual("inner-b", selected[1].Content);
        Assert.AreEqual("summary-b", selected[2].Content);
    }

    [TestMethod]
    public void ReparentedSourceCannotRemainSelectedByFormerOwner()
    {
        var coordinator = new PresentationSourceCoordinator<string>();
        coordinator.Resolve(Revision(), GameA);
        var nested = Background with { ElementId = "nested" };
        var revision = new PresentationRevision<string>(Authority,
            [new(Background, "outer-default"), new(nested, "inner-default")],
            [new(GameA, nested, "inner-a")]);
        var selected = coordinator.Resolve(revision, null);
        Assert.AreEqual("outer-default", selected[0].Content);
        Assert.AreEqual("inner-default", selected[1].Content);
    }

    [TestMethod]
    public void RuntimeInstanceAndPresentationChangesRetireMemory()
    {
        foreach (var authority in new[]
        {
            Authority with { RuntimeId = "runtime-2" },
            Authority with { WidgetInstanceId = "another-widget" },
            Authority with { PresentationId = "pinned" },
        })
        {
            var coordinator = new PresentationSourceCoordinator<string>();
            coordinator.Resolve(Revision(retainFragment: true), GameA);
            AssertSlots(coordinator.Resolve(Revision(retainFragment: true, authority: authority), null), "default-art", "default-title");
            AssertSlots(coordinator.Resolve(Revision(retainFragment: true), null), "default-art", "default-title");
        }
    }

    [TestMethod]
    public void UnrealizationThemeAndScaleDoNotInvalidateLogicalMembership()
    {
        // No realized-element, pixel, theme or scale state exists in the coordinator.
        // Re-admitting equal logical membership preserves semantics independently.
        var coordinator = new PresentationSourceCoordinator<string>();
        coordinator.Resolve(Revision(retainFragment: true), GameA);
        for (var i = 0; i < 5; ++i)
            AssertSlots(coordinator.Resolve(Revision(retainFragment: true), null), "art-a", "title-a");
    }

    [TestMethod]
    public void DisablingRetentionClearsRememberedSource()
    {
        var coordinator = new PresentationSourceCoordinator<string>();
        coordinator.Resolve(Revision(), GameA);
        var disabled = new PresentationRevision<string>(Authority,
            [new(Background, "off", false)], [new(GameA, Background, "art-a")]);
        Assert.AreEqual("off", coordinator.Resolve(disabled, null)[0].Content);
        AssertSlots(coordinator.Resolve(Revision(), null), "default-art", "default-title");
    }

    [TestMethod]
    public void MissingDeclarationFallsBackToCurrentAuthoredDefault()
    {
        var coordinator = new PresentationSourceCoordinator<string>();
        coordinator.Resolve(Revision(retainFragment: true), GameA);
        var revision = new PresentationRevision<string>(Authority,
            [new(Background, "new-default-art"), new(Fragment, "new-default-title", true)],
            [new(GameA, Fragment, "title-only")]);
        AssertSlots(coordinator.Resolve(revision, null), "new-default-art", "title-only");
    }

    [TestMethod]
    public void ScopeOrRoleReplacementCannotInheritMemory()
    {
        foreach (var source in new[] { GameA with { ScopeId = "library" }, GameA with { Role = "button" } })
        {
            var coordinator = new PresentationSourceCoordinator<string>();
            coordinator.Resolve(Revision(), GameA);
            var revision = new PresentationRevision<string>(Authority,
                [new(Background, "default")], [new(source, Background, "replacement")]);
            Assert.AreEqual("default", coordinator.Resolve(revision, null)[0].Content);
        }
    }

    [TestMethod]
    public void RevisionAndSelectionsCannotBeMutatedThroughCallerCollections()
    {
        var surfaces = new List<PresentationSurface<string>> { new(Background, "default") };
        var contributions = new List<PresentationContribution<string>> { new(GameA, Background, "art") };
        var revision = new PresentationRevision<string>(Authority, surfaces, contributions);
        surfaces.Clear();
        contributions.Clear();
        var result = new PresentationSourceCoordinator<string>().Resolve(revision, GameA);
        Assert.AreEqual("art", result[0].Content);
        Assert.ThrowsExactly<NotSupportedException>(() => ((IList<PresentationSelection<string>>)result).Clear());
        Assert.ThrowsExactly<NotSupportedException>(() => ((IList<PresentationSurface<string>>)revision.Surfaces).Clear());
        Assert.ThrowsExactly<NotSupportedException>(() => ((IList<PresentationContribution<string>>)revision.Contributions).Clear());
    }

    [TestMethod]
    public void InvalidMembershipIsRejectedBeforeItCanChangeCoordinatorState()
    {
        var coordinator = new PresentationSourceCoordinator<string>();
        coordinator.Resolve(Revision(), GameA);
        Assert.ThrowsExactly<ArgumentException>(() => new PresentationRevision<string>(Authority,
            [new(Background, "one"), new(Background, "two")], []));
        Assert.ThrowsExactly<ArgumentException>(() => new PresentationRevision<string>(Authority,
            [], [new(GameA, Background, "orphan")]));
        Assert.ThrowsExactly<ArgumentException>(() => new PresentationRevision<string>(Authority,
            [new(Background, "default")], [new(GameA, Background, "one"), new(GameA, Background, "two")]));
        Assert.ThrowsExactly<ArgumentException>(() => new PresentationRevision<string>(Authority,
            [new(Background, "default"), new(Fragment, "default")],
            [new(GameA, Background, "one"), new(GameA with { ItemIdentity = "other" }, Fragment, "two")]));
        AssertSlots(coordinator.Resolve(Revision(), null), "art-a", "default-title");
    }

    [TestMethod]
    public void ClearRetiresRememberedSources()
    {
        var coordinator = new PresentationSourceCoordinator<string>();
        coordinator.Resolve(Revision(retainFragment: true), GameA);
        coordinator.Clear();
        AssertSlots(coordinator.Resolve(Revision(retainFragment: true), null), "default-art", "default-title");
    }

    private static PresentationRevision<string> Revision(bool? retainFragment = null,
        string suffix = "", bool omitA = false, PresentationAuthority? authority = null)
    {
        var contributions = new List<PresentationContribution<string>>
        {
            new(GameB, Background, "art-b" + suffix), new(GameB, Fragment, "title-b" + suffix),
        };
        if (!omitA)
        {
            contributions.Add(new(GameA, Background, "art-a" + suffix));
            contributions.Add(new(GameA, Fragment, "title-a" + suffix));
        }
        return new(authority ?? Authority,
            [new(Background, "default-art"), new(Fragment, "default-title", retainFragment)], contributions);
    }

    private static void AssertSlots(IReadOnlyList<PresentationSelection<string>> selections, string artwork, string title)
    {
        Assert.AreEqual(2, selections.Count);
        Assert.AreEqual(artwork, selections.Single(s => s.Surface == Background).Content);
        Assert.AreEqual(title, selections.Single(s => s.Surface == Fragment).Content);
    }
}
