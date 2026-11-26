namespace WidgetRail.WidgetBridge;

public sealed record BridgeFilePickerPrompt(string RequestId, string WidgetId, string Title, bool Multiple)
{
    public string? CommitLabel { get; init; }
    public bool IsWindowChoice { get; init; }
}
public sealed record BridgeFilePickerTake(BridgeFilePickerPrompt? Prompt);
public sealed record BridgeFilePickerReference(string RequestId);
public sealed record BridgeFilePickerCurrent(bool Current);
/// <summary>Only the trusted frontend supplies native picker paths; widgets receive opaque file references.</summary>
public sealed record BridgeFilePickerCompletion(string RequestId, IReadOnlyList<string> Paths)
{
    public string? WindowMatch { get; init; }
}
