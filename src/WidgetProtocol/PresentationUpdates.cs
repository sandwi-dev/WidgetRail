using System.Text.Json;
using System.Text.Json.Serialization;

namespace WidgetRail.WidgetProtocol;

[Flags]
[JsonConverter(typeof(JsonStringEnumConverter<PresentationPropertyImpact>))]
public enum PresentationPropertyImpact
{
    None = 0,
    Authority = 1,
    Paint = 2,
    MeasureLayout = 4,
    Accessibility = 8,
    Interaction = 16,
    Resource = 32,
    SurfacePlacement = 64,
}

[JsonConverter(typeof(JsonStringEnumConverter<PresentationProperty>))]
public enum PresentationProperty
{
    ActiveInputScopeId,
    InitialFocusId,
    QuickActions,
    Surface,
    AdvancedPresentation,
    VisibleWhen,
    Text,
    AccessibilityLabel,
    AccessibilityValue,
    ActionId,
    TextEntryValue,
    TextEntryPlaceholder,
    TextEntryMaximumLength,
    Value,
    Minimum,
    Maximum,
    Step,
    ValueChangedActionId,
    SliderInteractionMode,
    ImageSource,
    ArtworkHandle,
    ImageFit,
    Glyph,
    IndicatorSize,
    ActionSurfaceOrientation,
    GridMinimumColumnWidth,
    GridMaximumColumns,
    IsDisabled,
    IsSelected,
    IsBusy,
    FocusPersistenceId,
    Focus,
    InputScopeId,
    ScrollAxis,
    ScrollNearStartActionId,
    ScrollNearEndActionId,
    ScrollPaginationThreshold,
    VirtualCollectionWindow,
    CollectionAnchorKey,
    CollectionItemKey,
    AdvancedPresentationSlot,
    StyleClasses,
    Shortcuts,
}

[JsonConverter(typeof(JsonStringEnumConverter<PresentationUpdateOperationKind>))]
public enum PresentationUpdateOperationKind
{
    SetProperties,
    InsertChild,
    RemoveChild,
    MoveChild,
    ReplaceSubtree,
}

public sealed record PresentationUpdateCapabilities
{
    public int MaximumProtocolVersion { get; init; }
    public int MaximumOperationsPerBatch { get; init; }
    public int MaximumBatchBytes { get; init; }

    [JsonIgnore]
    public bool SupportsAtomicUpdates =>
        MaximumProtocolVersion >= ProtocolConstants.AtomicPresentationUpdateVersion &&
        MaximumOperationsPerBatch > 0 && MaximumBatchBytes > 0;

    public static PresentationUpdateCapabilities None { get; } = new();
    public static PresentationUpdateCapabilities Current { get; } = new()
    {
        MaximumProtocolVersion = ProtocolConstants.AtomicPresentationUpdateVersion,
        MaximumOperationsPerBatch = ProtocolConstants.MaximumPresentationUpdateOperations,
        MaximumBatchBytes = ProtocolConstants.MaximumPresentationUpdateBytes,
    };
}

public sealed record PresentationPropertyChange(
    PresentationProperty Property,
    JsonElement Value);

public sealed record PresentationUpdateOperation
{
    public required PresentationUpdateOperationKind Kind { get; init; }
    public string? TargetId { get; init; }
    public string? ParentId { get; init; }
    public string? ChildId { get; init; }
    public int? Index { get; init; }
    public IReadOnlyList<PresentationPropertyChange>? Properties { get; init; }
    public ViewNode? Subtree { get; init; }
}

public sealed record PresentationUpdateBatch
{
    public int ProtocolVersion { get; init; } =
        ProtocolConstants.AtomicPresentationUpdateVersion;
    public required string WidgetInstanceId { get; init; }
    public required string PresentationGeneration { get; init; }
    public required long BaseSequence { get; init; }
    public required long Sequence { get; init; }
    public IReadOnlyList<PresentationUpdateOperation> Operations { get; init; } = [];
}

public static class PresentationPropertyMetadata
{
    public static PresentationPropertyImpact Impact(PresentationProperty property) => property switch
    {
        PresentationProperty.ActiveInputScopeId or
        PresentationProperty.InitialFocusId or
        PresentationProperty.QuickActions =>
            PresentationPropertyImpact.Authority |
            PresentationPropertyImpact.Interaction |
            PresentationPropertyImpact.Accessibility,
        PresentationProperty.Surface or PresentationProperty.AdvancedPresentation =>
            PresentationPropertyImpact.SurfacePlacement |
            PresentationPropertyImpact.MeasureLayout |
            PresentationPropertyImpact.Paint |
            PresentationPropertyImpact.Accessibility,
        PresentationProperty.VisibleWhen =>
            PresentationPropertyImpact.MeasureLayout |
            PresentationPropertyImpact.Paint |
            PresentationPropertyImpact.Interaction |
            PresentationPropertyImpact.Accessibility,
        PresentationProperty.Text or
        PresentationProperty.TextEntryValue or
        PresentationProperty.TextEntryPlaceholder =>
            PresentationPropertyImpact.MeasureLayout |
            PresentationPropertyImpact.Paint |
            PresentationPropertyImpact.Accessibility,
        PresentationProperty.AccessibilityLabel or
        PresentationProperty.AccessibilityValue =>
            PresentationPropertyImpact.Accessibility,
        PresentationProperty.Value =>
            PresentationPropertyImpact.Paint |
            PresentationPropertyImpact.Accessibility,
        PresentationProperty.ImageSource or PresentationProperty.ArtworkHandle =>
            PresentationPropertyImpact.Resource |
            PresentationPropertyImpact.Paint |
            PresentationPropertyImpact.Accessibility,
        PresentationProperty.IsDisabled or
        PresentationProperty.IsSelected or
        PresentationProperty.IsBusy =>
            PresentationPropertyImpact.Paint |
            PresentationPropertyImpact.Interaction |
            PresentationPropertyImpact.Accessibility,
        PresentationProperty.ActionId or
        PresentationProperty.ValueChangedActionId or
        PresentationProperty.Shortcuts or
        PresentationProperty.InputScopeId or
        PresentationProperty.Focus or
        PresentationProperty.FocusPersistenceId or
        PresentationProperty.ScrollNearStartActionId or
        PresentationProperty.ScrollNearEndActionId =>
            PresentationPropertyImpact.Authority |
            PresentationPropertyImpact.Interaction |
            PresentationPropertyImpact.Accessibility,
        PresentationProperty.StyleClasses or
        PresentationProperty.GridMinimumColumnWidth or
        PresentationProperty.GridMaximumColumns or
        PresentationProperty.Minimum or
        PresentationProperty.Maximum or
        PresentationProperty.Step or
        PresentationProperty.SliderInteractionMode or
        PresentationProperty.ImageFit or
        PresentationProperty.Glyph or
        PresentationProperty.IndicatorSize or
        PresentationProperty.ActionSurfaceOrientation or
        PresentationProperty.ScrollAxis or
        PresentationProperty.ScrollPaginationThreshold or
        PresentationProperty.VirtualCollectionWindow or
        PresentationProperty.CollectionAnchorKey or
        PresentationProperty.CollectionItemKey or
        PresentationProperty.AdvancedPresentationSlot or
        PresentationProperty.TextEntryMaximumLength =>
            PresentationPropertyImpact.MeasureLayout |
            PresentationPropertyImpact.Paint |
            PresentationPropertyImpact.Interaction |
            PresentationPropertyImpact.Accessibility,
        _ => throw new ArgumentOutOfRangeException(nameof(property), property, null),
    };

    internal static bool IsDocument(PresentationProperty property) => property is
        PresentationProperty.ActiveInputScopeId or
        PresentationProperty.InitialFocusId or
        PresentationProperty.QuickActions or
        PresentationProperty.Surface or
        PresentationProperty.AdvancedPresentation;
}

public static class PresentationUpdateJson
{
    private static readonly JsonSerializerOptions Options = CreateOptions();

    public static byte[] Serialize(PresentationUpdateBatch batch)
    {
        var errors = PresentationUpdateValidator.Validate(batch);
        if (errors.Count != 0) throw new ProtocolValidationException(errors);
        var bytes = JsonSerializer.SerializeToUtf8Bytes(batch, Options);
        if (bytes.Length > ProtocolConstants.MaximumPresentationUpdateBytes)
            throw new ProtocolValidationException([
                new("$", "update_too_large",
                    $"Update payload may not exceed {ProtocolConstants.MaximumPresentationUpdateBytes} bytes."),
            ]);
        return bytes;
    }

    public static PresentationUpdateBatch Deserialize(ReadOnlySpan<byte> payload)
    {
        if (payload.Length > ProtocolConstants.MaximumPresentationUpdateBytes)
            throw new ProtocolValidationException([
                new("$", "update_too_large",
                    $"Update payload may not exceed {ProtocolConstants.MaximumPresentationUpdateBytes} bytes."),
            ]);
        var batch = JsonSerializer.Deserialize<PresentationUpdateBatch>(payload, Options)
            ?? throw new JsonException("The update payload was null.");
        var errors = PresentationUpdateValidator.Validate(batch);
        if (errors.Count != 0) throw new ProtocolValidationException(errors);
        return batch;
    }

    internal static JsonElement Value<T>(T value) =>
        JsonSerializer.SerializeToElement(value, Options);

    internal static T? Read<T>(JsonElement value) =>
        value.Deserialize<T>(Options);

    internal static JsonSerializerOptions SerializerOptions => Options;

    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = false,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
            WriteIndented = false,
        };
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        return options;
    }
}
