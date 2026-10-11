using WidgetRail.WidgetProtocol;

namespace WidgetRail.WidgetBridge;

public sealed record BridgePermissionPrompt(string RequestId, string WidgetId, string WidgetName, IReadOnlyList<string> PermissionNames);
public sealed record BridgePermissionTake(BridgePermissionPrompt? Prompt);
public sealed record BridgePermissionComplete(string RequestId, WidgetPermissionOutcome Outcome);
public sealed record BridgePermissionCurrent(bool Current);
