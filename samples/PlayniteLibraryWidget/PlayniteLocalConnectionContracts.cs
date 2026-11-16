using WidgetRail.Playnite;

namespace WidgetRail.Samples.PlayniteLibrary;

// Not a record: automatic ToString must never print the credential.
internal sealed class PlayniteLocalConnection(
    string bindingId, ConnectionDescriptor descriptor, string credential, Func<bool> isCurrent)
{
    internal string BindingId { get; } = bindingId;
    internal ConnectionDescriptor Descriptor { get; } = descriptor;
    internal string Credential { get; } = credential;
    internal Uri Origin => new($"http://127.0.0.1:{Descriptor.Port}/");
    internal bool IsCurrent() => isCurrent();
}

internal interface IPlayniteLocalConnectionSource
{
    ValueTask<PlayniteLocalConnection?> OpenAsync(CancellationToken token);
}

internal interface IPlayniteConnectedTransport
{
    bool UsesProfileConnection { get; }
    ValueTask<PlayniteBridgeResponse?> SendConnectedAsync(PlayniteBridgeCommand command, CancellationToken token);
    void ResetConnection();
}
