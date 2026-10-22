using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

internal sealed partial class IndexedOwnedBridgeProbeWidget
{
    private WidgetIndexedCollection<(bool Disabled, bool Tall), int>? returnProbe;
    private TaskCompletionSource? returnReadGate;
    private bool returnDetail;
    private bool returnDisabled;
    private bool returnHorizontal, returnTall;
    private int returnReads;
    private FocusGroupEntryRequest? returnEntry;
    private string returnPhase = "";
    private static readonly string[] ReturnCommands = ["mode", "open", "back", "exact", "hold", "release", "disabled", "replace", "other", "horizontal", "tall"];

    private WidgetView RenderReturnProbe() => new(UI.Stack("root",
        UI.Text(returnPhase, "return.phase"),
        UI.Text("Held reads: " + Volatile.Read(ref returnReads), "return.reads"),
        UI.Row("return.tools", ReturnCommands.Take(5).Select(command => UI.Button(command, "return-" + command, "return-" + command)).ToArray()),
        UI.Row("return.tools.more", ReturnCommands.Skip(5).Select(command => UI.Button(command, "return-" + command, "return-" + command)).ToArray()),
        returnDetail ? UI.Text("Detail", "return.detail") : UI.CollectionList("items", returnProbe!, returnHorizontal ? 180 : 48,
            "Return test items", returnHorizontal ? ScrollAxis.Horizontal : ScrollAxis.Vertical)),
        returnDetail ? "return-back" : "items") { FocusGroupEntryRequest = returnEntry };

    private void HandleReturnProbe(string action)
    {
        returnPhase = action;
        returnProbe ??= CreateIndexedCollection<(bool Disabled, bool Tall), int>("return.source", (false, false), 100, new()
        {
            ReadRange = async (_, start, count, token) =>
            {
                if (Volatile.Read(ref returnReadGate) is { } gate)
                {
                    Interlocked.Increment(ref returnReads); Invalidate();
                    await gate.Task.WaitAsync(token);
                }
                return Enumerable.Range(start, count).ToArray();
            },
            ItemKey = item => new("return.item." + item),
            RenderItem = (context, item, ids) => UI.Button("Return item " + item, "return-open", ids.Id("row"))
                .Disabled(context.Disabled && item == 75).Classes(context.Tall ? "return-row-tall" : "return-row"),
            OnAction = (_, _, action, _) => { HandleReturnProbe(action.ActionId); return ValueTask.CompletedTask; },
        });
        switch (action)
        {
            case "return-mode":
                // Each race starts with independent range authority, so a prior
                // deliberately canceled/failed read cannot contaminate its setup.
                returnDetail = false; returnEntry = null; returnDisabled = false; returnHorizontal = returnTall = false;
                returnProbe.PublishQuery((false, false), 100);
                break;
            case "return-open": returnDetail = true; returnEntry = null; break;
            case "return-back":
                returnDetail = false;
                returnEntry = CreateFocusRequest().ReturnToCollection(returnProbe, "items", returnProbe.FocusTarget("items", new("return.item.75"), 75));
                break;
            case "return-exact":
                returnEntry = CreateFocusRequest().EnterCollection(returnProbe, "items", returnProbe.FocusTarget("items", new("return.item.75"), 75));
                break;
            case "return-hold":
                returnReadGate = new(TaskCreationOptions.RunContinuationsAsynchronously);
                returnProbe.UpdateContent((returnDisabled, returnTall));
                break;
            case "return-release": Interlocked.Exchange(ref returnReadGate, null)?.TrySetResult(); break;
            case "return-disabled": returnDisabled = !returnDisabled; returnProbe.UpdateContent((returnDisabled, returnTall)); break;
            case "return-replace": returnProbe.PublishQuery((returnDisabled, returnTall), 100); break;
            case "return-horizontal": returnHorizontal = true; break;
            case "return-tall": returnTall = true; returnProbe.UpdateContent((returnDisabled, returnTall)); break;
        }
        Invalidate();
    }
}
