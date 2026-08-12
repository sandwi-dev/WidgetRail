using GameBarAlternative.WidgetSdk;

internal static class AppLibraryPresentationValidationTests
{
    internal static Task Run()
    {
        IdentityAndSourceOwnLocalRules();
        AvailabilityOwnsLaunchableStateRules();
        ArtworkOwnsRolesAndRevisions();
        MetadataOwnsProvenanceAndTimestamps();
        CapabilitiesOwnClosedUniqueActions();
        OperationsOwnClosedStateAndIdentifiers();
        SourceStatusOwnsObservationRules();
        ComposerOwnsOnlyCrossValueRelationships();
        return Task.CompletedTask;
    }

    private static void IdentityAndSourceOwnLocalRules()
    {
        True(WidgetAppLibraryPresentationIdentityValidator.IsValid(
            "Game", WidgetAppLibraryKind.Game));
        False(WidgetAppLibraryPresentationIdentityValidator.IsValid(
            "Game", (WidgetAppLibraryKind)999));
        False(WidgetAppLibraryPresentationIdentityValidator.IsValid(
            "bad\nname", WidgetAppLibraryKind.Game));
        True(WidgetAppLibrarySourceReferenceValidator.IsValid(
            new("source-epic", "Epic")));
        False(WidgetAppLibrarySourceReferenceValidator.IsValid(
            new("unsafe/source", "Epic")));
    }

    private static void AvailabilityOwnsLaunchableStateRules()
    {
        True(WidgetAppLibraryAvailabilityValidator.IsValid(
            new(WidgetAppLibraryAvailabilityState.Installed, true, "installed")));
        True(WidgetAppLibraryAvailabilityValidator.IsValid(
            new(WidgetAppLibraryAvailabilityState.Unavailable, false, "unavailable")));
        True(WidgetAppLibraryAvailabilityValidator.IsValid(
            new(WidgetAppLibraryAvailabilityState.StaleSource, false, "stale")));
        False(WidgetAppLibraryAvailabilityValidator.IsValid(
            new(WidgetAppLibraryAvailabilityState.Unavailable, true, "unavailable")));
        False(WidgetAppLibraryAvailabilityValidator.IsValid(
            new((WidgetAppLibraryAvailabilityState)999, false, "unknown")));
        False(WidgetAppLibraryAvailabilityValidator.IsValid(
            new(WidgetAppLibraryAvailabilityState.Installed, true, "unsafe status")));
    }

    private static void ArtworkOwnsRolesAndRevisions()
    {
        var tile = new WidgetAppLibraryArtwork(WidgetAppLibraryArtworkRole.Tile,
            "art-handle", "revision-1", WidgetAppLibraryArtworkFallback.Game);
        True(WidgetAppLibraryArtworkSetValidator.IsValid(new([tile])));
        False(WidgetAppLibraryArtworkSetValidator.IsValid(new([tile, tile])));
        False(WidgetAppLibraryArtworkSetValidator.IsValid(new([
            tile with { Revision = "bad/revision" },
        ])));
        False(WidgetAppLibraryArtworkSetValidator.IsValid(new([
            tile with { Role = (WidgetAppLibraryArtworkRole)999 },
        ])));
    }

    private static void MetadataOwnsProvenanceAndTimestamps()
    {
        var metadata = new WidgetAppLibraryMetadata("metadata-revision",
            new("Provider", "record-revision", "Provider catalog", 100))
        {
            LastPlayedAtUnixMilliseconds = 90,
            Categories = ["Action"],
        };
        True(WidgetAppLibraryMetadataValidator.IsValid(metadata));
        False(WidgetAppLibraryMetadataValidator.IsValid(metadata with
        {
            Attribution = metadata.Attribution with
                { RetrievedAtUnixMilliseconds = -1 },
        }));
        False(WidgetAppLibraryMetadataValidator.IsValid(metadata with
            { Categories = ["Action", "Action"] }));
        False(WidgetAppLibraryMetadataValidator.IsValid(metadata with
            { Revision = "bad/revision" }));
    }

    private static void CapabilitiesOwnClosedUniqueActions()
    {
        True(WidgetAppLibraryCapabilitySetValidator.IsValid(
            new([WidgetAppLibraryAction.Launch, WidgetAppLibraryAction.Update])));
        False(WidgetAppLibraryCapabilitySetValidator.IsValid(
            new([WidgetAppLibraryAction.Launch, WidgetAppLibraryAction.Launch])));
        False(WidgetAppLibraryCapabilitySetValidator.IsValid(
            new([(WidgetAppLibraryAction)999])));
    }

    private static void OperationsOwnClosedStateAndIdentifiers()
    {
        var operation = new WidgetAppLibraryOperation("operation-1",
            WidgetAppLibraryOperationKind.Update,
            WidgetAppLibraryOperationState.Running, "updating");
        True(WidgetAppLibraryOperationValidator.IsValid(operation));
        False(WidgetAppLibraryOperationValidator.IsValid(operation with
            { OperationId = "unsafe/id" }));
        False(WidgetAppLibraryOperationValidator.IsValid(operation with
            { State = (WidgetAppLibraryOperationState)999 }));
    }

    private static void SourceStatusOwnsObservationRules()
    {
        var source = new WidgetAppLibrarySource("source-epic", "Epic",
            WidgetAppLibrarySourceHealth.Healthy, 1, "healthy")
        {
            LastSuccessfulRefreshAtUnixMilliseconds = 100,
        };
        True(WidgetAppLibrarySourceValidator.IsValid(source));
        False(WidgetAppLibrarySourceValidator.IsValid(source with
            { Revision = -1 }));
        False(WidgetAppLibrarySourceValidator.IsValid(source with
            { AccountState = (WidgetAppLibrarySourceAccountState)999 }));
        False(WidgetAppLibrarySourceValidator.IsValid(source with
            { LastSuccessfulRefreshAtUnixMilliseconds = -1 }));
    }

    private static void ComposerOwnsOnlyCrossValueRelationships()
    {
        var installed = new WidgetAppLibraryAvailability(
            WidgetAppLibraryAvailabilityState.Installed, true, "installed");
        var unavailable = new WidgetAppLibraryAvailability(
            WidgetAppLibraryAvailabilityState.Unavailable, false, "unavailable");
        var stale = new WidgetAppLibraryAvailability(
            WidgetAppLibraryAvailabilityState.StaleSource, false, "stale");
        var launch = new WidgetAppLibraryCapabilitySet(
            [WidgetAppLibraryAction.Launch]);
        var none = new WidgetAppLibraryCapabilitySet([]);
        var update = new WidgetAppLibraryCapabilitySet(
            [WidgetAppLibraryAction.Update]);
        var operation = new WidgetAppLibraryOperation("operation-1",
            WidgetAppLibraryOperationKind.Update,
            WidgetAppLibraryOperationState.Running, "updating");

        True(WidgetAppLibraryPresentationComposer.RelationshipsAreValid(
            installed, launch, null));
        True(WidgetAppLibraryPresentationComposer.RelationshipsAreValid(
            unavailable, none, null));
        True(WidgetAppLibraryPresentationComposer.RelationshipsAreValid(
            stale, none, null));
        False(WidgetAppLibraryPresentationComposer.RelationshipsAreValid(
            installed, none, null));
        False(WidgetAppLibraryPresentationComposer.RelationshipsAreValid(
            unavailable, launch, null));
        False(WidgetAppLibraryPresentationComposer.RelationshipsAreValid(
            installed, launch, operation));
        True(WidgetAppLibraryPresentationComposer.RelationshipsAreValid(
            new(WidgetAppLibraryAvailabilityState.Installed, false, "installed"),
            update, operation));
        False(WidgetAppLibraryPresentationComposer.RelationshipsAreValid(
            new(WidgetAppLibraryAvailabilityState.Installed, false, "installed"),
            update, operation with { State = WidgetAppLibraryOperationState.Paused }));
        True(WidgetAppLibraryPresentationComposer.RelationshipsAreValid(
            new(WidgetAppLibraryAvailabilityState.Installed, false, "installed"),
            new([WidgetAppLibraryAction.Update, WidgetAppLibraryAction.Resume]),
            operation with { State = WidgetAppLibraryOperationState.Paused }));
    }

    private static void True(bool value)
    {
        if (!value) throw new InvalidOperationException("Expected valid value.");
    }

    private static void False(bool value)
    {
        if (value) throw new InvalidOperationException("Expected invalid value.");
    }
}
