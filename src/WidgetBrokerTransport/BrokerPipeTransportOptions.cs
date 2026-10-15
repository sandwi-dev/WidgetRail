namespace WidgetRail.PlatformBroker;

public sealed record BrokerPipeTransportOptions
{
    public int MaximumFrameBytes { get; init; } = 256 * 1024;
    public TimeSpan AcceptTimeout { get; init; } = TimeSpan.FromSeconds(5);
    public TimeSpan HandshakeTimeout { get; init; } = TimeSpan.FromSeconds(3);
    public TimeSpan RequestTimeout { get; init; } = TimeSpan.FromSeconds(3);
    public int MaximumInFlightRequests { get; init; } = 16;
    public int MaximumSubscriptions { get; init; } = 8;

    internal void Validate()
    {
        if (MaximumFrameBytes is < 1024 or > 256 * 1024)
            throw new ArgumentOutOfRangeException(nameof(MaximumFrameBytes));
        ValidateTimeout(AcceptTimeout, nameof(AcceptTimeout));
        ValidateTimeout(HandshakeTimeout, nameof(HandshakeTimeout));
        ValidateTimeout(RequestTimeout, nameof(RequestTimeout));
        if (MaximumInFlightRequests is < 1 or > 64)
            throw new ArgumentOutOfRangeException(nameof(MaximumInFlightRequests));
        if (MaximumSubscriptions is < 1 or > 32)
            throw new ArgumentOutOfRangeException(nameof(MaximumSubscriptions));
    }

    private static void ValidateTimeout(TimeSpan value, string name)
    {
        if (value <= TimeSpan.Zero || value > TimeSpan.FromMinutes(1))
            throw new ArgumentOutOfRangeException(name);
    }
}
