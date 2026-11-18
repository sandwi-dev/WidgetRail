using WidgetRail.PlatformBroker;

namespace WidgetRail.WidgetBridge;

public sealed record BridgeReplayPoll(HostReplayReport? Report = null);
public sealed record BridgeReplayPower(long Revision, bool Suspended);
