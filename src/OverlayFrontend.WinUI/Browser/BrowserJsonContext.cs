using System.Text.Json.Serialization;
using System.Text.Json;

namespace WidgetRail.OverlayFrontend.WinUI.Browser;

// Fixed host commands only; neither page nor widget code can supply CDP methods.
internal sealed record BrowserPointerEvent(string type, double x, double y,
    double deltaX = 0, double deltaY = 0, string? button = null, int clickCount = 0, int buttons = 0);
internal sealed record BrowserEvaluation(string expression, int? contextId = null, string? objectGroup = null);
internal sealed record BrowserAttachTarget(string targetId, bool flatten = true);
internal sealed record BrowserDetachTarget(string sessionId);
internal sealed record BrowserIsolatedWorld(string frameId, string worldName = "WidgetRail.Keyboard");
internal sealed record BrowserHitPoint(double x, double y);
internal sealed record BrowserSpatialFramePoint(int index, double x, double y);
internal sealed record BrowserArgument(string value);
internal sealed record BrowserFieldCall(string objectId, string functionDeclaration, BrowserArgument[]? arguments = null, bool returnByValue = true, string? objectGroup = null, bool awaitPromise = false);
internal sealed record BrowserObjectGroup(string objectGroup);
internal sealed record BrowserObject(string objectId);
internal sealed record BrowserInsertText(string text);
internal sealed record BrowserKeyEvent(string type, string key, string code, int windowsVirtualKeyCode, string? text = null, int modifiers = 0);
internal sealed record BrowserBackendNode(int backendNodeId);
internal sealed record BrowserSelectedFiles(string[] files, int backendNodeId);
internal sealed record BrowserPinchGesture(double x, double y, double scaleFactor, int relativeSpeed = 800, string gestureSourceType = "mouse");
internal sealed record BrowserDragEvent(string type, double x, double y, JsonElement data);

[JsonSourceGenerationOptions(DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(BrowserPointerEvent))]
[JsonSerializable(typeof(BrowserEvaluation))]
[JsonSerializable(typeof(BrowserAttachTarget))]
[JsonSerializable(typeof(BrowserDetachTarget))]
[JsonSerializable(typeof(BrowserIsolatedWorld))]
[JsonSerializable(typeof(BrowserHitPoint))]
[JsonSerializable(typeof(BrowserSpatialFramePoint))]
[JsonSerializable(typeof(BrowserFieldCall))]
[JsonSerializable(typeof(BrowserObject))]
[JsonSerializable(typeof(BrowserObjectGroup))]
[JsonSerializable(typeof(BrowserInsertText))]
[JsonSerializable(typeof(BrowserKeyEvent))]
[JsonSerializable(typeof(BrowserBackendNode))]
[JsonSerializable(typeof(BrowserSelectedFiles))]
[JsonSerializable(typeof(BrowserPinchGesture))]
[JsonSerializable(typeof(BrowserDragEvent))]
internal sealed partial class BrowserJsonContext : JsonSerializerContext;
