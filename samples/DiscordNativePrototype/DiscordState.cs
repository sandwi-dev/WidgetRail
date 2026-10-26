namespace WidgetRail.Samples.DiscordPrototype;

internal sealed record DiscordCapabilities(bool Voice, bool Mute, bool Deafen, bool Watch, bool Share, bool StopShare);
internal sealed record DiscordChannel(string Id, string GuildId, string Name, string Guild);
internal sealed record DiscordParticipant(string Id, string Name, bool Muted, bool Deafened, bool Speaking);
internal sealed record DiscordStream(string Key, string UserId, string Name);
internal sealed record DiscordState(int Revision, bool Ready, string Account, DiscordCapabilities Capabilities,
    DiscordChannel[] Channels, string? ChannelId, string ChannelName, bool Muted, bool Deafened,
    DiscordParticipant[] Participants, DiscordStream[] Streams, bool Sharing, bool SharePending, string? ShareError, string RelayState)
{
    internal void Validate()
    {
        if (Revision != 1 || Capabilities is null || Channels is null || Participants is null || Streams is null ||
            Channels.Length > 1000 || Participants.Length > 100 || Streams.Length > 50 || Account is null || Account.Length > 32 ||
            Channels.Any(c => c is null || !Id(c.Id) || !Id(c.GuildId) || !Label(c.Name) || !Label(c.Guild)) ||
            Participants.Any(p => p is null || !Id(p.Id) || !Label(p.Name)) ||
            Streams.Any(s => s is null || !Id(s.UserId) || !Label(s.Name) || s.Key is not { Length: > 0 and <= 100 }) ||
            Channels.Select(c => c.Id).Distinct().Count() != Channels.Length ||
            Streams.Select(s => s.Key).Distinct().Count() != Streams.Length)
            throw new InvalidDataException("invalid_adapter_state");
    }
    private static bool Id(string? id) => id is { Length: > 0 and <= 32 } && id.All(char.IsAsciiDigit);
    private static bool Label(string? label) => label is { Length: <= 120 } && !label.Any(char.IsControl);
}
