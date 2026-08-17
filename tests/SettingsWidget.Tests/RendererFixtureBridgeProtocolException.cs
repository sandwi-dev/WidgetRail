namespace WidgetRail.WidgetBridge;

// The renderer fixture compiles the production style resolver source without
// pulling the Windows-only bridge executable into this cross-platform suite.
internal sealed class BridgeProtocolException(string message) : Exception(message);
