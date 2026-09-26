namespace WidgetRail.WidgetBridge;

// Compile the production resolver in fixture exporters without the Windows
// bridge executable and its runtime dependencies.
internal sealed class BridgeProtocolException(string message) : Exception(message);
