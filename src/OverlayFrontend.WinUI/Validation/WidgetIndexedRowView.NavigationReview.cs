using System.Text.Json;
using WidgetRail.OverlayFrontend.WinUI.Collections;
namespace WidgetRail.OverlayFrontend.WinUI.Presentation;
public sealed partial class WidgetIndexedRowView
{
    private static readonly string? ReviewPath = Shell.FrontendArguments.Value(Environment.GetCommandLineArgs(), "--navigation-review-output");
    private static int reviewEvents;
    partial void TraceReviewRow(string phase)
    {
        if (ReviewPath is null || ++reviewEvents > 6000) return;
        File.AppendAllText(ReviewPath, JsonSerializer.Serialize(new { time = Environment.TickCount64, phase,
            instance = System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(this), IsLoaded,
            row = (Row as WidgetIndexedRow)?.Item.Key, root = (Row as WidgetIndexedRow)?.Item.Root.Id,
            previous = appliedRow?.Item.Key, hadPresenter = presenter is not null,
            stack = phase == "retire" && reviewEvents < 300 ? Environment.StackTrace : null }) + Environment.NewLine);
    }
}
