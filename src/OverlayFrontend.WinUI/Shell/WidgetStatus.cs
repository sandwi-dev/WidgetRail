namespace WidgetRail.OverlayFrontend.WinUI.Shell;

internal enum WidgetStatusKind { None, Notifications, Audio, RemotePlayback, Stopped, Failed, Recording }
internal readonly record struct WidgetStatus(int Pending = 0, bool Audio = false, bool RemotePlayback = false,
    bool Stopped = false, bool Failed = false, bool Recording = false)
{
    internal WidgetStatusKind Activity => Stopped ? WidgetStatusKind.Stopped : Recording ? WidgetStatusKind.Recording :
        Failed ? WidgetStatusKind.Failed : Audio ? WidgetStatusKind.Audio : RemotePlayback ? WidgetStatusKind.RemotePlayback : WidgetStatusKind.None;
    internal WidgetStatusKind Radial => Activity is WidgetStatusKind.Stopped or WidgetStatusKind.Failed or WidgetStatusKind.Recording ? Activity :
        Pending > 0 ? WidgetStatusKind.Notifications : Activity;
    internal string Count => Pending > 99 ? "99+" : Math.Max(0, Pending).ToString(System.Globalization.CultureInfo.CurrentCulture);
    internal string Describe(string name)
    {
        var details = new List<string>();
        if (Pending > 0) details.Add($"{Pending} pending notification{(Pending == 1 ? "" : "s")}");
        if (Stopped) details.Add("Stopped");
        else
        {
            if (Failed) details.Add("Needs attention");
            if (Recording) details.Add("Recording");
            if (Audio) details.Add("Playing audio");
            if (RemotePlayback) details.Add("Remote playback");
        }
        return details.Count == 0 ? name : name + ". " + string.Join(", ", details);
    }
}
