using System.Globalization;

namespace WidgetRail.OverlayFrontend.WinUI.Shell;

internal enum ShellInternetStatus { Unknown, Offline, Limited, Online }
internal enum ShellBluetoothStatus { Unknown, Unavailable, Off, On }

internal sealed record ShellStatusSnapshot(ShellInternetStatus Internet, ShellBluetoothStatus Bluetooth)
{
    internal static ShellStatusSnapshot Unknown { get; } = new(ShellInternetStatus.Unknown, ShellBluetoothStatus.Unknown);
    internal static ShellInternetStatus InternetLevel(int? level) => level switch
    { 0 => ShellInternetStatus.Offline, 1 or 2 => ShellInternetStatus.Limited, 3 => ShellInternetStatus.Online, _ => ShellInternetStatus.Unknown };

    // RadioState values: Unknown=0, On=1, Off=2, Disabled=3. One enabled
    // adapter is sufficient; missing information is never labelled powered off.
    internal static ShellBluetoothStatus BluetoothRadios(IEnumerable<int> states)
    {
        var any = false; var unknown = false;
        foreach (var state in states)
        {
            any = true;
            if (state == 1) return ShellBluetoothStatus.On;
            if (state is not (2 or 3)) unknown = true;
        }
        return !any ? ShellBluetoothStatus.Unavailable : unknown ? ShellBluetoothStatus.Unknown : ShellBluetoothStatus.Off;
    }

    internal string InternetDescription => Internet switch
    {
        ShellInternetStatus.Online => "Internet access",
        ShellInternetStatus.Offline => "No internet access",
        ShellInternetStatus.Limited => "Limited network access or sign-in required",
        _ => "Internet status unavailable",
    };
    internal string BluetoothDescription => Bluetooth switch
    {
        ShellBluetoothStatus.On => "Bluetooth on",
        ShellBluetoothStatus.Off => "Bluetooth off",
        ShellBluetoothStatus.Unavailable => "Bluetooth not available",
        _ => "Bluetooth status unavailable",
    };
    internal string Describe(DateTime time, CultureInfo culture) =>
        $"{time.ToString("t", culture)}, {time.ToString("d", culture)}. {InternetDescription}. {BluetoothDescription}.";
}
