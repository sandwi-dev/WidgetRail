using System.Text.Json;

namespace WidgetRail.Samples.DiscordWebView.Domain;

internal sealed record DiscordCallMeasurement(string Phase,double? PingMilliseconds,double? AveragePingMilliseconds,string? Quality)
{
    internal static DiscordCallMeasurement Read(JsonElement value)
    {
        var phase=MeasurementJson.Text(value,"phase");
        if(phase is not("connected" or "connecting" or "reconnecting" or "disconnected" or "unavailable"))
            throw new InvalidDataException("Invalid connection phase.");
        var quality=MeasurementJson.Text(value,"quality");
        if(quality is not(null or "fine" or "average" or "bad"))throw new InvalidDataException("Invalid connection quality.");
        return new(phase,MeasurementJson.Number(value,"pingMilliseconds",60000),MeasurementJson.Number(value,"averagePingMilliseconds",60000),quality);
    }
}

internal sealed record DiscordIncomingMeasurement(bool Available,double? Width=null,double? Height=null,
    double? FramesPerSecond=null,double? FramesReceived=null,double? FramesDecoded=null,double? BytesReceived=null)
{
    internal static DiscordIncomingMeasurement Read(JsonElement value)=>MeasurementJson.Boolean(value,"available") is true
        ?new(true,MeasurementJson.Number(value,"width",16384,true),MeasurementJson.Number(value,"height",16384,true),
            MeasurementJson.Number(value,"framesPerSecond",240),MeasurementJson.Number(value,"framesReceived",1e12,true),
            MeasurementJson.Number(value,"framesDecoded",1e12,true),MeasurementJson.Number(value,"bytesReceived",1e12,true))
        :new(false);
}

internal sealed record DiscordPinMeasurement(string State,double? Width,double? Height,bool? Paused,double? TotalFrames,double? DroppedFrames)
{
    internal double? PresentedFrames=>TotalFrames is {} total&&DroppedFrames is {} dropped?total-dropped:null;
    internal static DiscordPinMeasurement Read(JsonElement value,string lease)
    {
        if(MeasurementJson.Text(value,"lease")!=lease)throw new InvalidDataException("Receiver lease changed.");
        var state=MeasurementJson.Text(value,"state");
        if(state is not("new" or "connecting" or "connected" or "disconnected" or "failed" or "closed"))
            throw new InvalidDataException("Invalid receiver phase.");
        var total=MeasurementJson.Number(value,"totalFrames",1e12,true);var dropped=MeasurementJson.Number(value,"droppedFrames",1e12,true);
        if(total is {} count&&dropped>count)throw new InvalidDataException("Invalid playback counters.");
        return new(state,MeasurementJson.Number(value,"width",16384,true),MeasurementJson.Number(value,"height",16384,true),
            MeasurementJson.Boolean(value,"paused"),total,dropped);
    }
}

internal static class DiscordConnectionMeasurementPolicy
{
    internal const int HistoryLimit=24;
    internal static double?[] AppendPing(IReadOnlyList<double?> previous,double? ping)=>previous.TakeLast(HistoryLimit-1).Append(ping).ToArray();

    internal static string Playback(DiscordPinMeasurement? pin,DiscordIncomingMeasurement? incoming,
        DiscordPinMeasurement? previousPin,DiscordIncomingMeasurement? previousIncoming,double elapsedSeconds)
    {
        if(pin is null)return "Pin measurements unavailable";
        if(pin.State is "failed" or "closed")return "Pin disconnected";
        if(pin.State=="disconnected")return "Pin reconnecting…";
        if(pin.Paused==true)return "Pin playback paused";
        if(pin.State!="connected")return "Connecting pin…";
        if(previousPin is null||elapsedSeconds<0.1||elapsedSeconds>10)return "Measuring playback…";
        var presented=Delta(pin.PresentedFrames,previousPin.PresentedFrames);
        if(presented>0)return $"Pin playback · {presented/elapsedSeconds:0.#} FPS";
        var decoded=Delta(incoming?.FramesDecoded,previousIncoming?.FramesDecoded);
        if(presented==0&&decoded>0)return "Discord is receiving video; the pin has not advanced";
        if(presented==0&&decoded==0)return "No new frames · stream may be idle or waiting";
        return "Playback rate unavailable";
    }
    private static double? Delta(double? current,double? previous)=>current is {} latest&&previous is {} prior&&latest>=prior?latest-prior:null;
}

internal static class MeasurementJson
{
    internal static double? Number(JsonElement value,string field,double maximum,bool integer=false)
    {
        if(value.ValueKind!=JsonValueKind.Object)throw new InvalidDataException("Invalid measurement.");
        if(!value.TryGetProperty(field,out var item)||item.ValueKind==JsonValueKind.Null)return null;
        if(item.ValueKind!=JsonValueKind.Number||!item.TryGetDouble(out var number)||!double.IsFinite(number)||number<0||number>maximum||integer&&number!=Math.Truncate(number))
            throw new InvalidDataException("Invalid measurement number.");
        return number;
    }
    internal static string? Text(JsonElement value,string field)
    {
        if(value.ValueKind!=JsonValueKind.Object)throw new InvalidDataException("Invalid measurement.");
        if(!value.TryGetProperty(field,out var item)||item.ValueKind==JsonValueKind.Null)return null;
        if(item.ValueKind!=JsonValueKind.String)throw new InvalidDataException("Invalid measurement value.");
        return item.GetString();
    }
    internal static bool? Boolean(JsonElement value,string field)
    {
        if(value.ValueKind!=JsonValueKind.Object)throw new InvalidDataException("Invalid measurement.");
        if(!value.TryGetProperty(field,out var item)||item.ValueKind==JsonValueKind.Null)return null;
        return item.ValueKind switch {JsonValueKind.True=>true,JsonValueKind.False=>false,_=>throw new InvalidDataException("Invalid measurement flag.")};
    }
}
