using Windows.Devices.Radios;
using Windows.Networking.Connectivity;

namespace WidgetRail.OverlayFrontend.WinUI.Shell;

/// <summary>Read-only system status. No radio control or provider mutation.</summary>
internal static class ShellStatusReader
{
    internal static Task<ShellStatusSnapshot> ReadAsync(CancellationToken token) => Task.Run(async () =>
    {
        var internet = ShellInternetStatus.Unknown;
        var bluetooth = ShellBluetoothStatus.Unknown;
        try
        {
            var profile = NetworkInformation.GetInternetConnectionProfile();
            internet = ShellStatusSnapshot.InternetLevel(profile is null ? 0 : (int)profile.GetNetworkConnectivityLevel());
        }
        catch (Exception error) when (error is not OutOfMemoryException) { }
        token.ThrowIfCancellationRequested();
        try
        {
            var radios = await Radio.GetRadiosAsync().AsTask(token).ConfigureAwait(false);
            bluetooth = ShellStatusSnapshot.BluetoothRadios(radios.Where(radio => radio.Kind == RadioKind.Bluetooth).Select(radio => (int)radio.State));
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch (Exception error) when (error is not OutOfMemoryException) { }
        token.ThrowIfCancellationRequested();
        return new ShellStatusSnapshot(internet, bluetooth);
    }, token);
}
