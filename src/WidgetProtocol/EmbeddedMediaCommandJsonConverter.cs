using System.Text.Json;
using System.Text.Json.Serialization;

namespace WidgetRail.WidgetProtocol;

internal sealed class EmbeddedMediaCommandJsonConverter : JsonConverter<EmbeddedMediaCommand>
{
    public override EmbeddedMediaCommand Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options) => reader.TokenType == JsonTokenType.String
            ? reader.GetString() switch
            {
                "navigatePrevious" => EmbeddedMediaCommand.Previous,
                "navigateNext" => EmbeddedMediaCommand.Next,
                "activate" => EmbeddedMediaCommand.Activate,
                "back" => EmbeddedMediaCommand.Back,
                "togglePlayback" => EmbeddedMediaCommand.TogglePlayback,
                "seekBackward" => EmbeddedMediaCommand.SeekBackward,
                "seekForward" => EmbeddedMediaCommand.SeekForward,
                _ => throw new JsonException("The embedded media command is not supported."),
            }
            : throw new JsonException("An embedded media command must be a string.");

    public override void Write(
        Utf8JsonWriter writer,
        EmbeddedMediaCommand value,
        JsonSerializerOptions options) => writer.WriteStringValue(value switch
        {
            EmbeddedMediaCommand.Previous => "navigatePrevious",
            EmbeddedMediaCommand.Next => "navigateNext",
            EmbeddedMediaCommand.Activate => "activate",
            EmbeddedMediaCommand.Back => "back",
            EmbeddedMediaCommand.TogglePlayback => "togglePlayback",
            EmbeddedMediaCommand.SeekBackward => "seekBackward",
            EmbeddedMediaCommand.SeekForward => "seekForward",
            _ => throw new JsonException("The embedded media command is not supported."),
        });
}
