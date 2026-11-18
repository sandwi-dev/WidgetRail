using WinUIEx.Messaging;

namespace WidgetRail.OverlayFrontend.WinUI;

public sealed partial class MainWindow
{
    private WindowMessageMonitor? replayPowerMessages;
    private void InitializeReplayPower()
    {
        replayPowerMessages = new(this);
        replayPowerMessages.WindowMessageReceived += (_, args) =>
        {
            // WM_POWERBROADCAST is observed through the existing window monitor;
            // no new window procedure or input router is installed.
            if (cleanupStarted || args.Message.MessageId != 0x0218 || RootFrame.Content is not Shell.OverlayShellPage page) return;
            switch (unchecked((uint)(nint)args.Message.WParam))
            {
                case 0x04: page.SetReplaySuspended(true); break; // PBT_APMSUSPEND
                case 0x07: // PBT_APMRESUMESUSPEND (can follow automatic resume)
                case 0x12: page.SetReplaySuspended(false); break; // PBT_APMRESUMEAUTOMATIC
            }
        };
    }
}
