namespace GameBarAlternative.WidgetSdk;

internal static class WidgetAppLibraryValueValidator
{
    internal const int MaximumOpaqueIdLength = 128;

    internal static bool IsOpaqueId(string? value) =>
        value is { Length: > 0 and <= MaximumOpaqueIdLength } &&
        !string.IsNullOrWhiteSpace(value) && value.All(character =>
            char.IsAsciiLetterOrDigit(character) || character is '.' or '_' or '-');

    internal static bool IsDisplayValue(string? value, int maximum) =>
        !string.IsNullOrWhiteSpace(value) && value.Length <= maximum &&
        !value.Any(char.IsControl);

    internal static bool IsStatusCode(string? value) =>
        value is { Length: > 0 and <= 48 } && value.All(character =>
            char.IsAsciiLetterOrDigit(character) || character is '_' or '-' or '.');
}

internal static class WidgetAppLibraryPresentationIdentityValidator
{
    internal static bool IsValid(string? displayName, WidgetAppLibraryKind kind) =>
        Enum.IsDefined(kind) &&
        WidgetAppLibraryValueValidator.IsDisplayValue(displayName, 160);
}

internal static class WidgetAppLibrarySourceReferenceValidator
{
    internal static bool IsValid(WidgetAppLibrarySourceReference? value) =>
        value is not null &&
        WidgetAppLibraryValueValidator.IsOpaqueId(value.SourceId) &&
        WidgetAppLibraryValueValidator.IsDisplayValue(value.DisplayName, 64);
}

internal static class WidgetAppLibraryAvailabilityValidator
{
    internal static bool IsValid(WidgetAppLibraryAvailability? value) =>
        value is not null && Enum.IsDefined(value.State) &&
        WidgetAppLibraryValueValidator.IsStatusCode(value.StatusCode) &&
        (value.State == WidgetAppLibraryAvailabilityState.Installed ||
            !value.IsLaunchable);
}

internal static class WidgetAppLibraryArtworkSetValidator
{
    internal static bool IsValid(WidgetAppLibraryArtworkSet? value)
    {
        if (value?.Items is null || value.Items.Count > 4) return false;
        var roles = new HashSet<WidgetAppLibraryArtworkRole>();
        foreach (var item in value.Items)
        {
            if (item is null || !Enum.IsDefined(item.Role) ||
                !Enum.IsDefined(item.Fallback) || !roles.Add(item.Role) ||
                !WidgetAppLibraryValueValidator.IsOpaqueId(item.Handle) ||
                !WidgetAppLibraryValueValidator.IsOpaqueId(item.Revision))
                return false;
        }
        return true;
    }
}

internal static class WidgetAppLibraryMetadataValidator
{
    internal static bool IsValid(WidgetAppLibraryMetadata? value)
    {
        if (value is null) return true;
        var attribution = value.Attribution;
        return attribution is not null &&
            WidgetAppLibraryValueValidator.IsOpaqueId(value.Revision) &&
            WidgetAppLibraryValueValidator.IsDisplayValue(attribution.Provider, 64) &&
            WidgetAppLibraryValueValidator.IsOpaqueId(attribution.RecordRevision) &&
            WidgetAppLibraryValueValidator.IsDisplayValue(
                attribution.Attribution, 160) &&
            attribution.RetrievedAtUnixMilliseconds >= 0 &&
            (value.SortTitle is null ||
                WidgetAppLibraryValueValidator.IsDisplayValue(value.SortTitle, 160)) &&
            (value.Version is null ||
                WidgetAppLibraryValueValidator.IsDisplayValue(value.Version, 64)) &&
            value.LastPlayedAtUnixMilliseconds is not < 0 &&
            value.PlaytimeMinutes is not < 0 and not > 100_000_000 &&
            value.Categories is { Count: <= 32 } &&
            value.Categories.Distinct(StringComparer.Ordinal).Count() ==
                value.Categories.Count &&
            value.Categories.All(category =>
                WidgetAppLibraryValueValidator.IsDisplayValue(category, 64)) &&
            (value.Description is null || value.Description.Length <= 2048 &&
                !value.Description.Any(char.IsControl));
    }
}

internal static class WidgetAppLibraryCapabilitySetValidator
{
    internal static bool IsValid(WidgetAppLibraryCapabilitySet? value) =>
        value?.Actions is { Count: <= 13 } &&
        value.Actions.Distinct().Count() == value.Actions.Count &&
        value.Actions.All(Enum.IsDefined);
}

internal static class WidgetAppLibraryOperationValidator
{
    internal static bool IsValid(WidgetAppLibraryOperation? value) =>
        value is null || Enum.IsDefined(value.Kind) && Enum.IsDefined(value.State) &&
        WidgetAppLibraryValueValidator.IsOpaqueId(value.OperationId) &&
        WidgetAppLibraryValueValidator.IsStatusCode(value.StatusCode);
}

internal static class WidgetAppLibrarySourceValidator
{
    internal static bool IsValid(WidgetAppLibrarySource? value) =>
        value is not null && Enum.IsDefined(value.Health) &&
        Enum.IsDefined(value.AccountState) && value.Revision >= 0 &&
        value.LastSuccessfulRefreshAtUnixMilliseconds is not < 0 &&
        WidgetAppLibraryValueValidator.IsOpaqueId(value.SourceId) &&
        WidgetAppLibraryValueValidator.IsDisplayValue(value.DisplayName, 64) &&
        WidgetAppLibraryValueValidator.IsStatusCode(value.StatusCode);
}

internal static class WidgetAppLibraryPresentationComposer
{
    internal static bool RelationshipsAreValid(
        WidgetAppLibraryAvailability availability,
        WidgetAppLibraryCapabilitySet capabilities,
        WidgetAppLibraryOperation? operation)
    {
        var launch = capabilities.Supports(WidgetAppLibraryAction.Launch);
        if (availability.IsLaunchable != launch) return false;
        if (operation is null) return true;
        return capabilities.Supports(ActionFor(operation.Kind)) &&
            (operation.State != WidgetAppLibraryOperationState.Paused ||
                capabilities.Supports(WidgetAppLibraryAction.Resume));
    }

    private static WidgetAppLibraryAction ActionFor(WidgetAppLibraryOperationKind kind) =>
        kind switch
        {
            WidgetAppLibraryOperationKind.Launch => WidgetAppLibraryAction.Launch,
            WidgetAppLibraryOperationKind.Install => WidgetAppLibraryAction.Install,
            WidgetAppLibraryOperationKind.Update => WidgetAppLibraryAction.Update,
            WidgetAppLibraryOperationKind.Repair => WidgetAppLibraryAction.Repair,
            WidgetAppLibraryOperationKind.Move => WidgetAppLibraryAction.Move,
            WidgetAppLibraryOperationKind.Import => WidgetAppLibraryAction.Import,
            WidgetAppLibraryOperationKind.Uninstall => WidgetAppLibraryAction.Uninstall,
            WidgetAppLibraryOperationKind.CloudSync => WidgetAppLibraryAction.CloudSync,
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };
}
