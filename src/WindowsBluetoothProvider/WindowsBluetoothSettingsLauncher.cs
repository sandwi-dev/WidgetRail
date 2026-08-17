using Windows.System;

namespace WidgetRail.WindowsBluetoothProvider;

internal sealed class WindowsBluetoothSettingsLauncher : IWindowsBluetoothSettingsLauncher
{
    private static readonly Uri BluetoothSettingsUri = new("ms-settings:bluetooth");

    public async Task<bool> OpenAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return await Launcher.LaunchUriAsync(BluetoothSettingsUri)
            .AsTask(cancellationToken).ConfigureAwait(false);
    }
}
