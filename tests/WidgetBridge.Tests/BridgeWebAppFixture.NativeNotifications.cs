using System.Text.Json;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

internal sealed partial class WebAppFrameworkFixture
{
    private sealed record NativeNoticeState(bool Browser = true, int Handled = 0, string Status = "ready");
    private readonly WidgetModel<NativeNoticeState> nativeNotice;
    private WidgetWebSession? nativeSession;
    private WidgetView RenderNativeNotice() => new(UI.Grid("native.root", [GridTrack.Auto(), GridTrack.Star()], [GridTrack.Star()],
        [UI.Row("native.commands", UI.Text(nativeNotice.Value.Status + ":" + nativeNotice.Value.Handled, "native.status"),
            UI.Button("Park browser", "native-hide", "native-hide"), UI.Button("Show browser", "native-show", "native-show")).InGrid(),
         (nativeNotice.Value.Browser ? (WidgetElement)UI.WebView(nativeSession!.Reference, "native.page") : UI.Text("Native conversation", "native.conversation")).InGrid(row: 1)]),
        "native-hide", Surface: new() { Mode = WidgetSurfaceMode.Standard, WidthMode = WidgetSurfaceAxisMode.Preferred,
            HeightMode = WidgetSurfaceAxisMode.Preferred, PreferredWidth = 1100, PreferredHeight = 760, MinimumWidth = 480, MinimumHeight = 360 });

    private async ValueTask NativeNotificationActionAsync(string action, CancellationToken token)
    {
        if (action == "native-open" && nativeSession is null)
        {
            nativeSession = await HostServices.WebSessions.OpenAsync(new("native-notification", "Synthetic notification session",
                WebSessionSource.Remote("https://shell.fixture.invalid/native-notification")) { NativeNotificationIntents = true }, token);
            nativeNotice.Update(_ => new(Status: "opened"));
        }
        else if (action == "native-hide") nativeNotice.Update(state => state with { Browser = false, Status = "parked" });
        else if (action == "native-show") nativeNotice.Update(state => state with { Browser = true, Status = "browser" });
    }

    private ValueTask<WidgetIntentResult> NativeNotificationIntent(WidgetIntentRequest request)
    {
        var payload = request.Payload.Deserialize<WebNotificationIntentPayload>();
        if (!request.IsWellFormed() || payload?.IsWellFormed() != true || payload.SessionId != nativeSession?.Reference.Id)
            return ValueTask.FromResult(WidgetIntentResult.Rejected);
        if (payload.Tag == "native-known")
        {
            nativeNotice.Update(state => state with { Browser = false, Handled = state.Handled + 1, Status = "handled" });
            return ValueTask.FromResult(WidgetIntentResult.Accepted);
        }
        // The production receiver can attach its existing browser route before
        // declining, so the ordinary source callback still owns fallback.
        nativeNotice.Update(state => state with { Browser = true, Status = "fallback" });
        return ValueTask.FromResult(WidgetIntentResult.Rejected);
    }

    protected override async ValueTask OnDestroyingAsync(CancellationToken token)
    { if (Interlocked.Exchange(ref nativeSession, null) is { } previous) await previous.DisposeAsync(); }
}
