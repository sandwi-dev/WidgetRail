namespace WidgetRail.Samples.DiscordPrototype;

internal sealed record DiscordAudioDevice(string Id,string Name);
internal sealed record DiscordVoiceSettings(bool InputAvailable,bool OutputAvailable,bool InputVolumeAvailable,
    DiscordAudioDevice[] InputDevices,DiscordAudioDevice[] OutputDevices,string? InputDeviceId,string? OutputDeviceId,double? InputVolume)
{
    internal void Validate()
    {
        static bool Devices(DiscordAudioDevice[]? values,bool available,string? selected)=>values is {Length:<=64}&&
            values.All(device=>device is not null&&device.Id is {Length:>0 and <=256}&&!device.Id.Any(char.IsControl)&&
                device.Name is {Length:<=120}&&!device.Name.Any(char.IsControl))&&
            values.Select(device=>device.Id).Distinct(StringComparer.Ordinal).Count()==values.Length&&
            (available?selected is not null&&values.Any(device=>device.Id==selected):values.Length==0&&selected is null);
        if(!Devices(InputDevices,InputAvailable,InputDeviceId)||!Devices(OutputDevices,OutputAvailable,OutputDeviceId)||
            (InputVolumeAvailable?InputVolume is not { } volume||!double.IsFinite(volume)||volume is <0 or >100:InputVolume is not null))
            throw new InvalidDataException("invalid_voice_settings");
    }
}
