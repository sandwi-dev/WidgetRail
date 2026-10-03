using System.Text.Json.Serialization;
using System.Text.Json;

namespace WidgetRail.OverlayFrontend.WinUI.Browser;

// Fixed host commands only; neither page nor widget code can supply CDP methods.
internal sealed record BrowserPointerEvent(string type, double x, double y,
    double deltaX = 0, double deltaY = 0, string? button = null, int clickCount = 0, int buttons = 0);
internal sealed record BrowserEvaluation(string expression);
internal sealed record BrowserHitPoint(double x, double y);
internal sealed record BrowserArgument(string value);
internal sealed record BrowserFieldCall(string objectId, string functionDeclaration, BrowserArgument[]? arguments = null, bool returnByValue = true);
internal sealed record BrowserObject(string objectId);
internal sealed record BrowserPinchGesture(double x, double y, double scaleFactor, int relativeSpeed = 800, string gestureSourceType = "mouse");
internal sealed record BrowserDragEvent(string type, double x, double y, JsonElement data);

[JsonSourceGenerationOptions(DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(BrowserPointerEvent))]
[JsonSerializable(typeof(BrowserEvaluation))]
[JsonSerializable(typeof(BrowserHitPoint))]
[JsonSerializable(typeof(BrowserFieldCall))]
[JsonSerializable(typeof(BrowserObject))]
[JsonSerializable(typeof(BrowserPinchGesture))]
[JsonSerializable(typeof(BrowserDragEvent))]
internal sealed partial class BrowserJsonContext : JsonSerializerContext;
