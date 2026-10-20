using WidgetRail.WidgetSdk;

namespace WidgetRail.WidgetRuntime;

internal static class ArtworkPayloadReader
{
    internal static WidgetArtworkSource? Read(EncodedArtworkPayload payload, bool allowFileSource)
    {
        if (payload.File is { } file)
        {
            if (!allowFileSource || payload.ContentType is not null || payload.ContentBase64 is not null ||
                file.AllowedRoot is not null || !WidgetRail.WidgetProtocol.ArtworkFileContract.IsValid(file))
                throw new WidgetProtocolViolationException("Worker returned invalid artwork source metadata.");
            return new WidgetFileArtwork(file);
        }
        if (payload.ContentType is null && payload.ContentBase64 is null) return null;
        if (payload.ContentType is null || payload.ContentBase64 is null ||
            WidgetEncodedArtworkContract.ParseContentType(payload.ContentType) is not { } type)
            throw new WidgetProtocolViolationException("Worker returned invalid artwork metadata.");
        try
        {
            var artwork = new WidgetEncodedArtwork(type, Convert.FromBase64String(payload.ContentBase64));
            if (!WidgetEncodedArtworkContract.IsValid(artwork))
                throw new WidgetProtocolViolationException("Worker returned invalid artwork bytes.");
            return artwork;
        }
        catch (FormatException error)
        { throw new WidgetProtocolViolationException("Worker returned malformed artwork bytes.", error); }
    }
}
